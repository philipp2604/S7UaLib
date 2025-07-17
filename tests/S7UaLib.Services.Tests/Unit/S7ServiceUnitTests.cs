using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Structure;
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
        const string appName = "TestApp";
        const string appUri = "urn:test:app";
        const string productUri = "urn:test:prod";

        var securityConfig = new Core.Ua.Configuration.SecurityConfiguration(new());
        var clientConfig = new Core.Ua.Configuration.ClientConfiguration();
        var transportQuotas = new Core.Ua.Configuration.TransportQuotas();
        var opLimits = new Core.Ua.Configuration.OperationLimits();

        _mockClient.Setup(c => c.ConfigureAsync(
                appName,
                appUri,
                productUri,
                securityConfig,
                clientConfig,
                transportQuotas,
                opLimits))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        await sut.ConfigureAsync(appName, appUri, productUri, securityConfig, clientConfig, transportQuotas, opLimits);

        // Assert
        // Verify that the client's Configure method was called with the exact same parameters.
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
        // Verify that the client's SaveConfiguration method was called with the exact same file path.
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
        // Verify that the client's LoadConfiguration method was called with the exact same file path.
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
        _mockClient.Setup(c => c.ReadValuesOfElementAsync(It.IsAny<IS7DataBlockGlobal>(), "DataBlocksGlobal", It.IsAny<CancellationToken>())).ReturnsAsync(updatedDb);

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
        _mockClient.Setup(c => c.ReadValuesOfElementAsync(It.IsAny<IS7DataBlockGlobal>(), "DataBlocksGlobal", It.IsAny<CancellationToken>())).ReturnsAsync(sameDb);

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
    public void GetInputs_WhenInputsDoNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var inputs = sut.GetInputs();

        // Assert
        Assert.Null(inputs);
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
    public void GetOutputs_WhenOutputsDoNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var outputs = sut.GetOutputs();

        // Assert
        Assert.Null(outputs);
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
    public void GetMemory_WhenMemoryDoesNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var memory = sut.GetMemory();

        // Assert
        Assert.Null(memory);
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
    public void GetTimers_WhenTimersDoNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var memory = sut.GetTimers();

        // Assert
        Assert.Null(memory);
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
    public void GetCounters_WhenCountersDoNotExist_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var memory = sut.GetCounters();

        // Assert
        Assert.Null(memory);
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
                Assert.Collection(x.Variables, y => Assert.Equal("DataBlocksGlobal.DB1.TestVar", y.FullPath));
            },
            x =>
            {
                Assert.Equal("DB2", x.DisplayName);
                Assert.Collection(x.Variables, y => Assert.Equal("DataBlocksGlobal.DB2.TestVar2", y.FullPath));
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
                Assert.Collection(x.Static.Variables, y => Assert.Equal("DataBlocksInstance.IDB1.TestVar", y.FullPath));
            },
            x =>
            {
                Assert.Equal("IDB2", x.DisplayName);
                Assert.NotNull(x.Static);
                Assert.Collection(x.Static.Variables, y => Assert.Equal("DataBlocksInstance.IDB2.TestVar2", y.FullPath));
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

    #endregion UpdateVariableType Tests

    #region WriteVariableAsync Tests

    [Fact]
    public async Task WriteVariableAsync_WhenPathIsValid_CallsClientWrite()
    {
        // Arrange
        var sut = CreateSut();
        var nodeId = new NodeId("ns=3;s=Test");
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
        var nodeId = new NodeId("ns=3;s=Test");
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
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = new NodeId(1).ToString(), IsSubscribed = false, SamplingInterval = 500 };
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

        var varToSub1 = new S7Variable { DisplayName = "Var1", NodeId = new NodeId(1).ToString(), IsSubscribed = true, SamplingInterval = 100, FullPath = "DB.Var1" };
        var varToSub2 = new S7Variable { DisplayName = "Var2", NodeId = new NodeId(2).ToString(), IsSubscribed = true, SamplingInterval = 200, FullPath = "DB.Var2" };
        var varNotToSub = new S7Variable { DisplayName = "Var3", NodeId = new NodeId(3).ToString(), IsSubscribed = false, FullPath = "DB.Var3" };

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

        var varToSub1 = new S7Variable { DisplayName = "Var1", NodeId = new NodeId(1).ToString(), IsSubscribed = true, FullPath = "DB.Var1" };
        var varToFail = new S7Variable { DisplayName = "Var2", NodeId = new NodeId(2).ToString(), IsSubscribed = true, FullPath = "DB.Var2" };

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
    public async Task UnsubscribeFromVariableAsync_WhenPathIsValid_CallsClientAndUpdatesStore()
    {
        // Arrange
        var sut = CreateSut();
        const string path = "DataBlocksGlobal.DB1.TestVar";
        var variable = new S7Variable { DisplayName = "TestVar", NodeId = new NodeId(1).ToString(), IsSubscribed = true };
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
        var nodeId = new NodeId(123, 2);
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

        var monitoredItem = new MonitoredItem { StartNodeId = nodeId, DisplayName = "TestVar" };
        var notification = new MonitoredItemNotification { Value = new DataValue(new Variant((short)200)) };
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
        var nodeId = new NodeId(123, 2);
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

        var monitoredItem = new MonitoredItem { StartNodeId = nodeId };
        var notification = new MonitoredItemNotification { Value = new DataValue(new Variant(sameValue)) };
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