using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.DataStore;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.Serialization.Json;
using S7UaLib.Infrastructure.Serialization.Models;
using S7UaLib.Infrastructure.Ua.Client;
using S7UaLib.Services.S7;
using S7UaLib.TestHelpers;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;

namespace S7UaLib.Services.Tests.Unit;

[Trait("Category", "Unit")]
public class S7ServiceUnitTests
{
    private readonly Mock<IS7UaClient> _mockClient;
    private readonly S7DataStore _realDataStore;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7Service>> _mockLogger;
    private readonly MockFileSystem _mockFileSystem;
    private const string _appName = "S7UaLib Integration Tests";
    private const string _appUri = "urn:localhost:UA:S7UaLib:IntegrationTests";
    private const string _productUri = "uri:philipp2604:S7UaLib:IntegrationTests";

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

    #region Configuration Tests

    [Fact]
    public async Task Configure_CallsClientConfigure_WithAllParameters()
    {
        // Arrange
        var sut = CreateSut();
        var appConfig = CreateTestAppConfig();

        _mockClient.Setup(c => c.ConfigureAsync(appConfig))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await sut.ConfigureAsync(appConfig);

        // Assert
        _mockClient.Verify();
    }

    [Fact]
    public void SaveConfiguration_CallsClientSaveConfiguration()
    {
        // Arrange
        var sut = CreateSut();
        const string filePath = "C:\\temp\\my-config.xml";

        _mockClient.Setup(c => c.SaveConfiguration(filePath)).Verifiable();

        // Act
        sut.SaveConfiguration(filePath);

        // Assert
        _mockClient.Verify();
    }

    [Fact]
    public async Task LoadConfiguration_CallsClientLoadConfiguration()
    {
        // Arrange
        var sut = CreateSut();
        const string filePath = "C:\\temp\\my-config.xml";

        _mockClient.Setup(c => c.LoadConfigurationAsync(filePath))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await sut.LoadConfigurationAsync(filePath);

        // Assert
        _mockClient.Verify();
    }

    #endregion Configuration Tests

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
        _mockClient.Setup(c => c.DiscoverNodeAsync(globalDbShell, It.IsAny<CancellationToken>())).ReturnsAsync(globalDbFull);

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
        _mockClient.Verify(c => c.DiscoverNodeAsync(globalDbShell, It.IsAny<CancellationToken>()), Times.Once);

