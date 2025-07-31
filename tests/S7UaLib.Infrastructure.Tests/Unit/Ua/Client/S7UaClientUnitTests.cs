using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.S7.Converters;
using S7UaLib.Infrastructure.Ua.Client;
using System.Collections.ObjectModel;

namespace S7UaLib.Infrastructure.Tests.Unit.Ua.Client;

[Trait("Category", "Unit")]
public class S7UaClientUnitTests
{
    private readonly Mock<ILogger<S7UaClient>> _mockLogger;
    private readonly Mock<IS7UaMainClient> _mockMainClient;
    private readonly Mock<IS7UaSessionPool> _mockSessionPool;
    private readonly S7UaClient _sut;

    // Helper delegates for mocking complex OPC UA calls
    private delegate void BrowseCallback(Opc.Ua.RequestHeader rh, Opc.Ua.ViewDescription v, uint m, Opc.Ua.BrowseDescriptionCollection bdc, out Opc.Ua.BrowseResultCollection brc, out Opc.Ua.DiagnosticInfoCollection dic);

    private delegate void ReadCallback(Opc.Ua.RequestHeader rh, double a, Opc.Ua.TimestampsToReturn t, Opc.Ua.ReadValueIdCollection r, out Opc.Ua.DataValueCollection dvc, out Opc.Ua.DiagnosticInfoCollection dic);

    public S7UaClientUnitTests()
    {
        _mockLogger = new Mock<ILogger<S7UaClient>>();
        _mockMainClient = new Mock<IS7UaMainClient>();
        _mockSessionPool = new Mock<IS7UaSessionPool>();
        var mockUserIdentity = new UserIdentity();

        _sut = new S7UaClient(
            _mockMainClient.Object,
            _mockSessionPool.Object,
            mockUserIdentity,
            _mockLogger.Object,
            (_, __) => { });
    }

    #region Constructor and Dispose Tests

    [Fact]
    public void Dispose_WhenCalled_DisposesDependenciesAndUnsubscribesFromEvents()
    {
        // Act
        _sut.Dispose();

        // Assert
        _mockMainClient.Verify(m => m.Dispose(), Times.Once, "MainClient should be disposed.");
        _mockSessionPool.Verify(p => p.Dispose(), Times.Once, "SessionPool should be disposed.");

        // Verify event handlers were detached by raising an event and ensuring the SUT doesn't forward it.
        bool eventFired = false;
        _sut.Connected += (s, e) => eventFired = true;
        _mockMainClient.Raise(m => m.Connected += null, new ConnectionEventArgs());

        Assert.False(eventFired, "Event handler should have been detached upon disposal.");
    }

