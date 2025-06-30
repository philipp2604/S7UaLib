using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Client;
using S7UaLib.S7.Structure;
using S7UaLib.UA;
using S7UaLib.UnitTests.Helpers;
using System.Collections;

namespace S7UaLib.UnitTests.Client;

[Trait("Category", "Unit")]
public class S7UaClientUnitTests
{
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7UaClient>> _mockLogger;
    private readonly ApplicationConfiguration _appConfig;
    private readonly Action<IList, IList> _validateResponse;
    private readonly Mock<ISession> _mockSession;

    public S7UaClientUnitTests()
    {
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<S7UaClient>>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);

        _appConfig = new ApplicationConfiguration
        {
            ApplicationName = "Test S7UaClient",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(),
                TrustedPeerCertificates = new CertificateTrustList(),
                AutoAcceptUntrustedCertificates = true
            },
            ClientConfiguration = new ClientConfiguration()
        };

        _validateResponse = (_, _) => { };

        _mockSession = new Mock<ISession>();
        _mockSession.SetupGet(s => s.Connected).Returns(true);
    }

    private S7UaClient CreateSut()
    {
        return new S7UaClient(_appConfig, _validateResponse, _mockLoggerFactory.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullAppConfig_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new S7UaClient(null!, _validateResponse));
    }

    [Fact]
    public void Constructor_WithNullValidateAction_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new S7UaClient(_appConfig, null!));
    }

    #endregion Constructor Tests

    #region Connection Tests

    [Fact]
    public async Task ConnectAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var client = CreateSut();
        client.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync("opc.tcp://localhost:4840"));
    }

    [Fact]
    public void Disconnect_WhenConnected_ClosesSessionAndCleansUpHandlers()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<ISession>();
        var reconnectHandler = new SessionReconnectHandler(true, -1);

        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        PrivateFieldHelpers.SetPrivateField(client, "_reconnectHandler", reconnectHandler);

        // Act
        client.Disconnect();

        // Assert
        mockSession.Verify(s => s.Close(It.IsAny<bool>()), Times.Once);
        mockSession.Verify(s => s.Dispose(), Times.Once);

        Assert.Null(PrivateFieldHelpers.GetPrivateField(client, "_reconnectHandler"));
        Assert.Null(PrivateFieldHelpers.GetPrivateField(client, "_session"));

        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Disconnect_FiresDisconnectingAndDisconnectedEvents()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<ISession>();
        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        PrivateFieldHelpers.SetPrivateField(client, "_reconnectHandler", new Mock<SessionReconnectHandler>(true, -1).Object);

        bool disconnectingFired = false;
        bool disconnectedFired = false;
        client.Disconnecting += (s, e) => disconnectingFired = true;
        client.Disconnected += (s, e) => disconnectedFired = true;

        // Act
        client.Disconnect();

        // Assert
        Assert.True(disconnectingFired, "Disconnecting event should have been fired.");
        Assert.True(disconnectedFired, "Disconnected event should have been fired.");
    }

    [Fact]
    public void IsConnected_ReflectsSessionState()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<ISession>();

        // Act & Assert
        Assert.False(client.IsConnected, "Should not be connected initially.");

        // Act & Assert
        mockSession.Setup(s => s.Connected).Returns(true);
        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        Assert.True(client.IsConnected, "Should be connected when session is connected.");

        // Act & Assert
        mockSession.Setup(s => s.Connected).Returns(false);
        Assert.False(client.IsConnected, "Should not be connected when session is disconnected.");
    }

    [Fact]
    public void Session_KeepAlive_WithBadStatus_InitiatesReconnectLogic()
    {
        // Arrange
        var client = CreateSut();
        client.ReconnectPeriod = 1000;

        var mockSession = new Mock<ISession>();

        var mockReconnectHandler = new Mock<SessionReconnectHandler>(true, -1);

        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        PrivateFieldHelpers.SetPrivateField(client, "_reconnectHandler", mockReconnectHandler.Object);

        bool reconnectingFired = false;
        client.Reconnecting += (s, e) => reconnectingFired = true;

        var keepAliveEventArgs = new KeepAliveEventArgs(new ServiceResult(StatusCodes.BadConnectionClosed), ServerState.CommunicationFault, DateTime.Now);

        Assert.False(keepAliveEventArgs.CancelKeepAlive);

        // Act
        PrivateFieldHelpers.InvokePrivateMethod(client, "Session_KeepAlive", [mockSession.Object, keepAliveEventArgs]);

        // Assert
        Assert.True(keepAliveEventArgs.CancelKeepAlive, "CancelKeepAlive should be true to stop further KeepAlives.");
        Assert.True(reconnectingFired, "Reconnecting event should have been fired.");
    }

    #endregion Connection Tests

    #region Structure Discovery and Browsing Tests

    [Fact]
    public void GetAllInstanceDataBlocks_WhenNotConnected_ReturnsEmptyListAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        _mockSession.SetupGet(s => s.Connected).Returns(false);
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = client.GetAllInstanceDataBlocks();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Cannot get instance data blocks")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void GetAllInstanceDataBlocks_WhenConnected_ReturnsMappedDataBlocks()
    {
        // Arrange
        var client = CreateSut();
        var simulatedReferences = new ReferenceDescriptionCollection
        {
            new ReferenceDescription { NodeId = new NodeId("ns=3;s=\"MyDb\""), DisplayName = "MyDb" }
        };

        _mockSession.Setup(s => s.Browse(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(), It.IsAny<BrowseDescriptionCollection>(),
                out It.Ref<BrowseResultCollection>.IsAny, out It.Ref<DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionBrowseCallback((RequestHeader _, ViewDescription _, uint _, BrowseDescriptionCollection bdc, out BrowseResultCollection brc, out DiagnosticInfoCollection dic) =>
            {
                brc = [];
                dic = [];
                foreach (var __ in bdc)
                {
                    brc.Add(new BrowseResult { References = simulatedReferences, StatusCode = StatusCodes.Good });
                }
            }))
            .Returns(new ResponseHeader { ServiceResult = StatusCodes.Good });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = client.GetAllInstanceDataBlocks();

        // Assert
        Assert.Single(result);
        Assert.Equal("MyDb", result[0].DisplayName);
    }

    [Fact]
    public void DiscoverVariablesOfElement_WhenConnected_ReturnsElementWithVariables()
    {
        // Arrange
        var client = CreateSut();
        var elementToDiscover = new S7Inputs { NodeId = new NodeId("ns=3;s=\"Inputs\""), DisplayName = "Inputs" };
        var simulatedReferences = new ReferenceDescriptionCollection
        {
            new ReferenceDescription { NodeId = new NodeId("ns=3;s=\"Inputs.MyInput\""), DisplayName = "MyInput" },
            new ReferenceDescription { NodeId = new NodeId("ns=3;s=\"Inputs.Icon\""), DisplayName = "Icon" },
        };

        _mockSession.Setup(s => s.Browse(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(), It.IsAny<BrowseDescriptionCollection>(),
                out It.Ref<BrowseResultCollection>.IsAny, out It.Ref<DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionBrowseCallback((RequestHeader _, ViewDescription _, uint _, BrowseDescriptionCollection bdc, out BrowseResultCollection brc, out DiagnosticInfoCollection dic) =>
            {
                brc = [];
                dic = [];
                foreach (var _ in bdc)
                {
                    brc.Add(new BrowseResult { References = simulatedReferences, StatusCode = StatusCodes.Good });
                }
            }))
            .Returns(new ResponseHeader { ServiceResult = StatusCodes.Good });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = client.DiscoverVariablesOfElement(elementToDiscover);

        // Assert
        Assert.NotNull(result.Variables);
        Assert.Single(result.Variables);
        Assert.Equal("MyInput", result.Variables[0].DisplayName);
    }

    [Fact]
    public void DiscoverElement_WithNullElement_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var client = CreateSut();

        // Act
        var result = client.DiscoverElement(null!);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("called with a null element shell")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void DiscoverElement_WithUnsupportedType_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var client = CreateSut();
        var unsupportedElement = new Mock<IUaElement>().Object;

        // Act
        var result = client.DiscoverElement(unsupportedElement);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("unsupported element type")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private delegate void SessionBrowseCallback(
    RequestHeader requestHeader,
    ViewDescription view,
    uint requestedMaxReferencesPerNode,
    BrowseDescriptionCollection nodesToBrowse,
    out BrowseResultCollection results,
    out DiagnosticInfoCollection diagnosticInfos);

    #endregion Structure Discovery and Browsing Tests
}