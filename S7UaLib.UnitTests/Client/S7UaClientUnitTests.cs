using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Client;
using S7UaLib.Events;
using S7UaLib.UnitTests.Helpers;
using System.Collections;
using System.Reflection;
using Xunit;

namespace S7UaLib.UnitTests.Client;

[Trait("Category", "Unit")]
public class S7UaClientUnitTests
{
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7UaClient>> _mockLogger;
    private readonly ApplicationConfiguration _appConfig;
    private readonly Action<IList, IList> _validateResponse;

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
    }

    private S7UaClient CreateSut()
    {
        return new S7UaClient(_appConfig, _validateResponse, _mockLoggerFactory.Object);
    }

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
}