    [Fact]
    public async Task AnyMethod_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        _sut.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.ConnectAsync("opc.tcp://localhost"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.GetAllGlobalDataBlocksAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.ReadNodeValuesAsync(new S7DataBlockGlobal()));
        Assert.Throws<ObjectDisposedException>(() => _sut.SaveConfiguration("path"));
    }

    #endregion Constructor and Dispose Tests

    #region Event Coordination Logic Tests

    [Fact]
    public void OnMainClientConnected_WhenPoolInitializesSuccessfully_FiresConnectedEvent()
    {
        // Arrange
        bool eventFired = false;
        _sut.Connected += (s, e) => eventFired = true;
        var mockOpcConfig = new Opc.Ua.ApplicationConfiguration();
        var mockEndpoint = new Opc.Ua.ConfiguredEndpoint();
        _mockMainClient.SetupGet(m => m.OpcApplicationConfiguration).Returns(mockOpcConfig);
        _mockMainClient.SetupGet(m => m.ConfiguredEndpoint).Returns(mockEndpoint);
        _mockSessionPool.Setup(p => p.InitializeAsync(mockOpcConfig, mockEndpoint)).Returns(Task.CompletedTask);

        // Act
        _mockMainClient.Raise(m => m.Connected += null, new ConnectionEventArgs());

        // Assert
        _mockSessionPool.Verify(p => p.InitializeAsync(mockOpcConfig, mockEndpoint), Times.Once);
        Assert.True(eventFired, "The SUT's Connected event should fire after the pool is initialized.");
    }

    [Fact]
    public void OnMainClientConnected_WhenPoolInitializationFails_DoesNotFireConnectedEventAndLogsError()
    {
        // Arrange
        bool eventFired = false;
        _sut.Connected += (s, e) => eventFired = true;
        var mockOpcConfig = new Opc.Ua.ApplicationConfiguration();
        var mockEndpoint = new Opc.Ua.ConfiguredEndpoint();
        _mockMainClient.SetupGet(m => m.OpcApplicationConfiguration).Returns(mockOpcConfig);
        _mockMainClient.SetupGet(m => m.ConfiguredEndpoint).Returns(mockEndpoint);
        var ex = new InvalidOperationException("Pool init failed");
        _mockSessionPool.Setup(p => p.InitializeAsync(mockOpcConfig, mockEndpoint)).ThrowsAsync(ex);

        // Act
        _mockMainClient.Raise(m => m.Connected += null, new ConnectionEventArgs());

        // Assert
        _mockSessionPool.Verify(p => p.InitializeAsync(mockOpcConfig, mockEndpoint), Times.Once);
        Assert.False(eventFired);
        _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), ex, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public void OnMainClientReconnected_WhenPoolReinitializesSuccessfully_FiresReconnectedEvent()
    {
        // Arrange
        bool eventFired = false;
        _sut.Reconnected += (s, e) => eventFired = true;
        var mockOpcConfig = new Opc.Ua.ApplicationConfiguration();
        var mockEndpoint = new Opc.Ua.ConfiguredEndpoint();
        _mockMainClient.SetupGet(m => m.OpcApplicationConfiguration).Returns(mockOpcConfig);
        _mockMainClient.SetupGet(m => m.ConfiguredEndpoint).Returns(mockEndpoint);
        _mockSessionPool.Setup(p => p.InitializeAsync(mockOpcConfig, mockEndpoint)).Returns(Task.CompletedTask);

        // Act
        _mockMainClient.Raise(m => m.Reconnected += null, new ConnectionEventArgs());

        // Assert
        _mockSessionPool.Verify(p => p.InitializeAsync(mockOpcConfig, mockEndpoint), Times.Once);
        Assert.True(eventFired, "The SUT's Reconnected event should fire after the pool is re-initialized.");
    }

    [Fact]
    public void OnMainClientReconnected_WhenPoolReinitializationFails_DoesNotFireReconnectedEventAndLogsError()
    {
        // Arrange
        bool eventFired = false;
        _sut.Reconnected += (s, e) => eventFired = true;
        var mockOpcConfig = new Opc.Ua.ApplicationConfiguration();
        var mockEndpoint = new Opc.Ua.ConfiguredEndpoint();
        _mockMainClient.SetupGet(m => m.OpcApplicationConfiguration).Returns(mockOpcConfig);
        _mockMainClient.SetupGet(m => m.ConfiguredEndpoint).Returns(mockEndpoint);
        var ex = new InvalidOperationException("Pool re-init failed");
        _mockSessionPool.Setup(p => p.InitializeAsync(mockOpcConfig, mockEndpoint)).ThrowsAsync(ex);

        // Act
        _mockMainClient.Raise(m => m.Reconnected += null, new ConnectionEventArgs());

        // Assert
        Assert.False(eventFired);
        _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), ex, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    #endregion Event Coordination Logic Tests

    #region Event Forwarding and Property Delegation Tests

    [Theory]
    [InlineData("Connecting")]
    [InlineData("Disconnecting")]
    [InlineData("Disconnected")]
    [InlineData("Reconnecting")]
    public void ConnectionEvents_WhenMainClientFires_AreForwardedBySut(string eventName)
    {
        // Arrange
        bool eventFired = false;
        var eventArgs = new ConnectionEventArgs();

        switch (eventName)
        {
            case "Connecting": _sut.Connecting += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); }; break;
            case "Disconnecting": _sut.Disconnecting += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); }; break;
            case "Disconnected": _sut.Disconnected += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); }; break;
            case "Reconnecting": _sut.Reconnecting += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); }; break;
        }

        // Act
        switch (eventName)
        {
            case "Connecting": _mockMainClient.Raise(m => m.Connecting += null, eventArgs); break;
            case "Disconnecting": _mockMainClient.Raise(m => m.Disconnecting += null, eventArgs); break;
            case "Disconnected": _mockMainClient.Raise(m => m.Disconnected += null, eventArgs); break;
            case "Reconnecting": _mockMainClient.Raise(m => m.Reconnecting += null, eventArgs); break;
        }

        // Assert
        Assert.True(eventFired, $"{eventName} event was not forwarded.");
    }

    [Fact]
    public void MonitoredItemChangedEvent_WhenMainClientFires_IsForwardedBySut()
    {
        // Arrange
        bool eventFired = false;
        var mockItem = new Mock<Opc.Ua.Client.MonitoredItem>();
        var mockNotif = new Opc.Ua.MonitoredItemNotification();
        var eventArgs = new MonitoredItemChangedEventArgs(mockItem.Object, mockNotif);

        _sut.MonitoredItemChanged += (sender, args) =>
        {
            eventFired = true;
            Assert.Same(eventArgs, args);
        };

        // Act
        _mockMainClient.Raise(m => m.MonitoredItemChanged += null, eventArgs);

        // Assert
        Assert.True(eventFired, "MonitoredItemChanged event was not forwarded.");
    }

    [Fact]
    public void KeepAliveInterval_GetterAndSetter_DelegatesToMainClient()
    {
        // Arrange
        _mockMainClient.SetupProperty(m => m.KeepAliveInterval, 5000);
        // Act
        var initial = _sut.KeepAliveInterval;
        _sut.KeepAliveInterval = 10000;
        var final = _sut.KeepAliveInterval;
        // Assert
        Assert.Equal(5000, initial);
        Assert.Equal(10000, final);
        _mockMainClient.VerifySet(m => m.KeepAliveInterval = 10000, Times.Once);
        _mockMainClient.VerifyGet(m => m.KeepAliveInterval, Times.Exactly(2));
    }

    #endregion Event Forwarding and Property Delegation Tests

    #region Stateful Method Delegation (to Main Client)

    [Fact]
    public async Task ConfigureAsync_WhenCalled_DelegatesToMainClient()
    {
        var appConfig = new ApplicationConfiguration();
        await _sut.ConfigureAsync(appConfig);
        _mockMainClient.Verify(m => m.ConfigureAsync(appConfig), Times.Once);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_WhenCalled_DelegatesToMainClient()
    {
        _mockMainClient.Setup(m => m.CreateSubscriptionAsync(100)).ReturnsAsync(true);
        var result = await _sut.CreateSubscriptionAsync(100);
        Assert.True(result);
        _mockMainClient.Verify(m => m.CreateSubscriptionAsync(100), Times.Once);
    }

    #endregion Stateful Method Delegation (to Main Client)

    #region Method-Specific Logic Tests (Conversion, etc.)

    [Fact]
    public async Task WriteVariableAsync_WithValidInputs_GetsConverterAndDelegatesToRawWrite()
    {
        // Arrange
        const string nodeId = "ns=1;s=MyTime";
        var userValue = TimeSpan.FromSeconds(10);
        const S7DataType s7Type = S7DataType.S5TIME;

        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync(It.IsAny<Func<Opc.Ua.Client.ISession, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _sut.WriteVariableAsync(nodeId, userValue, s7Type);

        // Assert
        _mockSessionPool.Verify(p => p.ExecuteWithSessionAsync(It.IsAny<Func<Opc.Ua.Client.ISession, Task<bool>>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteVariableAsync_ForVariableObject_ThrowsWhenNodeIdIsNull()
    {
        var variableWithoutNodeId = new S7Variable { DisplayName = "BadVar", S7Type = S7DataType.BOOL };
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _sut.WriteVariableAsync(variableWithoutNodeId, true));
        Assert.Contains("has no NodeId", ex.Message);
        Assert.Equal("variable", ex.ParamName);
    }

    [Fact]
    public void GetConverter_ForKnownType_ReturnsSpecificConverter()
    {
        var converter = _sut.GetConverter(S7DataType.DATE, typeof(object));
        Assert.IsType<S7DateConverter>(converter);
    }

    [Fact]
    public void GetConverter_ForUnknownType_ReturnsDefaultConverterWithFallbackType()
    {
        var converter = _sut.GetConverter(S7DataType.UNKNOWN, typeof(Guid));
        var defaultConverter = Assert.IsType<DefaultConverter>(converter);
        Assert.Equal(typeof(Guid), defaultConverter.TargetType);
    }

    #endregion Method-Specific Logic Tests (Conversion, etc.)

    #region UDT, Discovery, and Value Conversion Tests

    // Helper classes for UDT tests
    public class MyCustomUdt
    {
        public bool IsActive { get; set; }
        public short Counter { get; set; }
    }

    public class MyCustomUdtConverter : UdtConverterBase<MyCustomUdt>
    {
        public MyCustomUdtConverter() : base("MyUdt_T")
        {
        }

        public override MyCustomUdt ConvertFromUdtMembers(IReadOnlyList<IS7Variable> structMembers)
        {
            return new MyCustomUdt
            {
                IsActive = GetMemberValue<bool>(FindMember(structMembers, "IsActive")),
                Counter = GetMemberValue<short>(FindMember(structMembers, "Counter"))
            };
        }

        public override IReadOnlyList<IS7Variable> ConvertToUdtMembers(MyCustomUdt udtInstance, IReadOnlyList<IS7Variable> structMemberTemplate)
        {
            var activeMemberTemplate = structMemberTemplate.First(m => m.DisplayName == "IsActive");
            var counterMemberTemplate = structMemberTemplate.First(m => m.DisplayName == "Counter");

            return new List<IS7Variable>
            {
                (S7Variable)activeMemberTemplate with { Value = udtInstance.IsActive },
                (S7Variable)counterMemberTemplate with { Value = udtInstance.Counter }
            }.AsReadOnly();
        }
    }

    [Fact]
    public void RegisterUdtConverter_WhenCalled_RegistersConverterInRegistry()
    {
        // Arrange
        var udtConverter = new MyCustomUdtConverter();

        // Act
        _sut.RegisterUdtConverter(udtConverter);

        // Assert
        var registry = _sut.GetUdtTypeRegistry();
        Assert.True(registry.HasCustomConverter("MyUdt_T"));
        Assert.Same(udtConverter, registry.GetCustomConverter("MyUdt_T"));
    }

    [Fact]
    public async Task DiscoverNodeAsync_ForUdtVariable_CorrectlyIdentifiesTypeAndMembers()
    {
        // Arrange
        var dbShell = new S7DataBlockGlobal { NodeId = "ns=3;s=\"MyDb\"", DisplayName = "MyDb" };
        var udtVariableNodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyUdt\"");
        var udtMember1NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyUdt.Member1\"");
        var udtDataTypeNodeId = new Opc.Ua.NodeId("ns=3;s=MyUdt_T");

        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        mockSession.Setup(s => s.Connected).Returns(true);
        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync(It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>, CancellationToken>(async (op, _) => await op(mockSession.Object));

        Func<Opc.Ua.BrowseDescriptionCollection, Opc.Ua.NodeId, bool> browsePredicate = (bdc, nodeId) =>
        {
            if (bdc is null || bdc.Count != 1) return false;
            var bd = bdc[0];
            return bd.NodeId == nodeId &&
                   bd.BrowseDirection == Opc.Ua.BrowseDirection.Forward &&
                   bd.ReferenceTypeId == Opc.Ua.ReferenceTypeIds.HierarchicalReferences &&
                   bd.IncludeSubtypes == true &&
                   bd.NodeClassMask == (uint)Opc.Ua.NodeClass.Variable;
        };

        // 1. Mock the Browse from DB to UDT variable, using the specific predicate.
        mockSession.Setup(s => s.Browse(
            It.IsAny<Opc.Ua.RequestHeader>(),
            It.IsAny<Opc.Ua.ViewDescription>(),
            It.IsAny<uint>(),
            It.Is<Opc.Ua.BrowseDescriptionCollection>(bdc => browsePredicate(bdc, dbShell.NodeId)),
            out It.Ref<Opc.Ua.BrowseResultCollection>.IsAny,
            out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
        .Callback((Opc.Ua.RequestHeader _, Opc.Ua.ViewDescription _, uint _, Opc.Ua.BrowseDescriptionCollection _, out Opc.Ua.BrowseResultCollection results, out Opc.Ua.DiagnosticInfoCollection diagnosticInfos) =>
        {
            results = new Opc.Ua.BrowseResultCollection { new() { StatusCode = Opc.Ua.StatusCodes.Good, References = [new() { NodeId = udtVariableNodeId, DisplayName = "MyUdt", NodeClass = Opc.Ua.NodeClass.Variable }] } };
            diagnosticInfos = [];
        });

        // 2. Mock Read for the UDT variable's DataType and ValueRank.
        mockSession.Setup(s => s.Read(null, 0, Opc.Ua.TimestampsToReturn.Neither, It.Is<Opc.Ua.ReadValueIdCollection>(c => c[0].NodeId == udtVariableNodeId), out It.Ref<Opc.Ua.DataValueCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback((Opc.Ua.RequestHeader _, double _, Opc.Ua.TimestampsToReturn _, Opc.Ua.ReadValueIdCollection _, out Opc.Ua.DataValueCollection dvc, out Opc.Ua.DiagnosticInfoCollection dic) =>
            {
                dvc = [new(new Opc.Ua.Variant(udtDataTypeNodeId)) { StatusCode = Opc.Ua.StatusCodes.Good }, new(new Opc.Ua.Variant(-1)) { StatusCode = Opc.Ua.StatusCodes.Good }];
                dic = [];
            });

        // 3. Mock the Browse from UDT variable to its members, using the specific predicate.
        mockSession.Setup(s => s.Browse(
            It.IsAny<Opc.Ua.RequestHeader>(),
            It.IsAny<Opc.Ua.ViewDescription>(),
            It.IsAny<uint>(),
            It.Is<Opc.Ua.BrowseDescriptionCollection>(bdc => browsePredicate(bdc, udtVariableNodeId)),
            out It.Ref<Opc.Ua.BrowseResultCollection>.IsAny,
            out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
        .Callback((Opc.Ua.RequestHeader _, Opc.Ua.ViewDescription _, uint _, Opc.Ua.BrowseDescriptionCollection _, out Opc.Ua.BrowseResultCollection results, out Opc.Ua.DiagnosticInfoCollection diagnosticInfos) =>
        {
            results = new Opc.Ua.BrowseResultCollection { new() { StatusCode = Opc.Ua.StatusCodes.Good, References = [new() { NodeId = udtMember1NodeId, DisplayName = "Member1", NodeClass = Opc.Ua.NodeClass.Variable }] } };
            diagnosticInfos = [];
        });

        // 4. Mock Read for the member's DataType and ValueRank.
        mockSession.Setup(s => s.Read(null, 0, Opc.Ua.TimestampsToReturn.Neither, It.Is<Opc.Ua.ReadValueIdCollection>(c => c[0].NodeId == udtMember1NodeId), out It.Ref<Opc.Ua.DataValueCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
             .Callback((Opc.Ua.RequestHeader _, double _, Opc.Ua.TimestampsToReturn _, Opc.Ua.ReadValueIdCollection _, out Opc.Ua.DataValueCollection dvc, out Opc.Ua.DiagnosticInfoCollection dic) =>
             {
                 dvc = [new(new Opc.Ua.Variant(Opc.Ua.DataTypeIds.Boolean)) { StatusCode = Opc.Ua.StatusCodes.Good }, new(new Opc.Ua.Variant(-1)) { StatusCode = Opc.Ua.StatusCodes.Good }];
                 dic = [];
             });

        // Act
        var result = await _sut.DiscoverNodeAsync(dbShell);

        // Assert
        var discoveredDb = Assert.IsType<S7DataBlockGlobal>(result);
        Assert.Single(discoveredDb.Variables);
        var udtVar = discoveredDb.Variables.First();

        Assert.Equal("MyUdt", udtVar.DisplayName);
        Assert.Equal(S7DataType.UDT, udtVar.S7Type);
        Assert.Equal("MyUdt_T", udtVar.UdtTypeName);

        Assert.NotNull(udtVar.StructMembers);
        Assert.Single(udtVar.StructMembers);
        var memberVar = udtVar.StructMembers.First();
        Assert.Equal("Member1", memberVar.DisplayName);
        Assert.Equal(S7DataType.BOOL, memberVar.S7Type);
    }

    [Fact]
    public async Task ReadNodeValuesAsync_WithCustomUdtConverter_ConvertsMembersToCustomObject()
    {
        // Arrange
        var udtConverter = new MyCustomUdtConverter();
        _sut.RegisterUdtConverter(udtConverter);

        var elementToRead = new S7DataBlockGlobal
        {
            NodeId = "ns=3;s=\"MyDb\"",
            DisplayName = "MyDb",
            Variables =
            [
                new S7Variable() {
                    NodeId = "ns=3;s=\"MyDb.MyUdtInstance\"",
                    DisplayName = "MyUdtInstance",
                    S7Type = S7DataType.UDT,
                    UdtTypeName = "MyUdt_T",
                    StructMembers = [
                        new S7Variable { NodeId = "ns=3;s=\"MyDb.MyUdtInstance.IsActive\"", DisplayName = "IsActive", S7Type = S7DataType.BOOL },
                        new S7Variable { NodeId = "ns=3;s=\"MyDb.MyUdtInstance.Counter\"", DisplayName = "Counter", S7Type = S7DataType.INT }
                    ]
                }
            ]
        };

        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        mockSession.Setup(s => s.Connected).Returns(true);
        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync(It.IsAny<Func<Opc.Ua.Client.ISession, Task<S7DataBlockGlobal>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Opc.Ua.Client.ISession, Task<S7DataBlockGlobal>>, CancellationToken>(async (operation, _) => await operation(mockSession.Object));

        // Setup Read to return values for the struct members
        mockSession.Setup(s => s.Read(null, 0, Opc.Ua.TimestampsToReturn.Neither, It.Is<Opc.Ua.ReadValueIdCollection>(c => c.Count == 2), out It.Ref<Opc.Ua.DataValueCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
             .Callback((Opc.Ua.RequestHeader _, double _, Opc.Ua.TimestampsToReturn _, Opc.Ua.ReadValueIdCollection _, out Opc.Ua.DataValueCollection dvc, out Opc.Ua.DiagnosticInfoCollection dic) =>
             {
                 dvc =
                 [
                     new(new Opc.Ua.Variant(true)) { StatusCode = Opc.Ua.StatusCodes.Good },
                     new(new Opc.Ua.Variant((short)555)) { StatusCode = Opc.Ua.StatusCodes.Good }
                 ];
                 dic = [];
             });

        // Act
        var result = await _sut.ReadNodeValuesAsync(elementToRead, "MyRoot");

        // Assert
        Assert.NotNull(result);
        var resultUdtVar = result.Variables.First();

        Assert.NotNull(resultUdtVar.Value);
        var udtInstance = Assert.IsType<MyCustomUdt>(resultUdtVar.Value);

        Assert.True(udtInstance.IsActive);
        Assert.Equal(555, udtInstance.Counter);

        var member1 = resultUdtVar.StructMembers.First(m => m.DisplayName == "IsActive");
        Assert.Equal(true, member1.Value);
        Assert.Equal("MyRoot.MyDb.MyUdtInstance.IsActive", member1.FullPath);

        var member2 = resultUdtVar.StructMembers.First(m => m.DisplayName == "Counter");
        Assert.Equal((short)555, member2.Value);
        Assert.Equal("MyRoot.MyDb.MyUdtInstance.Counter", member2.FullPath);
    }

    [Fact]
    public async Task WriteVariableAsync_WithCustomUdtConverter_ConvertsObjectAndWritesEachMember()
    {
        // Arrange
        var udtConverter = new MyCustomUdtConverter();
        _sut.RegisterUdtConverter(udtConverter);

        var udtInstanceToWrite = new MyCustomUdt { IsActive = true, Counter = 123 };

        var udtVariableTemplate = new S7Variable
        {
            NodeId = "ns=3;s=\"MyDb.MyUdtInstance\"",
            DisplayName = "MyUdtInstance",
            S7Type = S7DataType.UDT,
            UdtTypeName = "MyUdt_T",
            StructMembers = new List<IS7Variable>
            {
                new S7Variable { NodeId = "ns=3;s=\"MyDb.MyUdtInstance.IsActive\"", DisplayName = "IsActive", S7Type = S7DataType.BOOL },
                new S7Variable { NodeId = "ns=3;s=\"MyDb.MyUdtInstance.Counter\"", DisplayName = "Counter", S7Type = S7DataType.INT }
            }
        };

        var capturedWrites = new List<Opc.Ua.WriteValue>();

        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync(It.IsAny<Func<Opc.Ua.Client.ISession, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Opc.Ua.Client.ISession, Task<bool>>, CancellationToken>(async (operation, _) =>
            {
                var mockSession = new Mock<Opc.Ua.Client.ISession>();
                mockSession.Setup(s => s.Connected).Returns(true);
                mockSession.Setup(s => s.WriteAsync(null, It.IsAny<Opc.Ua.WriteValueCollection>(), It.IsAny<CancellationToken>()))
                    .Callback<Opc.Ua.RequestHeader, Opc.Ua.WriteValueCollection, CancellationToken>((_, wvc, _) => capturedWrites.AddRange(wvc))
                    .ReturnsAsync((Opc.Ua.RequestHeader _, Opc.Ua.WriteValueCollection wvc, CancellationToken _) => new Opc.Ua.WriteResponse
                    {
                        ResponseHeader = new Opc.Ua.ResponseHeader { ServiceResult = Opc.Ua.StatusCodes.Good },
                        Results = new Opc.Ua.StatusCodeCollection(wvc.Select(_ => new Opc.Ua.StatusCode(Opc.Ua.StatusCodes.Good)))
                    });

                return await operation(mockSession.Object);
            });

        // Act
        var result = await _sut.WriteVariableAsync(udtVariableTemplate, udtInstanceToWrite);

        // Assert
        Assert.True(result, "The overall write operation should succeed.");
        Assert.Equal(2, capturedWrites.Count);

        var boolWrite = capturedWrites.FirstOrDefault(w => w.NodeId.ToString() == udtVariableTemplate.StructMembers[0].NodeId);
        Assert.NotNull(boolWrite);
        Assert.Equal(true, boolWrite.Value.Value);

        var intWrite = capturedWrites.FirstOrDefault(w => w.NodeId.ToString() == udtVariableTemplate.StructMembers[1].NodeId);
        Assert.NotNull(intWrite);
        Assert.Equal((short)123, intWrite.Value.Value);
    }

    #endregion UDT, Discovery, and Value Conversion Tests

    #region Structure, Discovery, and Reading Logic Tests

    [Fact]
    public async Task GetAllGlobalDataBlocksAsync_WhenCalled_DelegatesToSessionPoolWithCorrectTypes()
    {
        // Arrange
        var expectedList = new List<S7DataBlockGlobal>().AsReadOnly();
        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync<ReadOnlyCollection<S7DataBlockGlobal>>(
                It.IsAny<Func<Opc.Ua.Client.ISession, Task<ReadOnlyCollection<S7DataBlockGlobal>>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedList);

        // Act
        var result = await _sut.GetAllGlobalDataBlocksAsync();

        // Assert
        Assert.Same(expectedList, result);
        _mockSessionPool.Verify(p => p.ExecuteWithSessionAsync<ReadOnlyCollection<S7DataBlockGlobal>>(
            It.IsAny<Func<Opc.Ua.Client.ISession, Task<ReadOnlyCollection<S7DataBlockGlobal>>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DiscoverNodeAsync_WithSimpleElement_DispatchesToUnifiedDiscovery()
    {
        // Arrange
        var elementShell = new S7DataBlockGlobal { NodeId = "ns=1;s=DB1" };
        var expectedElement = elementShell with { Variables = [new S7Variable { DisplayName = "MyVar" }] };
        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync<IUaNode?>(
                It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedElement);

        // Act
        var result = await _sut.DiscoverNodeAsync(elementShell);

        // Assert
        Assert.Same(expectedElement, result);
        _mockSessionPool.Verify(p => p.ExecuteWithSessionAsync<IUaNode?>(
            It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DiscoverNodeAsync_WithInstanceDb_DispatchesToUnifiedDiscovery()
    {
        // Arrange
        var elementShell = new S7DataBlockInstance { NodeId = "ns=1;s=IDB1" };
        var expectedElement = elementShell with { Static = new S7InstanceDbSection { DisplayName = "Static" } };
        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync<IUaNode?>(
                It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedElement);

        // Act
        var result = await _sut.DiscoverNodeAsync(elementShell);

        // Assert
        Assert.Same(expectedElement, result);

        _mockSessionPool.Verify(p => p.ExecuteWithSessionAsync<IUaNode?>(
            It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadNodeValuesAsync_WithNestedStruct_CorrectlyReadsValues()
    {
        // Arrange - In the new architecture, struct members are already discovered during initial discovery
        var elementToRead = new S7DataBlockGlobal
        {
            NodeId = "ns=3;s=\"MyDb\"",
            DisplayName = "MyDb",
            Variables =
            [
                new S7Variable() {
                    NodeId = "ns=3;s=\"MyDb.MyStruct\"",
                    DisplayName = "MyStruct",
                    S7Type = S7DataType.UDT,
                    StructMembers = [
                        new S7Variable { NodeId = "ns=3;s=\"MyDb.MyStruct.MemberBool\"", DisplayName = "MemberBool", S7Type = S7DataType.BOOL },
                        new S7Variable { NodeId = "ns=3;s=\"MyDb.MyStruct.MemberInt\"", DisplayName = "MemberInt", S7Type = S7DataType.INT }
                    ]
                }
            ]
        };

        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        mockSession.Setup(s => s.Connected).Returns(true);

        mockSession.Setup(s => s.Read(null, 0, Opc.Ua.TimestampsToReturn.Neither, It.Is<Opc.Ua.ReadValueIdCollection>(c => c.Count == 2), out It.Ref<Opc.Ua.DataValueCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
             .Callback(new ReadCallback((Opc.Ua.RequestHeader _, double _, Opc.Ua.TimestampsToReturn _, Opc.Ua.ReadValueIdCollection _, out Opc.Ua.DataValueCollection dvc, out Opc.Ua.DiagnosticInfoCollection dic) =>
             {
                 dvc = [
                     new(new Opc.Ua.Variant(true)) { StatusCode = Opc.Ua.StatusCodes.Good },
                     new(new Opc.Ua.Variant((short)555)) { StatusCode = Opc.Ua.StatusCodes.Good },
                 ];
                 dic = [];
             }));

        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync(It.IsAny<Func<Opc.Ua.Client.ISession, Task<S7DataBlockGlobal>>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Opc.Ua.Client.ISession, Task<S7DataBlockGlobal>>, CancellationToken>(async (operation, _) => await operation(mockSession.Object));

        // Act
        var result = await _sut.ReadNodeValuesAsync(elementToRead, "MyRoot");

        // Assert
        Assert.NotNull(result);
        var resultStruct = result.Variables.First(v => v.DisplayName == "MyStruct");
        Assert.NotNull(resultStruct.StructMembers);
        Assert.Equal(2, resultStruct.StructMembers.Count);

        var member1 = resultStruct.StructMembers.First(m => m.DisplayName == "MemberBool");
        Assert.Equal(true, member1.Value);
        Assert.Equal("MyRoot.MyDb.MyStruct.MemberBool", member1.FullPath);

        var member2 = resultStruct.StructMembers.First(m => m.DisplayName == "MemberInt");
        Assert.Equal((short)555, member2.Value);
        Assert.Equal("MyRoot.MyDb.MyStruct.MemberInt", member2.FullPath);
    }

    #endregion Structure, Discovery, and Reading Logic Tests
}