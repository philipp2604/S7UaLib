using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua;
using S7UaLib.Client.Contracts;
using S7UaLib.DataStore;
using S7UaLib.Events;
using S7UaLib.Services;
using S7UaLib.S7.Structure;
using S7UaLib.S7.Structure.Contracts;
using System.Collections;

namespace S7UaLib.UnitTests.Services;

[Trait("Category", "Unit")]
public class S7ServiceUnitTests
{
    private readonly Mock<IS7UaClient> _mockClient;
    private readonly S7DataStore _realDataStore;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7Service>> _mockLogger;

    public S7ServiceUnitTests()
    {
        _mockClient = new Mock<IS7UaClient>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<S7Service>>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);
        _realDataStore = new S7DataStore(_mockLoggerFactory.Object);
    }

    private S7Service CreateSut()
    {
        // Use the internal constructor for testing with mocks
        return new S7Service(_mockClient.Object, _realDataStore, _mockLoggerFactory.Object);
    }

    #region DiscoverStructure Tests

    [Fact]
    public void DiscoverStructure_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(false);
        var sut = CreateSut();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => sut.DiscoverStructure());
        Assert.Contains("Client is not connected", ex.Message);
    }

    [Fact]
    public void DiscoverStructure_WhenConnected_CallsClientAndPopulatesStore()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var globalDbShell = new S7DataBlockGlobal { DisplayName = "MyGlobalDb" };
        var globalDbFull = globalDbShell with { Variables = [new S7Variable { DisplayName = "Var1" }] };

        _mockClient.Setup(c => c.GetAllGlobalDataBlocks()).Returns([globalDbShell]);
        _mockClient.Setup(c => c.DiscoverElement(globalDbShell)).Returns(globalDbFull);

        _mockClient.Setup(c => c.GetAllInstanceDataBlocks()).Returns([]);
        _mockClient.Setup(c => c.GetInputs()).Returns((S7Inputs?)null);
        _mockClient.Setup(c => c.GetOutputs()).Returns((S7Outputs?)null);
        _mockClient.Setup(c => c.GetMemory()).Returns((S7Memory?)null);
        _mockClient.Setup(c => c.GetTimers()).Returns((S7Timers?)null);
        _mockClient.Setup(c => c.GetCounters()).Returns((S7Counters?)null);

        // Act
        sut.DiscoverStructure();

        // Assert
        _mockClient.Verify(c => c.GetAllGlobalDataBlocks(), Times.Once);
        _mockClient.Verify(c => c.GetAllInstanceDataBlocks(), Times.Once);
        _mockClient.Verify(c => c.GetInputs(), Times.Once);
        _mockClient.Verify(c => c.DiscoverElement(globalDbShell), Times.Once);

        var variable = sut.GetVariable("DataBlocksGlobal.MyGlobalDb.Var1");
        Assert.NotNull(variable);
        Assert.Equal("Var1", variable.DisplayName);
    }

    #endregion DiscoverStructure Tests

    #region ReadAllVariables Tests

    [Fact]
    public void ReadAllVariables_WhenNotConnected_ThrowsInvalidOperationException()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(false);
        var sut = CreateSut();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => sut.ReadAllVariables());
        Assert.Contains("Client is not connected", ex.Message);
    }

    [Fact]
    public void ReadAllVariables_WhenValueChanges_FiresVariableValueChangedEvent()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var oldVar = new S7Variable { DisplayName = "TestVar", Value = 100 };
        var initialDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] };
        _realDataStore.DataBlocksGlobal = [initialDb];
        _realDataStore.BuildCache();

        var newVar = new S7Variable { DisplayName = "TestVar", Value = 200, FullPath = "DataBlocksGlobal.DB1.TestVar" };
        var updatedDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [newVar] };
        _mockClient.Setup(c => c.ReadValuesOfElement(It.IsAny<S7DataBlockGlobal>(), "DataBlocksGlobal")).Returns(updatedDb);

        VariableValueChangedEventArgs? eventArgs = null;
        sut.VariableValueChanged += (sender, e) => eventArgs = e;

        // Act
        sut.ReadAllVariables();

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal(100, eventArgs.OldVariable.Value);
        Assert.Equal(200, eventArgs.NewVariable.Value);
        Assert.Equal("DataBlocksGlobal.DB1.TestVar", eventArgs.NewVariable.FullPath);
    }

    [Fact]
    public void ReadAllVariables_WhenValueIsSame_DoesNotFireEvent()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var oldVar = new S7Variable { DisplayName = "TestVar", Value = 100 };
        var initialDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] };
        _realDataStore.DataBlocksGlobal = [initialDb];
        _realDataStore.BuildCache();

        var sameVar = new S7Variable { DisplayName = "TestVar", Value = 100, FullPath = "DataBlocksGlobal.DB1.TestVar" };
        var sameDb = new S7DataBlockGlobal { DisplayName = "DB1", Variables = [sameVar] };
        _mockClient.Setup(c => c.ReadValuesOfElement(It.IsAny<S7DataBlockGlobal>(), "DataBlocksGlobal")).Returns(sameDb);

        bool eventFired = false;
        sut.VariableValueChanged += (sender, e) => eventFired = true;

        // Act
        sut.ReadAllVariables();

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
        _realDataStore.DataBlocksGlobal = [new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }];
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

    #endregion

    #region UpdateVariableType Tests

    [Fact]
    public void UpdateVariableType_WhenPathIsValid_UpdatesTypeAndReconvertsValue()
    {
        // Arrange
        var sut = CreateSut();
        // RawOpcValue for a Char 'A' is byte 65.
        var oldVar = new S7Variable { DisplayName = "TestVar", S7Type = S7UaLib.S7.Types.S7DataType.UNKNOWN, RawOpcValue = (byte)65, Value = (byte)65 };
        _realDataStore.DataBlocksGlobal = [new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] }];
        _realDataStore.BuildCache();

        VariableValueChangedEventArgs? eventArgs = null;
        sut.VariableValueChanged += (s, e) => eventArgs = e;

        const string path = "DataBlocksGlobal.DB1.TestVar";

        // Act
        var success = sut.UpdateVariableType(path, S7UaLib.S7.Types.S7DataType.CHAR);

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
    public void UpdateVariableType_WhenPathIsInvalid_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.BuildCache();

        // Act
        var success = sut.UpdateVariableType("Some.Invalid.Path", S7UaLib.S7.Types.S7DataType.BOOL);

        // Assert
        Assert.False(success);
    }

    #endregion

    #region WriteVariableAsync Tests

    [Fact]
    public async Task WriteVariableAsync_WhenPathIsValid_CallsClientWrite()
    {
        // Arrange
        var sut = CreateSut();
        var nodeId = new NodeId("ns=3;s=Test");
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = nodeId, S7Type = S7UaLib.S7.Types.S7DataType.INT };
        _realDataStore.DataBlocksGlobal = [new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }];
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.WriteVariableAsync(nodeId, It.IsAny<object>(), It.IsAny<S7UaLib.S7.Types.S7DataType>()))
            .ReturnsAsync(true)
            .Verifiable();

        const string path = "DataBlocksGlobal.DB1.TestVar";
        const short valueToWrite = 123;

        // Act
        var success = await sut.WriteVariableAsync(path, valueToWrite);

        // Assert
        Assert.True(success);
        _mockClient.Verify(c => c.WriteVariableAsync(nodeId, valueToWrite, S7UaLib.S7.Types.S7DataType.INT), Times.Once);
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
        _mockClient.Verify(c => c.WriteVariableAsync(It.IsAny<NodeId>(), It.IsAny<object>(), It.IsAny<S7UaLib.S7.Types.S7DataType>()), Times.Never);
    }

    [Fact]
    public async Task WriteVariableAsync_WhenClientThrows_ReturnsFalseAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var nodeId = new NodeId("ns=3;s=Test");
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = nodeId, S7Type = S7UaLib.S7.Types.S7DataType.INT };
        _realDataStore.DataBlocksGlobal = [new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }];
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.WriteVariableAsync(It.IsAny<NodeId>(), It.IsAny<object>(), It.IsAny<S7UaLib.S7.Types.S7DataType>()))
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

    #endregion
}