using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
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
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.ReadValuesOfElementAsync(new S7DataBlockGlobal()));
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
    public async Task DiscoverElementAsync_WithSimpleElement_DispatchesToDiscoverVariables()
    {
        // Arrange
        var elementShell = new S7DataBlockGlobal { NodeId = "ns=1;s=DB1" };
        var expectedElement = elementShell with { Variables = [new S7Variable { DisplayName = "MyVar" }] };
        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync<IUaNode?>(
                It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedElement);

        // Act
        var result = await _sut.DiscoverElementAsync(elementShell);

        // Assert
        Assert.Same(expectedElement, result);
        _mockSessionPool.Verify(p => p.ExecuteWithSessionAsync<IUaNode?>(
            It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DiscoverElementAsync_WithInstanceDb_DispatchesToDiscoverInstance()
    {
        // Arrange
        var elementShell = new S7DataBlockInstance { NodeId = "ns=1;s=IDB1" };
        var expectedElement = elementShell with { Static = new S7InstanceDbSection { DisplayName = "Static" } };
        _mockSessionPool.Setup(p => p.ExecuteWithSessionAsync<IUaNode?>(
                It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedElement);

        // Act
        var result = await _sut.DiscoverElementAsync(elementShell);

        // Assert
        Assert.Same(expectedElement, result);

        _mockSessionPool.Verify(p => p.ExecuteWithSessionAsync<IUaNode?>(
            It.IsAny<Func<Opc.Ua.Client.ISession, Task<IUaNode?>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReadValuesOfElementAsync_WithNestedStruct_CorrectlyDiscoversAndReadsValues()
    {
        // Arrange
        var elementToRead = new S7DataBlockGlobal
        {
            NodeId = "ns=3;s=\"MyDb\"",
            DisplayName = "MyDb",
            Variables =
            [
                new S7Variable() {
                    NodeId = "ns=3;s=\"MyDb.MyStruct\"", DisplayName = "MyStruct", S7Type = S7DataType.STRUCT
                }
            ]
        };

        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        mockSession.Setup(s => s.Connected).Returns(true);

        var structMemberRefs = new Opc.Ua.ReferenceDescriptionCollection
        {
            new() { NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyStruct.MemberBool\""), DisplayName = "MemberBool", NodeClass = Opc.Ua.NodeClass.Variable},
            new() { NodeId = new Opc.Ua.NodeId("ns=3;s=\"MyDb.MyStruct.MemberInt\""), DisplayName = "MemberInt", NodeClass = Opc.Ua.NodeClass.Variable },
        };
        mockSession.Setup(s => s.Browse(null, null, It.IsAny<uint>(), It.Is<Opc.Ua.BrowseDescriptionCollection>(c => c[0].NodeId.ToString() == "ns=3;s=\"MyDb.MyStruct\""), out It.Ref<Opc.Ua.BrowseResultCollection>.IsAny, out It.Ref<Opc.Ua.DiagnosticInfoCollection>.IsAny))
            .Callback(new BrowseCallback((Opc.Ua.RequestHeader _, Opc.Ua.ViewDescription _, uint _, Opc.Ua.BrowseDescriptionCollection _, out Opc.Ua.BrowseResultCollection brc, out Opc.Ua.DiagnosticInfoCollection dic) =>
            {
                brc = [new() { References = structMemberRefs, StatusCode = Opc.Ua.StatusCodes.Good }];
                dic = [];
            }));

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
        var result = await _sut.ReadValuesOfElementAsync(elementToRead, "MyRoot");

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