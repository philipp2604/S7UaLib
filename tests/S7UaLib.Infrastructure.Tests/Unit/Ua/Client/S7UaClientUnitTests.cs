using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using S7UaLib.Core.Enums;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Infrastructure.Ua.Client;
using S7UaLib.TestHelpers;
using System.Collections;

namespace S7UaLib.Infrastructure.Tests.Unit.Ua.Client;

[Trait("Category", "Unit")]
public class S7UaClientUnitTests
{
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7UaClient>> _mockLogger;
    private readonly Action<IList, IList> _validateResponse;
    private readonly Mock<ISession> _mockSession;
    private readonly UserIdentity? _userIdentity = new();

    public S7UaClientUnitTests()
    {
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<S7UaClient>>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);

        _validateResponse = (_, _) => { };

        _mockSession = new Mock<ISession>();
        _mockSession.SetupGet(s => s.Connected).Returns(true);
    }

    private S7UaClient CreateSut()
    {
        return new S7UaClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object);
    }

    #region Configuration Tests

    // Helper class to manage temporary directories for certificate stores and config files.
    private class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch (IOException)
            {
                // Suppress exceptions during cleanup
            }
        }
    }

    private static SecurityConfiguration CreateTestSecurityConfig(string rootPath)
    {
        var certStores = new SecurityConfigurationStores
        {
            AppRoot = Path.Combine(rootPath, "certs"),
            TrustedRoot = Path.Combine(rootPath, "certs", "trusted"),
            IssuerRoot = Path.Combine(rootPath, "certs", "issuer"),
            RejectedRoot = Path.Combine(rootPath, "certs", "rejected"),
            SubjectName = "CN=S7UaClient.Test"
        };
        return new SecurityConfiguration(certStores);
    }

    [Fact]
    public async Task Configure_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var client = new S7UaClient();
        client.Dispose();
        var securityConfig = CreateTestSecurityConfig(Path.GetTempPath());

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            client.ConfigureAsync("TestApp", "urn:test", "urn:test:prod", securityConfig));
    }

    [Fact]
    public async Task Configure_WithBasicParameters_CreatesAndSetsConfiguration()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var client = new S7UaClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object);
        var securityConfig = CreateTestSecurityConfig(tempDir.Path);
        const string appName = "MyTestApp";
        const string appUri = "urn:localhost:mytestapp";
        const string productUri = "urn:mycompany:mytestapp";

        // Act
        await client.ConfigureAsync(appName, appUri, productUri, securityConfig);

        // Assert
        var appInst = PrivateFieldHelpers.GetPrivateField(client, "_appInst") as ApplicationInstance;
        Assert.NotNull(appInst);

        var appConfig = appInst.ApplicationConfiguration;
        Assert.NotNull(appConfig);
        Assert.Equal(appName, appConfig.ApplicationName);
        Assert.Equal(appUri, appConfig.ApplicationUri);
        Assert.Equal(productUri, appConfig.ProductUri);
        Assert.Equal("CN=S7UaClient.Test", appConfig.SecurityConfiguration.ApplicationCertificate.SubjectName);
    }

    [Fact]
    public async Task Configure_WithAllParameters_SetsFullConfigurationCorrectly()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var client = new S7UaClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object);
        var securityConfig = CreateTestSecurityConfig(tempDir.Path);

        var clientConfig = new Core.Ua.ClientConfiguration
        {
            SessionTimeout = 60000,
            WellKnownDiscoveryUrls = ["opc.tcp://localhost:4840/discovery"]
        };

        var transportQuotas = new Core.Ua.TransportQuotas
        {
            MaxArrayLength = 2048,
            OperationTimeout = 120000
        };

        var opLimits = new Core.Ua.OperationLimits
        {
            MaxNodesPerRead = 500,
            MaxNodesPerWrite = 500
        };

        // Act
        await client.ConfigureAsync("FullApp", "urn:full", "urn:prod:full", securityConfig, clientConfig, transportQuotas, opLimits);

        // Assert
        var appInst = PrivateFieldHelpers.GetPrivateField(client, "_appInst") as ApplicationInstance;
        Assert.NotNull(appInst);
        var appConfig = appInst.ApplicationConfiguration;

        // Verify Client Config
        Assert.Equal(clientConfig.SessionTimeout, (uint)appConfig.ClientConfiguration.DefaultSessionTimeout);
        Assert.Contains(clientConfig.WellKnownDiscoveryUrls[0], appConfig.ClientConfiguration.WellKnownDiscoveryUrls);

        // Verify Transport Quotas
        Assert.Equal(transportQuotas.MaxArrayLength, (uint)appConfig.TransportQuotas.MaxArrayLength);
        Assert.Equal(transportQuotas.OperationTimeout, (uint)appConfig.TransportQuotas.OperationTimeout);

        // Verify Operation Limits
        Assert.Equal(opLimits.MaxNodesPerRead, appConfig.ClientConfiguration.OperationLimits.MaxNodesPerRead);
        Assert.Equal(opLimits.MaxNodesPerWrite, appConfig.ClientConfiguration.OperationLimits.MaxNodesPerWrite);
    }

    [Fact]
    public async Task SaveConfiguration_WhenNotConfigured_ThrowsInvalidOperationException()
    {
        // Arrange
        var client = new S7UaClient();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => client.SaveConfiguration("test.xml"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task LoadConfiguration_WhenNotConfigured_ThrowsArgumentNullException()
    {
        // Arrange
        var client = new S7UaClient();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.LoadConfigurationAsync("test.xml"));
    }

    [Fact]
    public async Task SaveConfiguration_WithNullPath_ThrowsArgumentNullException()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var client = new S7UaClient();
        await client.ConfigureAsync("TestApp", "urn:test", "urn:prod", CreateTestSecurityConfig(tempDir.Path));

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => client.SaveConfiguration(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveConfiguration_WithEmptyOrWhitespacePath_ThrowsArgumentException(string filePath)
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var client = new S7UaClient();
        await client.ConfigureAsync("TestApp", "urn:test", "urn:prod", CreateTestSecurityConfig(tempDir.Path));

        // Act & Assert
        Assert.Throws<ArgumentException>(() => client.SaveConfiguration(filePath!));
    }

    [Fact]
    public async Task LoadConfiguration_WithNullPath_ThrowsArgumentNullException()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var client = new S7UaClient();
        await client.ConfigureAsync("TestApp", "urn:test", "urn:prod", CreateTestSecurityConfig(tempDir.Path));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.LoadConfigurationAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task LoadConfiguration_WithEmptyOrWhitespacePath_ThrowsArgumentException(string filePath)
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var client = new S7UaClient();
        await client.ConfigureAsync("TestApp", "urn:test", "urn:prod", CreateTestSecurityConfig(tempDir.Path));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.LoadConfigurationAsync(filePath));
    }

    [Fact]
    public async Task SaveAndLoadConfiguration_Integration_WorksCorrectly()
    {
        using var tempDir = new TempDirectory();
        var configFilePath = Path.Combine(tempDir.Path, "config.xml");

        // --- Arrange: Create and save a configuration ---
        var saveClient = new S7UaClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object);
        var securityConfig = CreateTestSecurityConfig(tempDir.Path);
        await saveClient.ConfigureAsync("SavedApp", "urn:saved", "urn:prod:saved", securityConfig, new Core.Ua.ClientConfiguration { SessionTimeout = 99000 });

        // --- Act 1: Save the configuration ---
        saveClient.SaveConfiguration(configFilePath);

        // Assert 1: File was created
        Assert.True(File.Exists(configFilePath));
        var fileContent = await File.ReadAllTextAsync(configFilePath);
        Assert.Contains("<ApplicationName>SavedApp</ApplicationName>", fileContent);
        Assert.Contains("<DefaultSessionTimeout>99000</DefaultSessionTimeout>", fileContent);

        // --- Arrange 2: Create a new client with a different initial config ---
        var loadClient = new S7UaClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object);
        await loadClient.ConfigureAsync("InitialApp", "urn:initial", "urn:prod:initial", CreateTestSecurityConfig(tempDir.Path));

        // --- Act 2: Load the previously saved configuration ---
        await loadClient.LoadConfigurationAsync(configFilePath);

        // --- Assert 2: The client's configuration is now updated ---
        var appInst = PrivateFieldHelpers.GetPrivateField(loadClient, "_appInst") as ApplicationInstance;
        Assert.NotNull(appInst);

        var loadedConfig = appInst.ApplicationConfiguration;
        Assert.Equal("SavedApp", loadedConfig.ApplicationName);
        Assert.Equal("urn:saved", loadedConfig.ApplicationUri);
        Assert.Equal("urn:prod:saved", loadedConfig.ProductUri);
        Assert.Equal(99000, loadedConfig.ClientConfiguration.DefaultSessionTimeout);
    }

    #endregion Configuration Tests

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
    public async Task Disconnect_WhenConnected_ClosesSessionAndCleansUpHandlers()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<ISession>();
        var reconnectHandler = new SessionReconnectHandler(true, -1);

        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        PrivateFieldHelpers.SetPrivateField(client, "_reconnectHandler", reconnectHandler);

        // Act
        await client.DisconnectAsync();

        // Assert
        mockSession.Verify(s => s.Close(It.IsAny<bool>()), Times.Once);
        mockSession.Verify(s => s.Dispose(), Times.Once);

        Assert.Null(PrivateFieldHelpers.GetPrivateField(client, "_reconnectHandler"));
        Assert.Null(PrivateFieldHelpers.GetPrivateField(client, "_session"));

        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Disconnect_FiresDisconnectingAndDisconnectedEvents()
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
        await client.DisconnectAsync();

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

        var keepAliveEventArgs = new KeepAliveEventArgs(new Opc.Ua.ServiceResult(Opc.Ua.StatusCodes.BadConnectionClosed), Opc.Ua.ServerState.CommunicationFault, DateTime.Now);

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
    public async Task GetAllInstanceDataBlocks_WhenNotConnected_ReturnsEmptyListAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        _mockSession.SetupGet(s => s.Connected).Returns(false);
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.GetAllInstanceDataBlocksAsync();

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
    public async Task GetAllInstanceDataBlocks_WhenConnected_ReturnsMappedDataBlocks()
    {
        // Arrange
        var client = CreateSut();
        var simulatedReferences = new Opc.Ua.ReferenceDescriptionCollection
        {
            new Opc.Ua.ReferenceDescription { NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb\""), DisplayName = "MyDb" }
        };

        _mockSession.Setup(s => s.Browse(
                It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<Opc.Ua.ViewDescription>(), It.IsAny<uint>(), It.IsAny<Opc.Ua.BrowseDescriptionCollection>(),
                out It.Ref<Opc.Ua.BrowseResultCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionBrowseCallback((Opc.Ua.RequestHeader _, Opc.Ua.ViewDescription _, uint _, Opc.Ua.BrowseDescriptionCollection bdc, out Opc.Ua.BrowseResultCollection brc, out Opc.Ua.DiagnosticInfoCollection dic) =>
            {
                brc = [];
                dic = [];
                foreach (var __ in bdc)
                {
                    brc.Add(new Opc.Ua.BrowseResult { References = simulatedReferences, StatusCode = Opc.Ua.StatusCodes.Good });
                }
            }))
            .Returns(new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.GetAllInstanceDataBlocksAsync();

        // Assert
        Assert.Single(result);
        Assert.Equal("MyDb", result[0].DisplayName);
    }

    [Fact]
    public async Task DiscoverVariablesOfElement_WhenConnected_ReturnsElementWithVariables()
    {
        // Arrange
        var client = CreateSut();
        var elementToDiscover = new S7Inputs { NodeId = new Opc.Ua.NodeId("ns=3;s=\"Inputs\"").ToString(), DisplayName = "Inputs" };
        var simulatedReferences = new Opc.Ua.ReferenceDescriptionCollection
        {
            new Opc.Ua.ReferenceDescription { NodeId = new Opc.Ua.NodeId("ns=3;s=\"Inputs.MyInput\""), DisplayName = "MyInput" },
            new Opc.Ua.ReferenceDescription { NodeId = new Opc.Ua.NodeId("ns=3;s=\"Inputs.Icon\""), DisplayName = "Icon" },
        };

        _mockSession.Setup(s => s.Browse(
                It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<Opc.Ua.ViewDescription>(), It.IsAny<uint>(), It.IsAny<Opc.Ua.BrowseDescriptionCollection>(),
                out It.Ref<Opc.Ua.BrowseResultCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionBrowseCallback((Opc.Ua.RequestHeader _, Opc.Ua.ViewDescription _, uint _, Opc.Ua.BrowseDescriptionCollection bdc, out Opc.Ua.BrowseResultCollection brc, out Opc.Ua.DiagnosticInfoCollection dic) =>
            {
                brc = [];
                dic = [];
                foreach (var _ in bdc)
                {
                    brc.Add(new Opc.Ua.BrowseResult { References = simulatedReferences, StatusCode = Opc.Ua.StatusCodes.Good });
                }
            }))
            .Returns(new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.DiscoverVariablesOfElementAsync(elementToDiscover);

        // Assert
        Assert.NotNull(result.Variables);
        Assert.Single(result.Variables);
        Assert.Equal("MyInput", result.Variables[0].DisplayName);
    }

    [Fact]
    public async Task DiscoverElement_WithNullElement_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var client = CreateSut();

        // Act
        var result = await client.DiscoverElementAsync(null!);

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
    public async Task DiscoverElement_WithUnsupportedType_ReturnsNullAndLogsWarning()
    {
        // Arrange
        var client = CreateSut();
        var unsupportedElement = new Mock<IUaNode>().Object;

        // Act
        var result = await client.DiscoverElementAsync(unsupportedElement);

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
    Opc.Ua.RequestHeader requestHeader,
    Opc.Ua.ViewDescription view,
    uint requestedMaxReferencesPerNode,
    Opc.Ua.BrowseDescriptionCollection nodesToBrowse,
    out Opc.Ua.BrowseResultCollection results,
    out Opc.Ua.DiagnosticInfoCollection diagnosticInfos);

    #endregion Structure Discovery and Browsing Tests

    #region Reading and Writing Tests

    #region Reading Tests

    [Fact]
    public async Task ReadValuesOfElement_WhenNotConnected_ReturnsOriginalElementAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        _mockSession.SetupGet(s => s.Connected).Returns(false);
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        var element = new S7Inputs { DisplayName = "Inputs", NodeId = new Opc.Ua.NodeId(1).ToString() };

        // Act
        var result = await client.ReadValuesOfElementAsync(element);

        // Assert
        Assert.Same(element, result); // Should return the original instance
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Cannot read values for 'Inputs'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task ReadValuesOfElement_WithSimpleStructure_ReturnsPopulatedElement()
    {
        // Arrange
        var client = CreateSut();
        var var1NodeId = new Opc.Ua.NodeId("ns=3;s=\"Inputs.TestBool\"");
        var var2NodeId = new Opc.Ua.NodeId("ns=3;s=\"Inputs.TestInt\"");

        var elementWithStructure = new S7Inputs
        {
            NodeId = new Opc.Ua.NodeId("ns=3;s=\"Inputs\"").ToString(),
            DisplayName = "Inputs",
            Variables =
            [
                new S7Variable() { NodeId = var1NodeId.ToString(), DisplayName = "TestBool" },
                new S7Variable() { NodeId = var2NodeId.ToString(), DisplayName = "TestInt" }
            ]
        };

        _mockSession.Setup(s => s.Read(
                It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<double>(), It.IsAny<Opc.Ua.TimestampsToReturn>(),
                It.IsAny<Opc.Ua.ReadValueIdCollection>(), out It.Ref<Opc.Ua.DataValueCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionReadCallback((Opc.Ua.RequestHeader _, double _, Opc.Ua.TimestampsToReturn _, Opc.Ua.ReadValueIdCollection _, out Opc.Ua.DataValueCollection results, out Opc.Ua.DiagnosticInfoCollection diags) =>
            {
                results =
                [
                    new(new Opc.Ua.Variant(true)) { StatusCode = Opc.Ua.StatusCodes.Good },
                    new(new Opc.Ua.Variant((short)123)) { StatusCode = Opc.Ua.StatusCodes.Good }
                ];
                diags = [];
            }))
            .Returns(new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good });
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.ReadValuesOfElementAsync(elementWithStructure, "S7");

        // Assert
        Assert.NotNull(result);
        Assert.NotSame(elementWithStructure, result);
        Assert.Equal(2, result.Variables.Count);

        var resultVar1 = result.Variables.First(v => v.DisplayName == "TestBool");
        Assert.True((bool)resultVar1.Value!);
        Assert.Equal(StatusCode.Good, resultVar1.StatusCode);
        Assert.Equal("S7.Inputs.TestBool", resultVar1.FullPath);

        var resultVar2 = result.Variables.First(v => v.DisplayName == "TestInt");
        Assert.Equal((short)123, resultVar2.Value);
        Assert.Equal(StatusCode.Good, resultVar2.StatusCode);
        Assert.Equal("S7.Inputs.TestInt", resultVar2.FullPath);
    }

    [Fact]
    public async Task ReadValuesOfElement_WithStructVariable_RecursivelyReadsAndPopulatesMembers()
    {
        // Arrange
        var client = CreateSut();
        var structNodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyStruct\"");
        var structMember1NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyStruct.MemberBool\"");
        var structMember2NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyStruct.MemberDInt\"");

        var elementWithStructure = new S7DataBlockGlobal
        {
            NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb\"").ToString(),
            DisplayName = "MyDb",
            Variables =
            [
                new S7Variable() { NodeId = structNodeId.ToString(), DisplayName = "MyStruct", S7Type = S7DataType.STRUCT }
            ]
        };

        // Mock browsing for the struct members
        var structMemberReferences = new Opc.Ua.ReferenceDescriptionCollection
    {
        new() { NodeId = structMember1NodeId, DisplayName = "MemberBool" },
        new() { NodeId = structMember2NodeId, DisplayName = "MemberDInt" }
    };
        _mockSession.Setup(s => s.Browse(It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<Opc.Ua.ViewDescription>(), It.IsAny<uint>(),
                It.Is<Opc.Ua.BrowseDescriptionCollection>(bdc => bdc[0].NodeId == structNodeId),
                out It.Ref<Opc.Ua.BrowseResultCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionBrowseCallback((Opc.Ua.RequestHeader _, Opc.Ua.ViewDescription _, uint _, Opc.Ua.BrowseDescriptionCollection _, out Opc.Ua.BrowseResultCollection brc, out Opc.Ua.DiagnosticInfoCollection dic) =>
            {
                brc = [new() { References = structMemberReferences, StatusCode = Opc.Ua.StatusCodes.Good }];
                dic = [];
            }));

        // Mock reading the values of the struct members
        _mockSession.Setup(s => s.Read(It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<double>(), It.IsAny<Opc.Ua.TimestampsToReturn>(),
                It.Is<Opc.Ua.ReadValueIdCollection>(rvc => rvc.Count == 2 && rvc.Any(r => r.NodeId == structMember1NodeId)),
                out It.Ref<Opc.Ua.DataValueCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionReadCallback((Opc.Ua.RequestHeader _, double _, Opc.Ua.TimestampsToReturn _, Opc.Ua.ReadValueIdCollection _, out Opc.Ua.DataValueCollection results, out Opc.Ua.DiagnosticInfoCollection diags) =>
            {
                results =
                [
                    new(new Opc.Ua.Variant(false)) { StatusCode = Opc.Ua.StatusCodes.Good },
                new(new Opc.Ua.Variant(9999)) { StatusCode = Opc.Ua.StatusCodes.Good }
                ];
                diags = [];
            }));

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.ReadValuesOfElementAsync(elementWithStructure);

        // Assert
        Assert.NotNull(result);
        var resultStruct = result.Variables.First(v => v.DisplayName == "MyStruct");
        Assert.Equal(S7DataType.STRUCT, resultStruct.S7Type);
        Assert.Equal("MyDb.MyStruct", resultStruct.FullPath);
        Assert.NotNull(resultStruct.StructMembers);
        Assert.Equal(2, resultStruct.StructMembers.Count);

        var member1 = resultStruct.StructMembers.First(m => m.DisplayName == "MemberBool");
        Assert.False((bool)member1.Value!);
        Assert.Equal("MyDb.MyStruct.MemberBool", member1.FullPath);

        var member2 = resultStruct.StructMembers.First(m => m.DisplayName == "MemberDInt");
        Assert.Equal(9999, member2.Value);
        Assert.Equal("MyDb.MyStruct.MemberDInt", member2.FullPath);
    }

    [Fact]
    public async Task ReadValuesOfElement_WithS7Char_AppliesCorrectConverter()
    {
        // Arrange
        var client = CreateSut();
        var charNodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyChar\"");

        var elementWithStructure = new S7DataBlockGlobal
        {
            DisplayName = "MyDb",
            NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb\"").ToString(),
            Variables =
            [
                new S7Variable() { NodeId = charNodeId.ToString(), DisplayName = "MyChar", S7Type = S7DataType.CHAR }
            ]
        };

        // The OPC UA server represents S7 CHAR as a Byte.
        // The converter should turn this byte (ASCII 65) into the char 'A'.
        const byte opcRawValue = 65;

        _mockSession.Setup(s => s.Read(
                It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<double>(), It.IsAny<Opc.Ua.TimestampsToReturn>(),
                It.Is<Opc.Ua.ReadValueIdCollection>(nodes => nodes.Count == 1 && nodes[0].NodeId == charNodeId),
                out It.Ref<Opc.Ua.DataValueCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionReadCallback((Opc.Ua.RequestHeader _, double _, Opc.Ua.TimestampsToReturn _, Opc.Ua.ReadValueIdCollection _, out Opc.Ua.DataValueCollection results, out Opc.Ua.DiagnosticInfoCollection diags) =>
            {
                results = [new(new Opc.Ua.Variant(opcRawValue)) { StatusCode = Opc.Ua.StatusCodes.Good }];
                diags = [];
            }))
            .Returns(new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good });
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.ReadValuesOfElementAsync(elementWithStructure);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Variables);

        var resultVar = result.Variables[0];
        Assert.Equal("MyChar", resultVar.DisplayName);
        Assert.Equal(StatusCode.Good, resultVar.StatusCode);

        // Verify the conversion
        Assert.Equal('A', resultVar.Value);
        Assert.Equal(typeof(char), resultVar.SystemType);
        Assert.Equal(opcRawValue, resultVar.RawOpcValue);
    }

    private delegate void SessionReadCallback(
        Opc.Ua.RequestHeader requestHeader,
        double maxAge,
        Opc.Ua.TimestampsToReturn timestampsToReturn,
        Opc.Ua.ReadValueIdCollection nodesToRead,
        out Opc.Ua.DataValueCollection results,
        out Opc.Ua.DiagnosticInfoCollection diagnosticInfos);

    #endregion Reading Tests

    #region Subscription Tests

    [Fact]
    public async Task CreateSubscriptionAsync_WhenNotConnected_ReturnsFalse()
    {
        // Arrange
        var client = CreateSut();
        _mockSession.SetupGet(s => s.Connected).Returns(false);
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.CreateSubscriptionAsync();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_WhenCalledFirstTime_CreatesAndRegistersSubscription()
    {
        // Arrange
        var mockClient = new Mock<S7UaClient>(_userIdentity!, _validateResponse, _mockLoggerFactory.Object) { CallBase = true };
        var client = mockClient.Object;

        _mockSession.Setup(s => s.DefaultSubscription).Returns(new Subscription());
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);
        _mockSession.Setup(s => s.AddSubscription(It.IsAny<Subscription>())).Returns(true);

        mockClient.Protected()
            .Setup<Task>("CreateSubscriptionOnServerAsync", ItExpr.IsAny<Subscription>())
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        var result = await client.CreateSubscriptionAsync();

        // Assert
        Assert.True(result, "CreateSubscriptionAsync should have returned true.");
        _mockSession.Verify(s => s.AddSubscription(It.IsAny<Subscription>()), Times.Once);
        mockClient.Protected().Verify("CreateSubscriptionOnServerAsync", Times.Once(), ItExpr.IsAny<Subscription>());
    }

    [Fact]
    public async Task CreateSubscriptionAsync_WhenAlreadyExists_DoesNothingAndReturnsTrue()
    {
        // Arrange
        var client = CreateSut();

        var existingSubscription = new Mock<Subscription>(new Subscription());
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);
        PrivateFieldHelpers.SetPrivateField(client, "_subscription", existingSubscription.Object);

        // Act
        var result = await client.CreateSubscriptionAsync();

        // Assert
        Assert.True(result);
        _mockSession.Verify(s => s.AddSubscription(It.IsAny<Subscription>()), Times.Never);
    }

    [Fact]
    public async Task SubscribeToVariableAsync_WithValidSubscription_AddsItemAndAppliesChanges()
    {
        // Arrange
        var mockClient = new Mock<S7UaClient>(_userIdentity!, _validateResponse, _mockLoggerFactory.Object) { CallBase = true };
        var client = mockClient.Object;
        var variable = new S7Variable { NodeId = new Opc.Ua.NodeId("ns=2;s=Var1").ToString(), DisplayName = "Var1" };

        var subscription = new Subscription(_mockSession.Object.DefaultSubscription);

        PrivateFieldHelpers.SetPrivateField(client, "_subscription", subscription);

        mockClient.Protected()
            .Setup<Task>("ApplySubscriptionChangesAsync", ItExpr.IsAny<Subscription>())
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        var result = await client.SubscribeToVariableAsync(variable);

        // Assert
        Assert.True(result);
        Assert.Single(subscription.MonitoredItems);
        Assert.Equal(variable.NodeId, subscription.MonitoredItems.First().StartNodeId);

        mockClient.Protected().Verify("ApplySubscriptionChangesAsync", Times.Once(), ItExpr.IsAny<Subscription>());
    }

    [Fact]
    public async Task UnsubscribeFromVariableAsync_WhenSubscribed_RemovesItemAndAppliesChanges()
    {
        // Arrange
        var mockClient = new Mock<S7UaClient>(_userIdentity!, _validateResponse, _mockLoggerFactory.Object) { CallBase = true };
        var client = mockClient.Object;

        var variable = new S7Variable { NodeId = new Opc.Ua.NodeId("ns=2;s=Var1").ToString(), DisplayName = "Var1" };

        var subscription = new Subscription(_mockSession.Object.DefaultSubscription);
        var monitoredItem = new MonitoredItem(subscription.DefaultItem)
        {
            StartNodeId = variable.NodeId
        };
        subscription.AddItem(monitoredItem);

        PrivateFieldHelpers.SetPrivateField(client, "_subscription", subscription);
        var monitoredItemsDict = (Dictionary<Opc.Ua.NodeId, MonitoredItem>)PrivateFieldHelpers.GetPrivateField(client, "_monitoredItems")!;
        monitoredItemsDict[variable.NodeId] = monitoredItem;

        Assert.Single(subscription.MonitoredItems);

        mockClient.Protected()
            .Setup<Task>("ApplySubscriptionChangesAsync", ItExpr.IsAny<Subscription>())
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        var result = await client.UnsubscribeFromVariableAsync(variable);

        // Assert
        Assert.True(result, "UnsubscribeFromVariableAsync should return true on success.");
        Assert.Empty(subscription.MonitoredItems);
        mockClient.Protected().Verify("ApplySubscriptionChangesAsync", Times.Once(), ItExpr.IsAny<Subscription>());
    }

    #endregion Subscription Tests

    #region Write Tests

    [Fact]
    public async Task WriteRawVariableAsync_WhenNotConnected_ReturnsFalseAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        _mockSession.SetupGet(s => s.Connected).Returns(false);
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);
        var nodeId = new Opc.Ua.NodeId("ns=3;s=\"MyVar\"");

        // Act
        var result = await client.WriteRawVariableAsync(nodeId.ToString(), 42);

        // Assert
        Assert.False(result);
        _mockSession.Verify(s => s.WriteAsync(It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<Opc.Ua.WriteValueCollection>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("session is not connected")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteRawVariableAsync_WhenWriteSucceeds_ReturnsTrue()
    {
        // Arrange
        var client = CreateSut();
        var nodeId = new Opc.Ua.NodeId("ns=3;s=\"MyVar\"");
        const int valueToWrite = 123;

        var response = new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good };
        var results = new Opc.Ua.StatusCodeCollection { Opc.Ua.StatusCodes.Good };
        var diags = new Opc.Ua.DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(
                It.IsAny<Opc.Ua.RequestHeader>(),
                It.Is<Opc.Ua.WriteValueCollection>(wvc => wvc.Count == 1 && wvc[0].NodeId == nodeId && (int)wvc[0].Value.Value! == valueToWrite),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Opc.Ua.WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.WriteRawVariableAsync(nodeId.ToString(), valueToWrite);

        // Assert
        Assert.True(result);
        _mockSession.Verify(s => s.WriteAsync(It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<Opc.Ua.WriteValueCollection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteRawVariableAsync_WhenWriteFails_ReturnsFalseAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        var nodeId = new Opc.Ua.NodeId("ns=3;s=\"MyVar\"");

        var response = new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good };
        var results = new Opc.Ua.StatusCodeCollection { Opc.Ua.StatusCodes.BadTypeMismatch };
        var diags = new Opc.Ua.DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(It.IsAny<Opc.Ua.RequestHeader>(), It.IsAny<Opc.Ua.WriteValueCollection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Opc.Ua.WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.WriteRawVariableAsync(nodeId.ToString(), "some value");

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to write raw value") && v.ToString()!.Contains("BadTypeMismatch")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteVariableAsync_WithS7Type_UsesConverterAndCallsSessionWrite()
    {
        // Arrange
        var client = CreateSut();
        var nodeId = new Opc.Ua.NodeId("ns=3;s=\"MyCharVar\"");
        const char userValue = 'A';
        const byte expectedOpcValue = 65;

        var response = new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good };
        var results = new Opc.Ua.StatusCodeCollection { Opc.Ua.StatusCodes.Good };
        var diags = new Opc.Ua.DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(
                It.IsAny<Opc.Ua.RequestHeader>(),
                It.Is<Opc.Ua.WriteValueCollection>(wvc =>
                    wvc.Count == 1 &&
                    wvc[0].NodeId == nodeId &&
                    (byte)wvc[0].Value.Value! == expectedOpcValue),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Opc.Ua.WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags })
            .Verifiable();

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.WriteVariableAsync(nodeId.ToString(), userValue, S7DataType.CHAR);

        // Assert
        Assert.True(result);
        _mockSession.Verify();
    }

    [Fact]
    public async Task WriteVariableAsync_WithS7VariableObject_UsesConverterAndCallsSessionWrite()
    {
        // Arrange
        var client = CreateSut();
        var variable = new S7Variable
        {
            NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyTimeVar\"").ToString(),
            DisplayName = "MyTimeVar",
            S7Type = S7DataType.TIME
        };
        var userValue = TimeSpan.FromSeconds(10);
        const int expectedOpcValue = 10000;

        var response = new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good };
        var results = new Opc.Ua.StatusCodeCollection { Opc.Ua.StatusCodes.Good };
        var diags = new Opc.Ua.DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(
                It.IsAny<Opc.Ua.RequestHeader>(),
                It.Is<Opc.Ua.WriteValueCollection>(wvc =>
                    wvc.Count == 1 &&
                    wvc[0].NodeId == new Opc.Ua.NodeId(variable.NodeId) &&
                    (int)wvc[0].Value.Value! == expectedOpcValue),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Opc.Ua.WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags })
            .Verifiable();

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.WriteVariableAsync(variable, userValue);

        // Assert
        Assert.True(result);
        _mockSession.Verify();
    }

    [Fact]
    public async Task WriteVariableAsync_WithS7VariableWithoutNodeId_ThrowsArgumentException()
    {
        // Arrange
        var client = CreateSut();
        var variableWithoutNodeId = new S7Variable { DisplayName = "InvalidVar", S7Type = S7DataType.BOOL };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => client.WriteVariableAsync(variableWithoutNodeId, true));
        Assert.Contains("has no NodeId", ex.Message);
    }

    #endregion Write Tests

    #endregion Reading and Writing Tests

    #region GetConverter Tests

    [Fact]
    public void GetConverter_WhenSpecificConverterExists_ReturnsInstance()
    {
        // Arrange
        var client = new S7UaClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object); // Recreate to pass factory

        // Act
        var converter = client.GetConverter(S7DataType.CHAR, typeof(object));

        // Assert
        Assert.NotNull(converter);
        Assert.IsType<S7UaLib.Infrastructure.S7.Converters.S7CharConverter>(converter);
    }

    [Fact]
    public void GetConverter_WhenSpecificConverterDoesNotExist_ReturnsDefaultConverter()
    {
        // Arrange
        var client = new S7UaClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object); // Recreate to pass factory

        // Act
        var converter = client.GetConverter(S7DataType.UNKNOWN, typeof(int)); // UNKNOWN type will use DefaultConverter

        // Assert
        Assert.NotNull(converter);
        var defaultConverter = Assert.IsType<S7UaLib.Infrastructure.S7.Converters.DefaultConverter>(converter);
        Assert.Equal(typeof(int), defaultConverter.TargetType);
    }

    [Fact]
    public void GetConverter_WhenLoggerFactoryIsNull_ConvertersAreCreatedWithoutLoggers()
    {
        // Arrange
        var client = new S7UaClient(_userIdentity, _validateResponse, null); // No logger factory

        // Act
        var charConverter = client.GetConverter(S7DataType.CHAR, typeof(object));
        var defaultConverter = client.GetConverter(S7DataType.UNKNOWN, typeof(int));

        // Assert
        Assert.NotNull(charConverter);
        Assert.IsType<S7UaLib.Infrastructure.S7.Converters.S7CharConverter>(charConverter);
        // Cannot directly verify logger is null without reflection, but no call to factory means no logger was passed.

        Assert.NotNull(defaultConverter);
        Assert.IsType<S7UaLib.Infrastructure.S7.Converters.DefaultConverter>(defaultConverter);
        // Factory mock will not be called.
        _mockLoggerFactory.Verify(f => f.CreateLogger(It.IsAny<string>()), Times.Never); //Verify overall factory usage
    }

    #endregion GetConverter Tests
}