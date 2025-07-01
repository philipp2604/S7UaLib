using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Client;
using S7UaLib.S7.Structure;
using S7UaLib.S7.Types;
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

    #region Reading and Writing Tests

    #region Reading Tests

    [Fact]
    public void ReadValuesOfElement_WhenNotConnected_ReturnsOriginalElementAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        _mockSession.SetupGet(s => s.Connected).Returns(false);
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        var element = new S7Inputs { DisplayName = "Inputs", NodeId = new NodeId(1) };

        // Act
        var result = client.ReadValuesOfElement(element);

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
    public void ReadValuesOfElement_WithSimpleStructure_ReturnsPopulatedElement()
    {
        // Arrange
        var client = CreateSut();
        var var1NodeId = new NodeId("ns=3;s=\"Inputs.TestBool\"");
        var var2NodeId = new NodeId("ns=3;s=\"Inputs.TestInt\"");

        var elementWithStructure = new S7Inputs
        {
            NodeId = new NodeId("ns=3;s=\"Inputs\""),
            DisplayName = "Inputs",
            Variables =
            [
                new S7Variable() { NodeId = var1NodeId, DisplayName = "TestBool" },
                new S7Variable() { NodeId = var2NodeId, DisplayName = "TestInt" }
            ]
        };

        _mockSession.Setup(s => s.Read(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ReadValueIdCollection>(), out It.Ref<DataValueCollection>.IsAny, out It.Ref<DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionReadCallback((RequestHeader _, double _, TimestampsToReturn _, ReadValueIdCollection _, out DataValueCollection results, out DiagnosticInfoCollection diags) =>
            {
                results =
                [
                    new(new Variant(true)) { StatusCode = StatusCodes.Good },
                    new(new Variant((short)123)) { StatusCode = StatusCodes.Good }
                ];
                diags = [];
            }))
            .Returns(new ResponseHeader { ServiceResult = StatusCodes.Good });
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = client.ReadValuesOfElement(elementWithStructure, "S7");

        // Assert
        Assert.NotNull(result);
        Assert.NotSame(elementWithStructure, result);
        Assert.Equal(2, result.Variables.Count);

        var resultVar1 = result.Variables.First(v => v.DisplayName == "TestBool");
        Assert.True((bool)resultVar1.Value!);
        Assert.Equal(StatusCodes.Good, resultVar1.StatusCode);
        Assert.Equal("S7.Inputs.TestBool", resultVar1.FullPath);

        var resultVar2 = result.Variables.First(v => v.DisplayName == "TestInt");
        Assert.Equal((short)123, resultVar2.Value);
        Assert.Equal(StatusCodes.Good, resultVar2.StatusCode);
        Assert.Equal("S7.Inputs.TestInt", resultVar2.FullPath);
    }

    [Fact]
    public void ReadValuesOfElement_WithStructVariable_RecursivelyReadsAndPopulatesMembers()
    {
        // Arrange
        var client = CreateSut();
        var structNodeId = new NodeId("ns=3;s=\"MyDb.MyStruct\"");
        var structMember1NodeId = new NodeId("ns=3;s=\"MyDb.MyStruct.MemberBool\"");
        var structMember2NodeId = new NodeId("ns=3;s=\"MyDb.MyStruct.MemberDInt\"");

        var elementWithStructure = new S7DataBlockGlobal
        {
            NodeId = new NodeId("ns=3;s=\"MyDb\""),
            DisplayName = "MyDb",
            Variables =
            [
                new S7Variable() { NodeId = structNodeId, DisplayName = "MyStruct", S7Type = S7DataType.STRUCT }
            ]
        };

        // Mock browsing for the struct members
        var structMemberReferences = new ReferenceDescriptionCollection
    {
        new() { NodeId = structMember1NodeId, DisplayName = "MemberBool" },
        new() { NodeId = structMember2NodeId, DisplayName = "MemberDInt" }
    };
        _mockSession.Setup(s => s.Browse(It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.Is<BrowseDescriptionCollection>(bdc => bdc[0].NodeId == structNodeId),
                out It.Ref<BrowseResultCollection>.IsAny, out It.Ref<DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionBrowseCallback((RequestHeader _, ViewDescription _, uint _, BrowseDescriptionCollection _, out BrowseResultCollection brc, out DiagnosticInfoCollection dic) =>
            {
                brc = [new() { References = structMemberReferences, StatusCode = StatusCodes.Good }];
                dic = [];
            }));

        // Mock reading the values of the struct members
        _mockSession.Setup(s => s.Read(It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.Is<ReadValueIdCollection>(rvc => rvc.Count == 2 && rvc.Any(r => r.NodeId == structMember1NodeId)),
                out It.Ref<DataValueCollection>.IsAny, out It.Ref<DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionReadCallback((RequestHeader _, double _, TimestampsToReturn _, ReadValueIdCollection _, out DataValueCollection results, out DiagnosticInfoCollection diags) =>
            {
                results =
                [
                    new(new Variant(false)) { StatusCode = StatusCodes.Good },
                new(new Variant(9999)) { StatusCode = StatusCodes.Good }
                ];
                diags = [];
            }));

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = client.ReadValuesOfElement(elementWithStructure);

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
    public void ReadValuesOfElement_WithS7Char_AppliesCorrectConverter()
    {
        // Arrange
        var client = CreateSut();
        var charNodeId = new NodeId("ns=3;s=\"MyDb.MyChar\"");

        var elementWithStructure = new S7DataBlockGlobal
        {
            DisplayName = "MyDb",
            NodeId = new NodeId("ns=3;s=\"MyDb\""),
            Variables =
            [
                new S7Variable() { NodeId = charNodeId, DisplayName = "MyChar", S7Type = S7DataType.CHAR }
            ]
        };

        // The OPC UA server represents S7 CHAR as a Byte.
        // The converter should turn this byte (ASCII 65) into the char 'A'.
        const byte opcRawValue = (byte)65;

        _mockSession.Setup(s => s.Read(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.Is<ReadValueIdCollection>(nodes => nodes.Count == 1 && nodes[0].NodeId == charNodeId),
                out It.Ref<DataValueCollection>.IsAny, out It.Ref<DiagnosticInfoCollection>.IsAny))
            .Callback(new SessionReadCallback((RequestHeader _, double _, TimestampsToReturn _, ReadValueIdCollection _, out DataValueCollection results, out DiagnosticInfoCollection diags) =>
            {
                results = [new(new Variant(opcRawValue)) { StatusCode = StatusCodes.Good }];
                diags = [];
            }))
            .Returns(new ResponseHeader { ServiceResult = StatusCodes.Good });
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = client.ReadValuesOfElement(elementWithStructure);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.Variables);

        var resultVar = result.Variables[0];
        Assert.Equal("MyChar", resultVar.DisplayName);
        Assert.Equal(StatusCodes.Good, resultVar.StatusCode);

        // Verify the conversion
        Assert.Equal('A', resultVar.Value);
        Assert.Equal(typeof(char), resultVar.SystemType);
        Assert.Equal(opcRawValue, resultVar.RawOpcValue);
    }

    private delegate void SessionReadCallback(
        RequestHeader requestHeader,
        double maxAge,
        TimestampsToReturn timestampsToReturn,
        ReadValueIdCollection nodesToRead,
        out DataValueCollection results,
        out DiagnosticInfoCollection diagnosticInfos);

    #endregion Reading Tests

    #region Write Tests

    [Fact]
    public async Task WriteRawVariableAsync_WhenNotConnected_ReturnsFalseAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        _mockSession.SetupGet(s => s.Connected).Returns(false);
        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);
        var nodeId = new NodeId("ns=3;s=\"MyVar\"");

        // Act
        var result = await client.WriteRawVariableAsync(nodeId, 42);

        // Assert
        Assert.False(result);
        _mockSession.Verify(s => s.WriteAsync(It.IsAny<RequestHeader>(), It.IsAny<WriteValueCollection>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var nodeId = new NodeId("ns=3;s=\"MyVar\"");
        const int valueToWrite = 123;

        var response = new ResponseHeader { ServiceResult = StatusCodes.Good };
        var results = new StatusCodeCollection { StatusCodes.Good };
        var diags = new DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(
                It.IsAny<RequestHeader>(),
                It.Is<WriteValueCollection>(wvc => wvc.Count == 1 && wvc[0].NodeId == nodeId && (int)wvc[0].Value.Value! == valueToWrite),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.WriteRawVariableAsync(nodeId, valueToWrite);

        // Assert
        Assert.True(result);
        _mockSession.Verify(s => s.WriteAsync(It.IsAny<RequestHeader>(), It.IsAny<WriteValueCollection>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteRawVariableAsync_WhenWriteFails_ReturnsFalseAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        var nodeId = new NodeId("ns=3;s=\"MyVar\"");

        var response = new ResponseHeader { ServiceResult = StatusCodes.Good };
        var results = new StatusCodeCollection { StatusCodes.BadTypeMismatch };
        var diags = new DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(It.IsAny<RequestHeader>(), It.IsAny<WriteValueCollection>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags });

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.WriteRawVariableAsync(nodeId, "some value");

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
        var nodeId = new NodeId("ns=3;s=\"MyCharVar\"");
        const char userValue = 'A';
        const byte expectedOpcValue = (byte)65;

        var response = new ResponseHeader { ServiceResult = StatusCodes.Good };
        var results = new StatusCodeCollection { StatusCodes.Good };
        var diags = new DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(
                It.IsAny<RequestHeader>(),
                It.Is<WriteValueCollection>(wvc =>
                    wvc.Count == 1 &&
                    wvc[0].NodeId == nodeId &&
                    (byte)wvc[0].Value.Value! == expectedOpcValue),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags })
            .Verifiable();

        PrivateFieldHelpers.SetPrivateField(client, "_session", _mockSession.Object);

        // Act
        var result = await client.WriteVariableAsync(nodeId, userValue, S7DataType.CHAR);

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
            NodeId = new NodeId("ns=3;s=\"MyTimeVar\""),
            DisplayName = "MyTimeVar",
            S7Type = S7DataType.TIME
        };
        var userValue = TimeSpan.FromSeconds(10);
        const int expectedOpcValue = 10000;

        var response = new ResponseHeader { ServiceResult = StatusCodes.Good };
        var results = new StatusCodeCollection { StatusCodes.Good };
        var diags = new DiagnosticInfoCollection();

        _mockSession.Setup(s => s.WriteAsync(
                It.IsAny<RequestHeader>(),
                It.Is<WriteValueCollection>(wvc =>
                    wvc.Count == 1 &&
                    wvc[0].NodeId == variable.NodeId &&
                    (int)wvc[0].Value.Value! == expectedOpcValue),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WriteResponse { ResponseHeader = response, Results = results, DiagnosticInfos = diags })
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
}