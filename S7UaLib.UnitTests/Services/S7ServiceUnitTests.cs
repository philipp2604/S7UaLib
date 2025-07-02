using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua;
using S7UaLib.Client.Contracts;
using S7UaLib.DataStore;
using S7UaLib.Events;
using S7UaLib.S7.Structure;
using S7UaLib.S7.Structure.Contracts;
using S7UaLib.Serialization.Json;
using S7UaLib.Serialization.Models;
using S7UaLib.Services;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace S7UaLib.UnitTests.Services;

[Trait("Category", "Unit")]
public class S7ServiceUnitTests
{
    private readonly Mock<IS7UaClient> _mockClient;
    private readonly S7DataStore _realDataStore;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7Service>> _mockLogger;
    private readonly MockFileSystem _mockFileSystem;

    public S7ServiceUnitTests()
    {
        _mockClient = new Mock<IS7UaClient>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<S7Service>>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);
        _realDataStore = new S7DataStore(_mockLoggerFactory.Object);
        _mockFileSystem = new MockFileSystem();
    }

    private S7Service CreateSut()
    {
        // Use the internal constructor for testing with mocks
        return new S7Service(_mockClient.Object, _realDataStore, _mockFileSystem, _mockLoggerFactory.Object);
    }

    #region DiscoverStructure Tests

    [Fact]
    public async Task DiscoverStructure_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(false);
        var sut = CreateSut();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.DiscoverStructureAsync());
        Assert.Contains("Client is not connected", ex.Message);
    }

    [Fact]
    public async Task DiscoverStructure_WhenConnected_CallsClientAndPopulatesStore()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var globalDbShell = new S7DataBlockGlobal { DisplayName = "MyGlobalDb" };
        var globalDbFull = globalDbShell with { Variables = [new S7Variable { DisplayName = "Var1" }] };

        _mockClient.Setup(c => c.GetAllGlobalDataBlocksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([globalDbShell]);
        _mockClient.Setup(c => c.DiscoverElementAsync(globalDbShell, It.IsAny<CancellationToken>())).ReturnsAsync(globalDbFull);

        _mockClient.Setup(c => c.GetAllInstanceDataBlocksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _mockClient.Setup(c => c.GetInputsAsync(It.IsAny<CancellationToken>())).ReturnsAsync((S7Inputs?)null);
        _mockClient.Setup(c => c.GetOutputsAsync(It.IsAny<CancellationToken>())).ReturnsAsync((S7Outputs?)null);
        _mockClient.Setup(c => c.GetMemoryAsync(It.IsAny<CancellationToken>())).ReturnsAsync((S7Memory?)null);
        _mockClient.Setup(c => c.GetTimersAsync(It.IsAny<CancellationToken>())).ReturnsAsync((S7Timers?)null);
        _mockClient.Setup(c => c.GetCountersAsync(It.IsAny<CancellationToken>())).ReturnsAsync((S7Counters?)null);

        // Act
        await sut.DiscoverStructureAsync();

        // Assert
        _mockClient.Verify(c => c.GetAllGlobalDataBlocksAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockClient.Verify(c => c.GetAllInstanceDataBlocksAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockClient.Verify(c => c.GetInputsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockClient.Verify(c => c.DiscoverElementAsync(globalDbShell, It.IsAny<CancellationToken>()), Times.Exactly(2));

        var variable = sut.GetVariable("DataBlocksGlobal.MyGlobalDb.Var1");
        Assert.NotNull(variable);
        Assert.Equal("Var1", variable.DisplayName);
    }

    #endregion DiscoverStructure Tests

    #region ReadAllVariables Tests

    [Fact]
    public async Task ReadAllVariables_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(false);
        var sut = CreateSut();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await sut.ReadAllVariablesAsync());
        Assert.Contains("Client is not connected", ex.Message);
    }

    [Fact]
    public async Task ReadAllVariables_WhenValueChanges_FiresVariableValueChangedEvent()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var oldVar = new S7Variable { DisplayName = "TestVar", Value = 100 };
        var initialDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] };
        _realDataStore.SetStructure([initialDb], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        var newVar = new S7Variable { DisplayName = "TestVar", Value = 200, FullPath = "DataBlocksGlobal.DB1.TestVar" };
        var updatedDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [newVar] };
        _mockClient.Setup(c => c.ReadValuesOfElementAsync(It.IsAny<S7DataBlockGlobal>(), "DataBlocksGlobal", It.IsAny<CancellationToken>())).ReturnsAsync(updatedDb);

        VariableValueChangedEventArgs? eventArgs = null;
        sut.VariableValueChanged += (sender, e) => eventArgs = e;

        // Act
        await sut.ReadAllVariablesAsync();

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal(100, eventArgs.OldVariable.Value);
        Assert.Equal(200, eventArgs.NewVariable.Value);
        Assert.Equal("DataBlocksGlobal.DB1.TestVar", eventArgs.NewVariable.FullPath);
    }

    [Fact]
    public async Task ReadAllVariables_WhenValueIsSame_DoesNotFireEvent()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var oldVar = new S7Variable { DisplayName = "TestVar", Value = 100 };
        var initialDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] };
        _realDataStore.SetStructure([initialDb], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        var sameVar = new S7Variable { DisplayName = "TestVar", Value = 100, FullPath = "DataBlocksGlobal.DB1.TestVar" };
        var sameDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [sameVar] };
        _mockClient.Setup(c => c.ReadValuesOfElementAsync(It.IsAny<S7DataBlockGlobal>(), "DataBlocksGlobal", It.IsAny<CancellationToken>())).ReturnsAsync(sameDb);

        bool eventFired = false;
        sut.VariableValueChanged += (sender, e) => eventFired = true;

        // Act
        await sut.ReadAllVariablesAsync();

        // Assert
        Assert.False(eventFired, "VariableValueChanged event should not fire for same values.");
    }

    #endregion ReadAllVariables Tests

    #region GetVariable Tests

    [Fact]
    public void GetVariable_WhenVarExists_ReturnsVariable()
    {
        // Arrange
        var sut = CreateSut();
        var variable = new S7Variable { DisplayName = "MyVar" };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        // Act
        var result = sut.GetVariable("DataBlocksGlobal.DB1.MyVar");

        // Assert
        Assert.NotNull(result);
        Assert.Same(variable, result);
    }

    [Fact]
    public void GetVariable_WhenVarDoesNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.BuildCache();

        // Act
        var result = sut.GetVariable("Some.Non.Existent.Path");

        // Assert
        Assert.Null(result);
    }

    #endregion GetVariable Tests

    #region UpdateVariableType Tests

    [Fact]
    public async Task UpdateVariableType_WhenPathIsValid_UpdatesTypeAndReconvertsValue()
    {
        // Arrange
        var sut = CreateSut();
        // RawOpcValue for a Char 'A' is byte 65.
        var oldVar = new S7Variable { DisplayName = "TestVar", S7Type = S7UaLib.S7.Types.S7DataType.UNKNOWN, RawOpcValue = (byte)65, Value = (byte)65 };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        VariableValueChangedEventArgs? eventArgs = null;
        sut.VariableValueChanged += (s, e) => eventArgs = e;

        const string path = "DataBlocksGlobal.DB1.TestVar";

        // Act
        var success = await sut.UpdateVariableTypeAsync(path, S7UaLib.S7.Types.S7DataType.CHAR);

        // Assert
        Assert.True(success);

        var updatedVar = sut.GetVariable(path);
        Assert.NotNull(updatedVar);
        Assert.Equal(S7UaLib.S7.Types.S7DataType.CHAR, updatedVar.S7Type);
        Assert.Equal('A', updatedVar.Value);

        // Assert that the event was fired because the value changed (from byte 65 to char 'A')
        Assert.NotNull(eventArgs);
        Assert.Equal((byte)65, eventArgs.OldVariable.Value);
        Assert.Equal('A', eventArgs.NewVariable.Value);
    }

    [Fact]
    public async Task UpdateVariableType_WhenPathIsInvalid_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.BuildCache();

        // Act
        var success = await sut.UpdateVariableTypeAsync("Some.Invalid.Path", S7UaLib.S7.Types.S7DataType.BOOL);

        // Assert
        Assert.False(success);
    }

    #endregion UpdateVariableType Tests

    #region WriteVariableAsync Tests

    [Fact]
    public async Task WriteVariableAsync_WhenPathIsValid_CallsClientWrite()
    {
        // Arrange
        var sut = CreateSut();
        var nodeId = new NodeId("ns=3;s=Test");
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = nodeId, S7Type = S7UaLib.S7.Types.S7DataType.INT };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.WriteVariableAsync(nodeId, It.IsAny<object>(), It.IsAny<S7UaLib.S7.Types.S7DataType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Verifiable();

        const string path = "DataBlocksGlobal.DB1.TestVar";
        const short valueToWrite = 123;

        // Act
        var success = await sut.WriteVariableAsync(path, valueToWrite);

        // Assert
        Assert.True(success);
        _mockClient.Verify(c => c.WriteVariableAsync(nodeId, valueToWrite, S7UaLib.S7.Types.S7DataType.INT, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WriteVariableAsync_WhenPathIsInvalid_ReturnsFalseAndDoesNotCallClient()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.BuildCache();

        // Act
        var success = await sut.WriteVariableAsync("Some.Invalid.Path", 123);

        // Assert
        Assert.False(success);
        _mockClient.Verify(c => c.WriteVariableAsync(It.IsAny<NodeId>(), It.IsAny<object>(), It.IsAny<S7UaLib.S7.Types.S7DataType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteVariableAsync_WhenClientThrows_ReturnsFalseAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var nodeId = new NodeId("ns=3;s=Test");
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = nodeId, S7Type = S7UaLib.S7.Types.S7DataType.INT };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.WriteVariableAsync(It.IsAny<NodeId>(), It.IsAny<object>(), It.IsAny<S7UaLib.S7.Types.S7DataType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Write failed"));

        // Act
        var success = await sut.WriteVariableAsync("DataBlocksGlobal.DB1.TestVar", 123);

        // Assert
        Assert.False(success);
        _mockLogger.Verify(
                log => log.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Failed to write value")),
                    It.IsAny<InvalidOperationException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
    }

    #endregion WriteVariableAsync Tests

    #region File Operations Tests

    [Fact]
    public async Task SaveStructureAsync_WithValidPath_SerializesDataAndWritesToMockFileSystem()
    {
        // Arrange
        var sut = CreateSut();
        const string filePath = "C:\\data\\structure.json";

        var globalDb = new S7DataBlockGlobal { DisplayName = "TestDB" };
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        _mockClient.Setup(c => c.GetAllGlobalDataBlocksAsync(It.IsAny<CancellationToken>())).ReturnsAsync([globalDb]);
        _mockClient.Setup(c => c.DiscoverElementAsync(
               It.IsAny<IS7StructureElement>(),
               It.IsAny<CancellationToken>()))
           .Returns<IS7StructureElement, CancellationToken>(async (element, _) =>
           {
               await Task.CompletedTask;
               return element;
           });

        await sut.DiscoverStructureAsync();

        _mockFileSystem.AddDirectory("C:\\data");

        // Act
        await sut.SaveStructureAsync(filePath);

        // Assert
        Assert.True(_mockFileSystem.FileExists(filePath));
        var fileData = _mockFileSystem.GetFile(filePath);
        Assert.NotNull(fileData);

        var deserializedModel = JsonSerializer.Deserialize<S7StructureStorageModel>(fileData.TextContents, S7StructureSerializer.Options);
        Assert.NotNull(deserializedModel);
        Assert.Single(deserializedModel.DataBlocksGlobal);
        Assert.Equal("TestDB", deserializedModel.DataBlocksGlobal[0].DisplayName);
    }

    [Fact]
    public async Task SaveStructureAsync_WithNullPath_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.SaveStructureAsync(null!));
    }

    [Fact]
    public async Task SaveStructureAsync_WithEmptyPath_ThrowsArgumentException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => sut.SaveStructureAsync(string.Empty));
    }

    [Fact]
    public async Task LoadStructureAsync_WithValidFile_LoadsDataAndRebuildsCache()
    {
        // Arrange
        var sut = CreateSut();
        const string filePath = "C:\\config\\structure.json";

        var testModel = new S7StructureStorageModel
        {
            Inputs = new S7Inputs { DisplayName = "Inputs", Variables = [new S7Variable() { DisplayName = "TestInput", FullPath = "Inputs.TestInput" }] }
        };
        var json = JsonSerializer.Serialize(testModel, S7StructureSerializer.Options);

        _mockFileSystem.AddFile(filePath, new MockFileData(json));

        // Act
        await sut.LoadStructureAsync(filePath);

        // Assert
        Assert.NotNull(_realDataStore.Inputs);
        Assert.Equal("Inputs", _realDataStore.Inputs.DisplayName);

        var element = sut.GetVariable("Inputs.TestInput");
        Assert.NotNull(element);
        Assert.IsType<S7Variable>(element);
    }

    [Fact]
    public async Task LoadStructureAsync_WhenFileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var sut = CreateSut();
        const string filePath = "C:\\i_do_not_exist.json";

        // Act & Assert
        await Assert.ThrowsAsync<System.IO.FileNotFoundException>(() => sut.LoadStructureAsync(filePath));
    }

    #endregion File Operations Tests

    #region Connection Tests

    [Fact]
    public void IsConnected_ReturnsClientIsConnected()
    {
        // Arrange
        var sut = CreateSut();
        _mockClient.Setup(c => c.IsConnected).Returns(true);

        // Act & Assert
        Assert.True(sut.IsConnected);

        // Arrange 2
        _mockClient.Setup(c => c.IsConnected).Returns(false);

        // Act & Assert 2
        Assert.False(sut.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_CallsClientConnectAsync()
    {
        // Arrange
        var sut = CreateSut();
        const string serverUrl = "opc.tcp://localhost:4840";
        const bool useSecurity = true;

        _mockClient.Setup(c => c.ConnectAsync(serverUrl, useSecurity, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await sut.ConnectAsync(serverUrl, useSecurity);

        // Assert
        _mockClient.Verify();
    }

    [Fact]
    public async Task Disconnect_CallsClientDisconnect()
    {
        // Arrange
        var sut = CreateSut();
        const bool leaveOpen = true;

        _mockClient.Setup(c => c.DisconnectAsync(leaveOpen, It.IsAny<CancellationToken>())).Verifiable();

        // Act
        await sut.DisconnectAsync(leaveOpen);

        // Assert
        _mockClient.Verify();
    }

    [Theory]
    [InlineData("Connecting")]
    [InlineData("Connected")]
    [InlineData("Disconnecting")]
    [InlineData("Disconnected")]
    [InlineData("Reconnecting")]
    [InlineData("Reconnected")]
    public void ClientEvents_AreForwardedByService(string eventName)
    {
        // Arrange
        var eventArgs = new ConnectionEventArgs();
        bool eventFired = false;
        var sut = CreateSut();

        switch (eventName)
        {
            case "Connecting":
                sut.Connecting += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); };
                break;

            case "Connected":
                sut.Connected += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); };
                break;

            case "Disconnecting":
                sut.Disconnecting += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); };
                break;

            case "Disconnected":
                sut.Disconnected += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); };
                break;

            case "Reconnecting":
                sut.Reconnecting += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); };
                break;

            case "Reconnected":
                sut.Reconnected += (s, e) => { eventFired = true; Assert.Same(eventArgs, e); };
                break;
        }

        // Act
        switch (eventName)
        {
            case "Connecting":
                _mockClient.Raise(m => m.Connecting += null, _mockClient.Object, eventArgs);
                break;

            case "Connected":
                _mockClient.Raise(m => m.Connected += null, _mockClient.Object, eventArgs);
                break;

            case "Disconnecting":
                _mockClient.Raise(m => m.Disconnecting += null, _mockClient.Object, eventArgs);
                break;

            case "Disconnected":
                _mockClient.Raise(m => m.Disconnected += null, _mockClient.Object, eventArgs);
                break;

            case "Reconnecting":
                _mockClient.Raise(m => m.Reconnecting += null, _mockClient.Object, eventArgs);
                break;

            case "Reconnected":
                _mockClient.Raise(m => m.Reconnected += null, _mockClient.Object, eventArgs);
                break;
        }

        // Assert
        Assert.True(eventFired, $"The {eventName} event should have been fired.");
    }

    #endregion Connection Tests
}