        var variable = sut.GetVariable("DataBlocksGlobal.MyGlobalDb.Var1");
        Assert.NotNull(variable);
        Assert.Equal("Var1", variable.DisplayName);
    }

    [Fact]
    public async Task DiscoverStructure_WithAllElementTypes_CorrectlyOrchestratesClientCalls()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var globalDbShell = new S7DataBlockGlobal { DisplayName = "GlobalDB" };
        var instanceDbShell = new S7DataBlockInstance { DisplayName = "InstanceDB" };
        var inputsShell = new S7Inputs { DisplayName = "Inputs" };

        var globalDbFull = globalDbShell with { Variables = [new S7Variable { DisplayName = "GlobalVar" }] };
        var instanceDbFull = instanceDbShell with { Static = new S7InstanceDbSection() };
        var inputsFull = inputsShell with { Variables = [new S7Variable { DisplayName = "InputVar" }] };

        _mockClient.Setup(c => c.GetAllGlobalDataBlocksAsync(default)).ReturnsAsync([globalDbShell]);
        _mockClient.Setup(c => c.GetAllInstanceDataBlocksAsync(default)).ReturnsAsync([instanceDbShell]);
        _mockClient.Setup(c => c.GetInputsAsync(default)).ReturnsAsync(inputsShell);
        _mockClient.Setup(c => c.GetOutputsAsync(default)).ReturnsAsync((S7Outputs?)null);
        _mockClient.Setup(c => c.GetMemoryAsync(default)).ReturnsAsync((S7Memory?)null);
        _mockClient.Setup(c => c.GetTimersAsync(default)).ReturnsAsync((S7Timers?)null);
        _mockClient.Setup(c => c.GetCountersAsync(default)).ReturnsAsync((S7Counters?)null);

        _mockClient.Setup(c => c.DiscoverNodeAsync(globalDbShell, default)).ReturnsAsync(globalDbFull);
        _mockClient.Setup(c => c.DiscoverNodeAsync(instanceDbShell, default)).ReturnsAsync(instanceDbFull);
        _mockClient.Setup(c => c.DiscoverNodeAsync(inputsShell, default)).ReturnsAsync(inputsFull);

        // Act
        await sut.DiscoverStructureAsync();

        // Assert
        _mockClient.Verify(c => c.GetAllGlobalDataBlocksAsync(default), Times.Once);
        _mockClient.Verify(c => c.GetAllInstanceDataBlocksAsync(default), Times.Once);
        _mockClient.Verify(c => c.GetInputsAsync(default), Times.Once);

        _mockClient.Verify(c => c.DiscoverNodeAsync(globalDbShell, default), Times.Once);
        _mockClient.Verify(c => c.DiscoverNodeAsync(instanceDbShell, default), Times.Once);
        _mockClient.Verify(c => c.DiscoverNodeAsync(inputsShell, default), Times.Once);

        Assert.Single(_realDataStore.DataBlocksGlobal);
        Assert.Single(_realDataStore.DataBlocksInstance);
        Assert.NotNull(_realDataStore.Inputs);
        Assert.NotNull(_realDataStore.Outputs);

        Assert.NotNull(sut.GetVariable("DataBlocksGlobal.GlobalDB.GlobalVar"));
        Assert.NotNull(sut.GetVariable("Inputs.InputVar"));
    }

    #endregion DiscoverStructure Tests

    #region RegisterGlobalDataBlock Tests

    [Fact]
    public async Task RegisterGlobalDataBlockAsync_WithValidDataBlock_CallsDataStoreAndReturnsTrue()
    {
        // Arrange
        var sut = CreateSut();
        var newDataBlock = new S7DataBlockGlobal
        {
            DisplayName = "DB10",
            FullPath = "DataBlocksGlobal.DB10",
            Variables = [new S7Variable { DisplayName = "Var1" }]
        };

        // Act
        var result = await sut.RegisterGlobalDataBlockAsync(newDataBlock);

        // Assert
        Assert.True(result);
        var dbs = sut.GetGlobalDataBlocks();
        Assert.Single(dbs);
        Assert.Equal("DB10", dbs[0].DisplayName);
        Assert.NotNull(sut.GetVariable("DataBlocksGlobal.DB10.Var1"));
    }

    [Fact]
    public async Task RegisterGlobalDataBlockAsync_WithNullDataBlock_ReturnsFalseAndLogsWarning()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = await sut.RegisterGlobalDataBlockAsync(null!);

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Cannot register variable: FullPath or variable is null.")), // Note: Log message is from a copy-paste but test verifies current state.
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterGlobalDataBlockAsync_WithInvalidFullPath_ReturnsFalse(string? invalidPath)
    {
        // Arrange
        var sut = CreateSut();
        var newDataBlock = new S7DataBlockGlobal
        {
            DisplayName = "DB10",
            FullPath = invalidPath
        };

        // Act
        var result = await sut.RegisterGlobalDataBlockAsync(newDataBlock);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task RegisterGlobalDataBlockAsync_WhenDataStoreRejectsDuplicate_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var existingDataBlock = new S7DataBlockGlobal
        {
            DisplayName = "DB1",
            FullPath = "DataBlocksGlobal.DB1"
        };
        // Pre-populate the store
        await sut.RegisterGlobalDataBlockAsync(existingDataBlock);

        var duplicateDataBlock = new S7DataBlockGlobal
        {
            DisplayName = "DB1_Duplicate",
            FullPath = "DataBlocksGlobal.DB1"
        };

        // Act
        var result = await sut.RegisterGlobalDataBlockAsync(duplicateDataBlock);

        // Assert
        Assert.False(result);
        var dbs = sut.GetGlobalDataBlocks();
        Assert.Single(dbs);
        Assert.Equal("DB1", dbs[0].DisplayName);
    }

    #endregion RegisterGlobalDataBlock Tests

    #region RegisterVariable Tests

    [Fact]
    public async Task RegisterVariableAsync_WithValidData_CallsDataStoreAndReturnsTrue()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.DB1.NewVar";

        _realDataStore.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1" }],
            [], null, null, null, null, null);
        _realDataStore.BuildCache();

        var newVar = new S7Variable { DisplayName = "NewVar", FullPath = path, NodeId = "ns=3;s=New", S7Type = S7DataType.BOOL };

        // Act
        var result = await sut.RegisterVariableAsync(newVar);

        // Assert
        Assert.True(result);
        var retrievedVar = sut.GetVariable(path);
        Assert.NotNull(retrievedVar);
        Assert.Equal("NewVar", retrievedVar.DisplayName);
        Assert.Equal("ns=3;s=New", retrievedVar.NodeId);
    }

    [Fact]
    public async Task RegisterVariableAsync_WhenDataStoreFails_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.NonExistentDB.NewVar"; // Parent DB does not exist
        var newVar = new S7Variable { DisplayName = "NewVar", FullPath = path, S7Type = S7DataType.BOOL };

        // Act
        var result = await sut.RegisterVariableAsync(newVar);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(null, "var")]
    [InlineData("path", null)]
    [InlineData("", "var")]
    public async Task RegisterVariableAsync_WithNullOrEmptyInputs_ReturnsFalse(string? path, string? varName)
    {
        // Arrange
        var sut = CreateSut();
        var variable = varName is null ? null : new S7Variable { FullPath = path, DisplayName = varName };

        // Act
        var result = await sut.RegisterVariableAsync(variable!);

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
                log => log.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Cannot register variable: FullPath or variable is null.")),
                    null,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once);
    }

    #endregion RegisterVariable Tests

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
        _mockClient.Setup(c => c.ReadNodeValuesAsync(It.IsAny<IS7DataBlockGlobal>(), "DataBlocksGlobal", It.IsAny<CancellationToken>())).ReturnsAsync(updatedDb);

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
    public async Task ReadAllVariables_WhenArrayValueChanges_FiresVariableValueChangedEvent()
    {
        // Arrange
        _mockClient.Setup(c => c.IsConnected).Returns(true);
        var sut = CreateSut();

        var oldVar = new S7Variable { DisplayName = "TestArray", FullPath = "DataBlocksGlobal.DB1.TestArray", Value = new byte[] { 1, 2, 3 } };
        var initialDb = new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1", Variables = [oldVar] };
        _realDataStore.SetStructure([initialDb], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        var newVar = new S7Variable { DisplayName = "TestArray", FullPath = "DataBlocksGlobal.DB1.TestArray", Value = new byte[] { 1, 2, 4 } };
        var updatedDb = new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1", Variables = [newVar] };
        _mockClient.Setup(c => c.ReadNodeValuesAsync(It.IsAny<IS7DataBlockGlobal>(), "DataBlocksGlobal", It.IsAny<CancellationToken>())).ReturnsAsync(updatedDb);

        VariableValueChangedEventArgs? eventArgs = null;
        sut.VariableValueChanged += (sender, e) => eventArgs = e;

        // Act
        await sut.ReadAllVariablesAsync();

        // Assert
        Assert.NotNull(eventArgs);
        Assert.Equal(new byte[] { 1, 2, 3 }, eventArgs.OldVariable.Value as byte[]);
        Assert.Equal(new byte[] { 1, 2, 4 }, eventArgs.NewVariable.Value as byte[]);
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
        _mockClient.Setup(c => c.ReadNodeValuesAsync(It.IsAny<IS7DataBlockGlobal>(), "DataBlocksGlobal", It.IsAny<CancellationToken>())).ReturnsAsync(sameDb);

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

    [Fact]
    public void FindVariables_WhenVarExists_ReturnsCorrectList()
    {
        // Arrange
        const string firstVar = "ThisVarShouldBeFound";
        const string secondVar = "ThisVarShouldBeFoundToo";
        const string thirdVar = "ButThisOneShouldNot";

        var sut = CreateSut();
        _realDataStore.SetStructure(
            [
                new S7DataBlockGlobal
                {
                    DisplayName = "DB1",
                    Variables =
                    [
                        new S7Variable { DisplayName = firstVar },
                        new S7Variable { DisplayName = secondVar },
                        new S7Variable { DisplayName = thirdVar }
                    ]
                }
            ],
            [],
            null, null, null, null, null);

        _realDataStore.BuildCache();

        // Act
        var foundVars = sut.FindVariablesWhere(x => x.DisplayName!.Contains("Found"));
        var notFoundVars = sut.FindVariablesWhere(x => x.S7Type == S7DataType.TIME);

        // Assert
        Assert.Collection(foundVars,
            x => Assert.Equal(firstVar, x.DisplayName),
            x => Assert.Equal(secondVar, x.DisplayName));
        Assert.Empty(notFoundVars);
    }

    [Fact]
    public void FindVariables_WhenVarDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.BuildCache();

        // Act
        var foundVars = sut.FindVariablesWhere(x => x.DisplayName!.Contains("VariableName"));

        // Assert
        Assert.Empty(foundVars);
    }

    [Fact]
    public void GetInputs_WhenInputsDoNotExist_ReturnsNewInputs()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var inputs = sut.GetInputs();

        // Assert
        Assert.NotNull(inputs);
        Assert.Empty(inputs.Variables);
        Assert.Equal("Inputs", inputs.DisplayName);
        Assert.Equal("Inputs", inputs.FullPath);
    }

    [Fact]
    public void GetInputs_WhenInputsDoExist_ReturnsInputs()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.SetStructure(
            [],
            [],
            new S7Inputs()
            {
                DisplayName = "Inputs",
                Variables = [
                    new S7Variable() { DisplayName = "TestInput", FullPath = "Inputs.TestInput" },
                    new S7Variable() { DisplayName = "TestInput2", FullPath = "Inputs.TestInput2" }
                ]
            },
            null, null, null, null);

        // Act
        var inputs = sut.GetInputs();

        // Assert
        Assert.NotNull(inputs);
        Assert.Equal("Inputs", inputs.DisplayName);
        Assert.Collection(inputs.Variables,
            x => Assert.Equal("Inputs.TestInput", x.FullPath),
            x => Assert.Equal("Inputs.TestInput2", x.FullPath));
    }

    [Fact]
    public void GetOutputs_WhenOutputsDoNotExist_ReturnsNewOutputs()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var outputs = sut.GetOutputs();

        // Assert
        Assert.NotNull(outputs);
        Assert.Empty(outputs.Variables);
        Assert.Equal("Outputs", outputs.DisplayName);
        Assert.Equal("Outputs", outputs.FullPath);
    }

    [Fact]
    public void GetOutputs_WhenOutputsDoExist_ReturnsOutputs()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.SetStructure(
            [],
            [],
            null,
            new S7Outputs()
            {
                DisplayName = "Outputs",
                Variables = [
                    new S7Variable() { DisplayName = "TestOutput", FullPath = "Outputs.TestOutput" },
                    new S7Variable() { DisplayName = "TestOutput2", FullPath = "Outputs.TestOutput2" }
                ]
            },
            null, null, null);

        // Act
        var outputs = sut.GetOutputs();

        // Assert
        Assert.NotNull(outputs);
        Assert.Equal("Outputs", outputs.DisplayName);
        Assert.Collection(outputs.Variables,
            x => Assert.Equal("Outputs.TestOutput", x.FullPath),
            x => Assert.Equal("Outputs.TestOutput2", x.FullPath));
    }

    [Fact]
    public void GetMemory_WhenMemoryDoesNotExist_ReturnsNewMemory()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var memory = sut.GetMemory();

        // Assert
        Assert.NotNull(memory);
        Assert.Empty(memory.Variables);
        Assert.Equal("Memory", memory.DisplayName);
        Assert.Equal("Memory", memory.FullPath);
    }

    [Fact]
    public void GetMemory_WhenMemoryDoesExist_ReturnsMemory()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.SetStructure(
            [],
            [],
            null,
            null,
            new S7Memory()
            {
                DisplayName = "Memory",
                Variables = [
                    new S7Variable() { DisplayName = "TestVar", FullPath = "Memory.TestVar" },
                    new S7Variable() { DisplayName = "TestVar2", FullPath = "Memory.TestVar2" }
                ]
            },
            null, null);

        // Act
        var memory = sut.GetMemory();

        // Assert
        Assert.NotNull(memory);
        Assert.Equal("Memory", memory.DisplayName);
        Assert.Collection(memory.Variables,
            x => Assert.Equal("Memory.TestVar", x.FullPath),
            x => Assert.Equal("Memory.TestVar2", x.FullPath));
    }

    [Fact]
    public void GetTimers_WhenTimersDoNotExist_ReturnsNewTimers()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var timers = sut.GetTimers();

        // Assert
        Assert.NotNull(timers);
        Assert.Empty(timers.Variables);
        Assert.Equal("Timers", timers.DisplayName);
        Assert.Equal("Timers", timers.FullPath);
    }

    [Fact]
    public void GetTimers_WhenTimersDoExist_ReturnsTimers()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.SetStructure(
            [],
            [],
            null,
            null,
            null,
            new S7Timers()
            {
                DisplayName = "Timers",
                Variables = [
                    new S7Variable() { DisplayName = "TestVar", FullPath = "Timers.TestVar" },
                    new S7Variable() { DisplayName = "TestVar2", FullPath = "Timers.TestVar2" }
                ]
            },
            null);

        // Act
        var timers = sut.GetTimers();

        // Assert
        Assert.NotNull(timers);
        Assert.Equal("Timers", timers.DisplayName);
        Assert.Collection(timers.Variables,
            x => Assert.Equal("Timers.TestVar", x.FullPath),
            x => Assert.Equal("Timers.TestVar2", x.FullPath));
    }

    [Fact]
    public void GetCounters_WhenCountersDoNotExist_ReturnsNewCounters()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var counters = sut.GetCounters();

        // Assert
        Assert.NotNull(counters);
        Assert.Empty(counters.Variables);
        Assert.Equal("Counters", counters.DisplayName);
        Assert.Equal("Counters", counters.FullPath);
    }

    [Fact]
    public void GetCounters_WhenCountersDoExist_ReturnsCounters()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.SetStructure(
            [],
            [],
            null,
            null,
            null,
            null,
            new S7Counters()
            {
                DisplayName = "Counters",
                Variables = [
                    new S7Variable() { DisplayName = "TestVar", FullPath = "Counters.TestVar" },
                    new S7Variable() { DisplayName = "TestVar2", FullPath = "Counters.TestVar2" }
                ]
            });

        // Act
        var counters = sut.GetCounters();

        // Assert
        Assert.NotNull(counters);
        Assert.Equal("Counters", counters.DisplayName);
        Assert.Collection(counters.Variables,
            x => Assert.Equal("Counters.TestVar", x.FullPath),
            x => Assert.Equal("Counters.TestVar2", x.FullPath));
    }

    [Fact]
    public void GetGlobalDataBlocks_WhenDataBlocksDoNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var dbs = sut.GetGlobalDataBlocks();

        // Assert
        Assert.Empty(dbs);
    }

    [Fact]
    public void GetGlobalDataBlocks_WhenDataBlocksDoExist_ReturnsDataBlocks()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.SetStructure(
            [
                new S7DataBlockGlobal()
                {
                    DisplayName = "DB1",
                    Variables = [
                            new S7Variable() { DisplayName = "TestVar", FullPath = "DataBlocksGlobal.DB1.TestVar" }
                    ]
                },
                new S7DataBlockGlobal()
                {
                    DisplayName = "DB2",
                    Variables = [
                            new S7Variable() { DisplayName = "TestVar2", FullPath = "DataBlocksGlobal.DB2.TestVar2" }
                    ]
                }
            ],
            [],
            null,
            null,
            null,
            null,
            null);

        // Act
        var dbs = sut.GetGlobalDataBlocks();

        // Assert
        Assert.NotEmpty(dbs);
        Assert.Collection(dbs,
            x =>
            {
                Assert.Equal("DB1", x.DisplayName);
                Assert.Single(x.Variables);
                Assert.Equal("DataBlocksGlobal.DB1.TestVar", x.Variables[0].FullPath);
            },
            x =>
            {
                Assert.Equal("DB2", x.DisplayName);
                Assert.Single(x.Variables);
                Assert.Equal("DataBlocksGlobal.DB2.TestVar2", x.Variables[0].FullPath);
            });
    }

    [Fact]
    public void GetInstanceDataBlocks_WhenDataBlocksDoNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var idbs = sut.GetInstanceDataBlocks();

        // Assert
        Assert.Empty(idbs);
    }

    [Fact]
    public void GetInstanceDataBlocks_WhenDataBlocksDoExist_ReturnsDataBlocks()
    {
        // Arrange
        var sut = CreateSut();
        _realDataStore.SetStructure(
            [],
            [
                new S7DataBlockInstance()
                {
                    DisplayName = "IDB1",
                    Static = new S7InstanceDbSection()
                    {
                        Variables = [
                            new S7Variable() { DisplayName = "TestVar", FullPath = "DataBlocksInstance.IDB1.TestVar" }
                        ]
                    }
                },
                new S7DataBlockInstance()
                {
                    DisplayName = "IDB2",
                    Static = new S7InstanceDbSection()
                    {
                        Variables = [
                            new S7Variable() { DisplayName = "TestVar2", FullPath = "DataBlocksInstance.IDB2.TestVar2" }
                        ]
                    }
                }
            ],
            null,
            null,
            null,
            null,
            null);

        // Act
        var idbs = sut.GetInstanceDataBlocks();

        // Assert
        Assert.NotEmpty(idbs);
        Assert.Collection(idbs,
            x =>
            {
                Assert.Equal("IDB1", x.DisplayName);
                Assert.NotNull(x.Static);
                Assert.Single(x.Static.Variables);
                Assert.Equal("DataBlocksInstance.IDB1.TestVar", x.Static.Variables[0].FullPath);
            },
            x =>
            {
                Assert.Equal("IDB2", x.DisplayName);
                Assert.NotNull(x.Static);
                Assert.Single(x.Static.Variables);
                Assert.Equal("DataBlocksInstance.IDB2.TestVar2", x.Static.Variables[0].FullPath);
            });
    }

    #endregion GetVariable Tests

    #region UpdateVariableType Tests

    [Fact]
    public async Task UpdateVariableType_WhenPathIsValid_UpdatesTypeAndReconvertsValue()
    {
        // Arrange
        var sut = CreateSut();
        // RawOpcValue for a Char 'A' is byte 65.
        var oldVar = new S7Variable { DisplayName = "TestVar", S7Type = S7DataType.UNKNOWN, RawOpcValue = (byte)65, Value = (byte)65 };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        // Mock the client's GetConverter method
        var mockCharConverter = new Infrastructure.S7.Converters.S7CharConverter(); // Using real converter for simplicity in test
        _mockClient.Setup(c => c.GetConverter(S7DataType.CHAR, typeof(byte)))
            .Returns(mockCharConverter);

        VariableValueChangedEventArgs? eventArgs = null;
        sut.VariableValueChanged += (s, e) => eventArgs = e;

        const string path = "DataBlocksGlobal.DB1.TestVar";

        // Act
        var success = await sut.UpdateVariableTypeAsync(path, S7DataType.CHAR);

        // Assert
        Assert.True(success);

        var updatedVar = sut.GetVariable(path);
        Assert.NotNull(updatedVar);
        Assert.Equal(S7DataType.CHAR, updatedVar.S7Type);
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
        var success = await sut.UpdateVariableTypeAsync("Some.Invalid.Path", S7DataType.BOOL);

        // Assert
        Assert.False(success);
    }

    [Fact]
    public async Task UpdateVariableType_ToStruct_DiscoversMembers()
    {
        // Arrange
        var sut = CreateSut();
        const string nodeId = "ns=3;s=MyStructVar";
        var oldVar = new S7Variable { DisplayName = "TestVar", NodeId = nodeId, S7Type = S7DataType.UNKNOWN };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVar] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        var discoveredMembers = new List<IS7Variable>
        {
            new S7Variable { DisplayName = "Member1" },
            new S7Variable { DisplayName = "Member2" }
        };
        var shellForDiscovery = new S7StructureElement { NodeId = nodeId, DisplayName = "TestVar" };

        _mockClient.Setup(c => c.IsConnected).Returns(true);
        _mockClient.Setup(c => c.DiscoverNodeAsync(It.Is<S7StructureElement>(s => s.NodeId == nodeId), default))
            .ReturnsAsync(new S7StructureElement { Variables = discoveredMembers });

        const string path = "DataBlocksGlobal.DB1.TestVar";

        // Act
        var success = await sut.UpdateVariableTypeAsync(path, S7DataType.STRUCT);

        // Assert
        Assert.True(success);
        _mockClient.Verify(c => c.DiscoverNodeAsync(It.Is<S7StructureElement>(s => s.NodeId == nodeId), default), Times.Once);

        var updatedVar = sut.GetVariable(path);
        Assert.NotNull(updatedVar);
        Assert.Equal(S7DataType.STRUCT, updatedVar.S7Type);
        Assert.Equal(2, updatedVar.StructMembers.Count);
        Assert.Equal("Member1", updatedVar.StructMembers[0].DisplayName);
    }

    #endregion UpdateVariableType Tests

    #region WriteVariableAsync Tests

    [Fact]
    public async Task WriteVariableAsync_WhenPathIsValid_CallsClientWrite()
    {
        // Arrange
        var sut = CreateSut();
        var nodeId = new Opc.Ua.NodeId("ns=3;s=Test");
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = nodeId.ToString(), S7Type = S7DataType.INT };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.WriteVariableAsync(nodeId.ToString(), It.IsAny<object>(), It.IsAny<S7DataType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .Verifiable();

        const string path = "DataBlocksGlobal.DB1.TestVar";
        const short valueToWrite = 123;

        // Act
        var success = await sut.WriteVariableAsync(path, valueToWrite);

        // Assert
        Assert.True(success);
        _mockClient.Verify(c => c.WriteVariableAsync(nodeId.ToString(), valueToWrite, S7DataType.INT, It.IsAny<CancellationToken>()), Times.Once);
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
        _mockClient.Verify(c => c.WriteVariableAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<S7DataType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WriteVariableAsync_WhenClientThrows_ReturnsFalseAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var nodeId = new Opc.Ua.NodeId("ns=3;s=Test");
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = nodeId.ToString(), S7Type = S7DataType.INT };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.WriteVariableAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<S7DataType>(), It.IsAny<CancellationToken>()))
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

    #region Subscription Tests

    [Fact]
    public async Task SubscribeToVariableAsync_WhenPathIsValid_CallsClientAndUpdatesStore()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.DB1.TestVar";
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = new Opc.Ua.NodeId(1).ToString(), IsSubscribed = false, SamplingInterval = 500 };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.IsConnected).Returns(true);
        _mockClient.Setup(c => c.CreateSubscriptionAsync(It.IsAny<int>())).ReturnsAsync(true);
        _mockClient.Setup(c => c.SubscribeToVariableAsync(It.IsAny<S7Variable>())).ReturnsAsync(true);

        // Act
        var result = await sut.SubscribeToVariableAsync(path, 500);

        // Assert
        Assert.True(result);
        _mockClient.Verify(c => c.CreateSubscriptionAsync(It.IsAny<int>()), Times.Once);
        _mockClient.Verify(c => c.SubscribeToVariableAsync(It.Is<S7Variable>(
            v => v.NodeId == variable.NodeId && v.SamplingInterval == 500
        )), Times.Once);

        var updatedVar = sut.GetVariable(path);
        Assert.NotNull(updatedVar);
        Assert.True(updatedVar.IsSubscribed);
        Assert.Equal(500u, updatedVar.SamplingInterval);
    }

    [Fact]
    public async Task SubscribeToAllConfiguredVariables_WhenCalled_SubscribesOnlyMarkedVariables()
    {
        // Arrange
        var sut = CreateSut();

        var varToSub1 = new S7Variable { DisplayName = "Var1", NodeId = new Opc.Ua.NodeId(1).ToString(), IsSubscribed = true, SamplingInterval = 100, FullPath = "DB.Var1" };
        var varToSub2 = new S7Variable { DisplayName = "Var2", NodeId = new Opc.Ua.NodeId(2).ToString(), IsSubscribed = true, SamplingInterval = 200, FullPath = "DB.Var2" };
        var varNotToSub = new S7Variable { DisplayName = "Var3", NodeId = new Opc.Ua.NodeId(3).ToString(), IsSubscribed = false, FullPath = "DB.Var3" };

        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB", Variables = [varToSub1, varToSub2, varNotToSub] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.IsConnected).Returns(true);
        _mockClient.Setup(c => c.CreateSubscriptionAsync(It.IsAny<int>())).ReturnsAsync(true);
        _mockClient.Setup(c => c.SubscribeToVariableAsync(It.IsAny<S7Variable>())).ReturnsAsync(true);

        // Act
        var result = await sut.SubscribeToAllConfiguredVariablesAsync();

        // Assert
        Assert.True(result, "The overall result should be true if all subscriptions succeed.");

        _mockClient.Verify(c => c.SubscribeToVariableAsync(It.Is<S7Variable>(
            v => v.NodeId == varToSub1.NodeId && v.SamplingInterval == varToSub1.SamplingInterval
        )), Times.Once, "Variable 1 should have been subscribed.");

        _mockClient.Verify(c => c.SubscribeToVariableAsync(It.Is<S7Variable>(
            v => v.NodeId == varToSub2.NodeId && v.SamplingInterval == varToSub2.SamplingInterval
        )), Times.Once, "Variable 2 should have been subscribed.");

        _mockClient.Verify(c => c.SubscribeToVariableAsync(It.Is<S7Variable>(
            v => v.NodeId == varNotToSub.NodeId
        )), Times.Never, "Variable 3 should NOT have been subscribed.");
    }

    [Fact]
    public async Task SubscribeToAllConfiguredVariables_WhenOneSubscriptionFails_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();

        var varToSub1 = new S7Variable { DisplayName = "Var1", NodeId = new Opc.Ua.NodeId(1).ToString(), IsSubscribed = true, FullPath = "DB.Var1" };
        var varToFail = new S7Variable { DisplayName = "Var2", NodeId = new Opc.Ua.NodeId(2).ToString(), IsSubscribed = true, FullPath = "DB.Var2" };

        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB", Variables = [varToSub1, varToFail] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.IsConnected).Returns(true);
        _mockClient.Setup(c => c.CreateSubscriptionAsync(It.IsAny<int>())).ReturnsAsync(true);

        _mockClient.Setup(c => c.SubscribeToVariableAsync(It.Is<S7Variable>(v => v.NodeId == varToSub1.NodeId))).ReturnsAsync(true);
        _mockClient.Setup(c => c.SubscribeToVariableAsync(It.Is<S7Variable>(v => v.NodeId == varToFail.NodeId))).ReturnsAsync(false);

        // Act
        var result = await sut.SubscribeToAllConfiguredVariablesAsync();

        // Assert
        Assert.False(result, "The overall result should be false if any subscription fails.");
    }

    [Fact]
    public async Task SubscribeToVariableAsync_WhenSubscriptionCreationFails_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.DB1.TestVar";
        var variable = new S7Variable { NodeId = "ns=1;s=Var" };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.IsConnected).Returns(true);
        _mockClient.Setup(c => c.CreateSubscriptionAsync(It.IsAny<int>())).ReturnsAsync(false); // Make it fail

        // Act
        var result = await sut.SubscribeToVariableAsync(path);

        // Assert
        Assert.False(result);
        _mockClient.Verify(c => c.SubscribeToVariableAsync(It.IsAny<S7Variable>()), Times.Never, "SubscribeToVariableAsync should not be called if subscription creation fails.");
    }

    [Fact]
    public void Client_MonitoredItemChanged_ForUnknownNodeId_LogsWarningAndDoesNothing()
    {
        // Arrange
        var sut = CreateSut();
        var unknownNodeId = new Opc.Ua.NodeId(999, 2);

        var monitoredItem = new Opc.Ua.Client.MonitoredItem { StartNodeId = unknownNodeId };
        var notification = new Opc.Ua.MonitoredItemNotification { Value = new Opc.Ua.DataValue(new Opc.Ua.Variant((short)200)) };
        var clientEventArgs = new MonitoredItemChangedEventArgs(monitoredItem, notification);

        bool eventFired = false;
        sut.VariableValueChanged += (s, e) => eventFired = true;

        // Act
        _mockClient.Raise(c => c.MonitoredItemChanged += null, clientEventArgs);

        // Assert
        Assert.False(eventFired, "No event should be fired for an unknown NodeId.");
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Received notification for unknown or unmapped NodeId")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task UnsubscribeFromVariableAsync_WhenPathIsValid_CallsClientAndUpdatesStore()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.DB1.TestVar";
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = new Opc.Ua.NodeId(1).ToString(), IsSubscribed = true };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [variable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        _mockClient.Setup(c => c.UnsubscribeFromVariableAsync(It.IsAny<S7Variable>())).ReturnsAsync(true);

        // Act
        var result = await sut.UnsubscribeFromVariableAsync(path);

        // Assert
        Assert.True(result);
        _mockClient.Verify(c => c.UnsubscribeFromVariableAsync(It.Is<S7Variable>(v => v.NodeId == variable.NodeId)), Times.Once);

        var updatedVar = sut.GetVariable(path);
        Assert.NotNull(updatedVar);
        Assert.False(updatedVar.IsSubscribed);
    }

    [Fact]
    public void Client_MonitoredItemChanged_WithValidData_UpdatesStoreAndFiresEvent()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.DB1.TestVar";
        var nodeId = new Opc.Ua.NodeId(123, 2);
        var oldVariable = new S7Variable
        {
            NodeId = nodeId.ToString(),
            DisplayName = "TestVar",
            Value = 100, // Alter Wert
            S7Type = S7DataType.INT,
            FullPath = path
        };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVariable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();

        var nodeIdToPathMap = (Dictionary<string, string>)PrivateFieldHelpers.GetPrivateField(sut, "_nodeIdToPathMap")!;
        nodeIdToPathMap[nodeId.ToString()] = path;

        var monitoredItem = new Opc.Ua.Client.MonitoredItem { StartNodeId = nodeId, DisplayName = "TestVar" };
        var notification = new Opc.Ua.MonitoredItemNotification { Value = new Opc.Ua.DataValue(new Opc.Ua.Variant((short)200)) };
        var clientEventArgs = new MonitoredItemChangedEventArgs(monitoredItem, notification);

        _mockClient.Setup(c => c.GetConverter(S7DataType.INT, typeof(short)))
            .Returns(new Infrastructure.S7.Converters.DefaultConverter(typeof(short)));

        VariableValueChangedEventArgs? receivedServiceArgs = null;
        sut.VariableValueChanged += (s, e) => receivedServiceArgs = e;

        // Act
        _mockClient.Raise(c => c.MonitoredItemChanged += null, clientEventArgs);

        // Assert
        Assert.NotNull(receivedServiceArgs);
        Assert.Equal(100, receivedServiceArgs.OldVariable.Value);
        Assert.Equal((short)200, receivedServiceArgs.NewVariable.Value);
        Assert.Equal(path, receivedServiceArgs.NewVariable.FullPath);

        var finalVar = sut.GetVariable(path);
        Assert.NotNull(finalVar);
        Assert.Equal((short)200, finalVar.Value);
    }

    [Fact]
    public void Client_MonitoredItemChanged_WithSameValue_DoesNotUpdateStoreOrFireEvent()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.DB1.TestVar";
        var nodeId = new Opc.Ua.NodeId(123, 2);
        const short sameValue = 150;
        var oldVariable = new S7Variable
        {
            NodeId = nodeId.ToString(),
            DisplayName = "TestVar",
            Value = sameValue,
            S7Type = S7DataType.INT,
            FullPath = path
        };
        _realDataStore.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", Variables = [oldVariable] }], [], null, null, null, null, null);
        _realDataStore.BuildCache();
        var nodeIdToPathMap = (Dictionary<string, string>)PrivateFieldHelpers.GetPrivateField(sut, "_nodeIdToPathMap")!;
        nodeIdToPathMap[nodeId.ToString()] = path;

        var monitoredItem = new Opc.Ua.Client.MonitoredItem { StartNodeId = nodeId };
        var notification = new Opc.Ua.MonitoredItemNotification { Value = new Opc.Ua.DataValue(new Opc.Ua.Variant(sameValue)) };
        var clientEventArgs = new MonitoredItemChangedEventArgs(monitoredItem, notification);

        _mockClient.Setup(c => c.GetConverter(It.IsAny<S7DataType>(), It.IsAny<Type>()))
            .Returns(new S7UaLib.Infrastructure.S7.Converters.DefaultConverter(typeof(short)));

        bool eventFired = false;
        sut.VariableValueChanged += (s, e) => eventFired = true;

        // Act
        _mockClient.Raise(c => c.MonitoredItemChanged += null, clientEventArgs);

        // Assert
        Assert.False(eventFired, "VariableValueChanged should not be fired if the value is the same.");

        var finalVar = sut.GetVariable(path);
        Assert.NotNull(finalVar);
        Assert.Same(oldVariable, finalVar);
    }

    #endregion Subscription Tests

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
        _mockClient.Setup(c => c.DiscoverNodeAsync(
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

    #region UDT Converter Tests

    [Fact]
    public void RegisterUdtConverter_ValidConverter_CallsClientRegisterUdtConverter()
    {
        // Arrange
        var sut = CreateSut();
        var mockConverter = new Mock<IUdtConverter<TestUdtType>>();
        mockConverter.Setup(c => c.UdtTypeName).Returns("TestUdt");

        // Act
        sut.RegisterUdtConverter(mockConverter.Object);

        // Assert
        _mockClient.Verify(c => c.RegisterUdtConverter(mockConverter.Object), Times.Once);
    }

    [Fact]
    public void RegisterUdtConverter_NullConverter_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => sut.RegisterUdtConverter<TestUdtType>(null!));
    }

    [Fact]
    public void RegisterUdtConverter_DisposedService_ThrowsObjectDisposedException()
    {
        // Arrange
        var sut = CreateSut();
        var mockConverter = new Mock<IUdtConverter<TestUdtType>>();
        mockConverter.Setup(c => c.UdtTypeName).Returns("TestUdt");

        sut.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => sut.RegisterUdtConverter(mockConverter.Object));
    }

    [Fact]
    public void RegisterUdtConverter_MultipleConverters_CallsClientForEach()
    {
        // Arrange
        var sut = CreateSut();
        var mockConverter1 = new Mock<IUdtConverter<TestUdtType>>();
        var mockConverter2 = new Mock<IUdtConverter<AnotherTestUdtType>>();
        mockConverter1.Setup(c => c.UdtTypeName).Returns("TestUdt1");
        mockConverter2.Setup(c => c.UdtTypeName).Returns("TestUdt2");

        // Act
        sut.RegisterUdtConverter(mockConverter1.Object);
        sut.RegisterUdtConverter(mockConverter2.Object);

        // Assert
        _mockClient.Verify(c => c.RegisterUdtConverter(mockConverter1.Object), Times.Once);
        _mockClient.Verify(c => c.RegisterUdtConverter(mockConverter2.Object), Times.Once);
    }

    #endregion UDT Converter Tests

    // Test UDT types for unit testing
    public record TestUdtType(bool TestBool, int TestInt, string TestString);
    public record AnotherTestUdtType(double TestDouble, DateTime TestDateTime);

    private static ApplicationConfiguration CreateTestAppConfig()
    {
        return new ApplicationConfiguration
        {
            ApplicationName = _appName,
            ApplicationUri = _appUri,
            ProductUri = _productUri,
            SecurityConfiguration = new SecurityConfiguration(new SecurityConfigurationStores()),
            ClientConfiguration = new ClientConfiguration { SessionTimeout = 60000 },
            TransportQuotas = new TransportQuotas { OperationTimeout = 60000 }
        };
    }
}