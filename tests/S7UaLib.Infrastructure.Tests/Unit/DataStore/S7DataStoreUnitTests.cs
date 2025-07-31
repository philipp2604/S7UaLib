using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.Core.Enums;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Infrastructure.DataStore;

namespace S7UaLib.Infrastructure.Tests.Unit.DataStore;

[Trait("Category", "Unit")]
public class S7DataStoreUnitTests
{
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<IS7DataStore>> _mockLogger;

    public S7DataStoreUnitTests()
    {
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<IS7DataStore>>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);
    }

    private S7DataStore CreateSut()
    {
        return new S7DataStore(_mockLoggerFactory.Object);
    }

    #region SetStructure Tests

    [Fact]
    public void SetStructure_WithNulls_CorrectlyResetsAndInitializesAreas()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1" }],
            [new S7DataBlockInstance { DisplayName = "FB_DB1" }],
            new S7Inputs { Variables = [new S7Variable { DisplayName = "I0.0" }] },
            null, null, null, null);
        sut.BuildCache();

        Assert.NotEmpty(sut.DataBlocksGlobal);
        Assert.NotEmpty(sut.Inputs.Variables);

        // Act
        sut.SetStructure([], [], null, null, null, null, null);
        sut.BuildCache();

        // Assert
        Assert.Empty(sut.DataBlocksGlobal);
        Assert.Empty(sut.DataBlocksInstance);

        Assert.NotNull(sut.Inputs);
        Assert.Equal("Inputs", sut.Inputs.DisplayName);
        Assert.Empty(sut.Inputs.Variables);

        Assert.NotNull(sut.Outputs);
        Assert.Equal("Outputs", sut.Outputs.DisplayName);
        Assert.Empty(sut.Outputs.Variables);
    }

    #endregion SetStructure Tests

    #region BuildCache Tests

    [Fact]
    public void BuildCache_WithPopulatedStore_CorrectlyBuildsVariablePathCache()
    {
        // Arrange
        var sut = CreateSut();
        var var1 = new S7Variable { DisplayName = "VarBool" };
        var var2 = new S7Variable { DisplayName = "VarInt" };
        var structMember = new S7Variable { DisplayName = "Member1" };
        var structVar = new S7Variable { DisplayName = "MyStruct", StructMembers = [structMember] };

        sut.SetStructure(
            [
                new S7DataBlockGlobal
                {
                    DisplayName = "DB1",
                    Variables = [var1, var2]
                }
            ],
            [],
                new S7Inputs
                {
                    DisplayName = "Inputs",
                    Variables = [structVar]
                }
                ,
            null, null, null, null);

        // Act
        sut.BuildCache();

        // Assert
        Assert.True(sut.TryGetVariableByPath("DataBlocksGlobal.DB1.VarBool", out var foundVar1));
        Assert.Same(var1, foundVar1);

        Assert.True(sut.TryGetVariableByPath("DataBlocksGlobal.DB1.VarInt", out var foundVar2));
        Assert.Same(var2, foundVar2);

        Assert.True(sut.TryGetVariableByPath("Inputs.MyStruct", out var foundStruct));
        Assert.Same(structVar, foundStruct);

        structMember = structMember with { FullPath = "Inputs.MyStruct.Member1" };
        structVar = structVar with { StructMembers = [structMember] };
        sut.SetStructure(sut.DataBlocksGlobal, [], (S7Inputs)sut?.Inputs! with { Variables = [structVar] }, null, null, null, null);

        sut.BuildCache();

        Assert.True(sut.TryGetVariableByPath("Inputs.MyStruct.Member1", out var foundMember));
        Assert.Same(structMember, foundMember);
    }

    [Fact]
    public void BuildCache_WithPathFromElement_UsesFullPathProperty()
    {
        // Arrange
        var sut = CreateSut();
        var varWithFullPath = new S7Variable { DisplayName = "Var", FullPath = "Some.Explicit.Path.Var" };

        sut.SetStructure([], [], null, null,
            new S7Memory
            {
                DisplayName = "Memory",
                FullPath = "Some.Explicit.Path",
                Variables = [varWithFullPath]
            },
            null, null);

        // Act
        sut.BuildCache();

        // Assert
        Assert.True(sut.TryGetVariableByPath("Some.Explicit.Path.Var", out var foundVar));
        Assert.Same(varWithFullPath, foundVar);
        Assert.False(sut.TryGetVariableByPath("Memory.Var", out _));
    }

    [Fact]
    public void BuildCache_WhenCalledTwice_ClearsOldCacheBeforeRebuilding()
    {
        // Arrange
        var sut = CreateSut();

        sut.SetStructure(
            [
                new S7DataBlockGlobal
                {
                    DisplayName = "DB_TO_REMOVE",
                    Variables = [new S7Variable { DisplayName = "OldVar" }]
                }
            ],
            [],
            null, null, null, null, null);

        sut.BuildCache();
        Assert.True(sut.TryGetVariableByPath("DataBlocksGlobal.DB_TO_REMOVE.OldVar", out _));

        sut.SetStructure(
            [
                new S7DataBlockGlobal
            {
                DisplayName = "DB_NEW",
                Variables = [ new S7Variable { DisplayName = "NewVar" } ]
            }
            ],
            [],
            null, null, null, null, null);

        // Act
        sut.BuildCache();

        // Assert
        Assert.False(sut.TryGetVariableByPath("DataBlocksGlobal.DB_TO_REMOVE.OldVar", out _));
        Assert.True(sut.TryGetVariableByPath("DataBlocksGlobal.DB_NEW.NewVar", out _));
    }

    #endregion BuildCache Tests

    #region Getter Tests

    [Fact]
    public void TryGetVariableByPath_WithNonExistentPath_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        sut.BuildCache();

        // Act
        var result = sut.TryGetVariableByPath("Non.Existent.Path", out var variable);

        // Assert
        Assert.False(result);
        Assert.Null(variable);
    }

    [Fact]
    public void GetAllVariables_AfterBuildCache_ReturnsCorrectDictionary()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure(
            [
                new S7DataBlockGlobal
                {
                    DisplayName = "DB1",
                    Variables = [ new S7Variable { DisplayName = "Var1" }]
                }
            ],
            [],
            null, null, null, null, null);

        sut.BuildCache();

        // Act
        var allVars = sut.GetAllVariables();

        // Assert
        Assert.Single(allVars);
        Assert.True(allVars.ContainsKey("DataBlocksGlobal.DB1.Var1"));
    }

    [Fact]
    public void FindVariables_AfterBuildCache_ReturnsCorrectList()
    {
        // Arrange
        const string firstVar = "ThisVarShouldBeFound";
        const string secondVar = "ThisVarShouldBeFoundToo";
        const string thirdVar = "ButThisOneShouldNot";

        var sut = CreateSut();
        sut.SetStructure(
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

        sut.BuildCache();

        // Act
        var foundVars = sut.FindVariablesWhere(x => x.DisplayName!.Contains("Found"));
        var notFoundVars = sut.FindVariablesWhere(x => x.S7Type == S7DataType.TIME);

        // Assert
        Assert.Collection(foundVars,
            x => Assert.Equal(firstVar, x.DisplayName),
            x => Assert.Equal(secondVar, x.DisplayName));
        Assert.Empty(notFoundVars);
    }

    #endregion Getter Tests

    #region RegisterVariable Tests

    [Fact]
    public void RegisterVariable_WithoutNodeIdInGlobalDb_AssignsCorrectNodeId()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1" }], [], null, null, null, null, null);
        sut.BuildCache();

        const string path = "DataBlocksGlobal.DB1.Temp";
        var newVar = new S7Variable { DisplayName = "Temp", FullPath = path }; // No NodeId provided

        // Act
        sut.RegisterVariable(newVar);

        // Assert
        Assert.True(sut.TryGetVariableByPath(path, out var addedVar));
        Assert.NotNull(addedVar);
        Assert.Equal("ns=3;s=DB1.Temp", addedVar.NodeId);
        Assert.Equal(path, addedVar.FullPath);
    }

    [Fact]
    public void RegisterVariable_WithoutNodeIdInInputs_AssignsCorrectNodeId()
    {
        // Arrange
        var sut = CreateSut();
        sut.BuildCache();

        const string path = "Inputs.NewInput";
        var newVar = new S7Variable { DisplayName = "NewInput", FullPath = path }; // No NodeId

        // Act
        sut.RegisterVariable(newVar);

        // Assert
        Assert.True(sut.TryGetVariableByPath(path, out var addedVar));
        Assert.NotNull(addedVar);
        Assert.Equal("ns=3;s=Inputs.NewInput", addedVar.NodeId);
        Assert.Equal(path, addedVar.FullPath);
    }

    [Fact]
    public void RegisterVariable_WithStructWithoutNodeIds_AssignsNodeIdsRecursively()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure([new S7DataBlockGlobal { DisplayName = "Settings", FullPath = "DataBlocksGlobal.Settings" }], [], null, null, null, null, null);
        sut.BuildCache();

        const string structPath = "DataBlocksGlobal.Settings.Motor";
        const string memberPath = "DataBlocksGlobal.Settings.Motor.Speed";

        var memberVar = new S7Variable { DisplayName = "Speed", FullPath = memberPath }; // No NodeId
        var structVar = new S7Variable { DisplayName = "Motor", FullPath = structPath, S7Type = S7DataType.STRUCT, StructMembers = [memberVar] }; // No NodeId

        // Act
        sut.RegisterVariable(structVar);

        // Assert
        Assert.True(sut.TryGetVariableByPath(structPath, out var addedStruct));
        Assert.NotNull(addedStruct);
        Assert.Equal("ns=3;s=Settings.Motor", addedStruct.NodeId);
        Assert.Equal(structPath, addedStruct.FullPath);

        Assert.True(sut.TryGetVariableByPath(memberPath, out var addedMember));
        Assert.NotNull(addedMember);
        Assert.Equal("ns=3;s=Settings.Motor.Speed", addedMember.NodeId);
        Assert.Equal(memberPath, addedMember.FullPath);
    }

    [Fact]
    public void RegisterVariable_WithoutNodeIdInInstanceDb_DoesNotAssignNodeId()
    {
        // Arrange
        var sut = CreateSut();
        var staticSection = new S7InstanceDbSection { DisplayName = "Static", FullPath = "DataBlocksInstance.MyFB_DB.Static" };
        sut.SetStructure([], [new S7DataBlockInstance { DisplayName = "MyFB_DB", FullPath = "DataBlocksInstance.MyFB_DB", Static = staticSection }], null, null, null, null, null);
        sut.BuildCache();

        const string path = "DataBlocksInstance.MyFB_DB.Static.InternalValue";
        var newVar = new S7Variable { DisplayName = "InternalValue", FullPath = path }; // No NodeId

        // Act
        sut.RegisterVariable(newVar);

        // Assert
        Assert.True(sut.TryGetVariableByPath(path, out var addedVar));
        Assert.NotNull(addedVar);
        Assert.True(string.IsNullOrEmpty(addedVar.NodeId), "NodeId should not be auto-generated for instance DBs.");
    }

    [Fact]
    public void RegisterVariable_WithExistingNodeId_KeepsUserProvidedNodeId()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure([new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1" }], [], null, null, null, null, null);
        sut.BuildCache();

        const string userNodeId = "My.Custom.NodeId";
        const string path = "DataBlocksGlobal.DB1.Temp";
        var newVar = new S7Variable { DisplayName = "Temp", FullPath = path, NodeId = userNodeId };

        // Act
        sut.RegisterVariable(newVar);

        // Assert
        Assert.True(sut.TryGetVariableByPath(path, out var addedVar));
        Assert.NotNull(addedVar);
        Assert.Equal(userNodeId, addedVar.NodeId);
    }

    [Fact]
    public void RegisterVariable_ToGlobalDb_SuccessfullyAddsVariableAndRebuildsCache()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1" }],
            [], null, null, null, null, null);
        sut.BuildCache();

        const string path = "DataBlocksGlobal.DB1.NewVar";
        var newVar = new S7Variable { DisplayName = "NewVar", FullPath = path, Value = true };

        // Act
        var success = sut.RegisterVariable(newVar);

        // Assert
        Assert.True(success);
        Assert.Single(sut.DataBlocksGlobal[0].Variables, v => v.DisplayName == "NewVar");
        Assert.True(sut.TryGetVariableByPath(path, out var updatedVar));
        Assert.NotNull(updatedVar);
        Assert.True((bool)updatedVar.Value!);
        Assert.Equal("DataBlocksGlobal.DB1.NewVar", updatedVar.FullPath);
        Assert.Equal("ns=3;s=DB1.NewVar", updatedVar.NodeId);
    }

    [Fact]
    public void RegisterVariable_ToIoArea_SuccessfullyAddsVariable()
    {
        // Arrange
        var sut = CreateSut();
        sut.BuildCache();

        const string path = "Inputs.NewInput";
        var newVar = new S7Variable { DisplayName = "NewInput", FullPath = path, Value = true };

        // Act
        var success = sut.RegisterVariable(newVar);

        // Assert
        Assert.True(success);
        Assert.NotNull(sut.Inputs);
        Assert.Single(sut.Inputs.Variables, v => v.DisplayName == "NewInput" && v.NodeId == "ns=3;s=Inputs.NewInput");
        Assert.True(sut.TryGetVariableByPath(path, out _));
    }

    [Fact]
    public void RegisterVariable_ToExistingStruct_SuccessfullyAddsMember()
    {
        // Arrange
        var sut = CreateSut();
        var structVar = new S7Variable { DisplayName = "MyStruct", FullPath = "DataBlocksGlobal.DB1.MyStruct", S7Type = S7DataType.STRUCT };
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1", Variables = [structVar] }],
            [], null, null, null, null, null);
        sut.BuildCache();

        const string path = "DataBlocksGlobal.DB1.MyStruct.NewMember";
        var newMember = new S7Variable { DisplayName = "NewMember", Value = 42, FullPath = path };

        // Act
        var success = sut.RegisterVariable(newMember);

        // Assert
        Assert.True(success);
        var updatedStruct = (S7Variable)sut.DataBlocksGlobal[0].Variables[0];
        Assert.Single(updatedStruct.StructMembers, m => m.DisplayName == "NewMember");
        Assert.True(sut.TryGetVariableByPath(path, out var cachedMember));
        Assert.Equal(42, cachedMember?.Value);
    }

    [Fact]
    public void RegisterVariable_NewStructWithMembers_SuccessfullyAddsStructAndAllMembersToCache()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1" }],
            [], null, null, null, null, null);
        sut.BuildCache();

        const string structPath = "DataBlocksGlobal.DB1.NewStruct";
        const string memberAPath = "DataBlocksGlobal.DB1.NewStruct.MemberA";
        const string memberBPath = "DataBlocksGlobal.DB1.NewStruct.MemberB";

        var member1 = new S7Variable { DisplayName = "MemberA", FullPath = memberAPath, Value = 10 };
        var member2 = new S7Variable { DisplayName = "MemberB", FullPath = memberBPath, Value = 20 };
        var newStruct = new S7Variable { DisplayName = "NewStruct", FullPath = structPath, S7Type = S7DataType.STRUCT, StructMembers = [member1, member2] };

        // Act
        var success = sut.RegisterVariable(newStruct);

        // Assert
        Assert.True(success);
        // Check if struct is in the hierarchy
        Assert.Single(sut.DataBlocksGlobal[0].Variables, v => v.DisplayName == "NewStruct");
        // Check if struct and all members are in the cache
        Assert.True(sut.TryGetVariableByPath(structPath, out _));
        Assert.True(sut.TryGetVariableByPath(memberAPath, out var cachedMemberA));
        Assert.True(sut.TryGetVariableByPath(memberBPath, out var cachedMemberB));
        Assert.Equal(10, cachedMemberA?.Value);
        Assert.Equal(20, cachedMemberB?.Value);
    }

    [Fact]
    public void RegisterVariable_ToNestedStruct_SuccessfullyAddsVariable()
    {
        // Arrange
        var sut = CreateSut();
        var level1Struct = new S7Variable { DisplayName = "Level1", FullPath = "DataBlocksGlobal.DB1.Level1", S7Type = S7DataType.STRUCT };
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1", Variables = [level1Struct] }],
            [], null, null, null, null, null);
        sut.BuildCache();

        const string path = "DataBlocksGlobal.DB1.Level1.DeepVar";
        var newVar = new S7Variable { DisplayName = "DeepVar", FullPath = path, Value = "test" };

        // Act
        var success = sut.RegisterVariable(newVar);

        // Assert
        Assert.True(success);
        var updatedL1Struct = (S7Variable)sut.DataBlocksGlobal[0].Variables[0];
        Assert.Single(updatedL1Struct.StructMembers, m => m.DisplayName == "DeepVar");
        Assert.True(sut.TryGetVariableByPath(path, out _));
    }

    [Fact]
    public void RegisterVariable_WhenParentPathDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1" }],
            [], null, null, null, null, null);
        sut.BuildCache();

        const string path = "DataBlocksGlobal.DB_DOES_NOT_EXIST.NewVar";
        var newVar = new S7Variable { DisplayName = "NewVar", FullPath = path };

        // Act
        var success = sut.RegisterVariable(newVar);

        // Assert
        Assert.False(success);
        Assert.Single(sut.DataBlocksGlobal); // Unchanged
        Assert.Empty(sut.DataBlocksGlobal[0].Variables); // Unchanged
        Assert.False(sut.TryGetVariableByPath(path, out _));
    }

    [Fact]
    public void RegisterVariable_WhenVariableAlreadyExists_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var existingVar = new S7Variable { DisplayName = "ExistingVar" };
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1", Variables = [existingVar] }],
            [], null, null, null, null, null);
        sut.BuildCache();

        const string path = "DataBlocksGlobal.DB1.ExistingVar";
        var newVar = new S7Variable { DisplayName = "ExistingVar", FullPath = path };

        // Act
        var success = sut.RegisterVariable(newVar);

        // Assert
        Assert.False(success);
    }

    #endregion RegisterVariable Tests

    #region RegisterGlobalDataBlock Tests

    [Fact]
    public void RegisterGlobalDataBlock_WithValidNewDataBlock_AddsToStoreAndRebuildsCache()
    {
        // Arrange
        var sut = CreateSut();
        sut.BuildCache();

        var newDb = new S7DataBlockGlobal
        {
            DisplayName = "DB5",
            FullPath = "DataBlocksGlobal.DB5",
            Variables = [new S7Variable { DisplayName = "MyVar" }]
        };

        // Act
        var success = sut.RegisterGlobalDataBlock(newDb);

        // Assert
        Assert.True(success);
        Assert.Single(sut.DataBlocksGlobal);
        Assert.Equal("DB5", sut.DataBlocksGlobal[0].DisplayName);
        Assert.True(sut.TryGetVariableByPath("DataBlocksGlobal.DB5.MyVar", out var foundVar));
        Assert.NotNull(foundVar);
        Assert.Equal("MyVar", foundVar.DisplayName);
    }

    [Fact]
    public void RegisterGlobalDataBlock_WithExistingPath_ReturnsFalseAndDoesNotModifyStore()
    {
        // Arrange
        var sut = CreateSut();
        var initialDb = new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1" };
        sut.SetStructure([initialDb], [], null, null, null, null, null);
        sut.BuildCache();

        var duplicateDb = new S7DataBlockGlobal { DisplayName = "DB1_DUPLICATE", FullPath = "DataBlocksGlobal.DB1" };

        // Act
        var success = sut.RegisterGlobalDataBlock(duplicateDb);

        // Assert
        Assert.False(success);
        Assert.Single(sut.DataBlocksGlobal);
        Assert.Same(initialDb, sut.DataBlocksGlobal[0]);
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("A data block with path 'DataBlocksGlobal.DB1' already exists.")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void RegisterGlobalDataBlock_WithInvalidPath_WrongRootName_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var invalidDb = new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksInstance.DB1" };

        // Act
        var success = sut.RegisterGlobalDataBlock(invalidDb);

        // Assert
        Assert.False(success);
        Assert.Empty(sut.DataBlocksGlobal);
        _mockLogger.Verify(
           x => x.Log(
               LogLevel.Warning,
               It.IsAny<EventId>(),
               It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("The root name is invalid.")),
               null,
               It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
           Times.Once);
    }

    [Fact]
    public void RegisterGlobalDataBlock_WithInvalidPath_TooFewSegments_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        var invalidDb = new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DB1" };

        // Act
        var success = sut.RegisterGlobalDataBlock(invalidDb);

        // Assert
        Assert.False(success);
        Assert.Empty(sut.DataBlocksGlobal);
        _mockLogger.Verify(
          x => x.Log(
              LogLevel.Warning,
              It.IsAny<EventId>(),
              It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("The path 'DB1' is not valid.")),
              null,
              It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
          Times.Once);
    }

    #endregion RegisterGlobalDataBlock Tests

    #region UpdateVariable Tests

    [Fact]
    public void UpdateVariable_PathCaseInsensitivity_SuccessfullyUpdates()
    {
        // Arrange
        var sut = CreateSut();
        var oldVar = new S7Variable { DisplayName = "MyVar", Value = "Initial" };
        sut.SetStructure([new S7DataBlockGlobal { DisplayName = "Db1", Variables = [oldVar] }], [], null, null, null, null, null);
        sut.BuildCache();

        var newVar = new S7Variable { DisplayName = "MyVar", Value = "Updated" };
        const string path = "DATABLOCKSGLOBAL.db1.MYVAR";

        // Act
        var success = sut.UpdateVariable(path, newVar);

        // Assert
        Assert.True(success, "Update should succeed with a case-insensitive path.");
        Assert.Equal("Updated", sut.DataBlocksGlobal[0].Variables[0].Value);
        Assert.True(sut.TryGetVariableByPath("DataBlocksGlobal.Db1.MyVar", out var updatedVar));
        Assert.Equal("Updated", updatedVar?.Value);
    }

    [Fact]
    public void UpdateVariable_InGlobalDb_SuccessfullyReplacesVariable()
    {
        // Arrange
        var sut = CreateSut();
        var oldVar = new S7Variable { DisplayName = "VarToReplace", Value = 1 };
        sut.SetStructure(
            [
                new S7DataBlockGlobal
                {
                    DisplayName = "DB1",
                    Variables = [ oldVar ]
                }
            ],
            [],
            null, null, null, null, null);

        sut.BuildCache();

        var newVar = new S7Variable { DisplayName = "VarToReplace", Value = 99 };
        const string path = "DataBlocksGlobal.DB1.VarToReplace";

        // Act
        var success = sut.UpdateVariable(path, newVar);

        // Assert
        Assert.True(success);
        Assert.Equal(99, sut.DataBlocksGlobal[0].Variables[0].Value);
        Assert.True(sut.TryGetVariableByPath(path, out var updatedVar));
        Assert.Equal(99, updatedVar?.Value);
    }

    [Fact]
    public void UpdateVariable_InIoArea_SuccessfullyReplacesVariable()
    {
        // Arrange
        var sut = CreateSut();
        var oldVar = new S7Variable { DisplayName = "TestInput", Value = false };
        sut.SetStructure(
            [], [],
            new S7Inputs
            {
                DisplayName = "Inputs",
                Variables = [oldVar]
            },
            null, null, null, null);

        sut.BuildCache();

        var newVar = new S7Variable { DisplayName = "TestInput", Value = true };
        const string path = "Inputs.TestInput";

        // Act
        var success = sut.UpdateVariable(path, newVar);

        // Assert
        Assert.True(success);
        Assert.NotNull(sut.Inputs);
        Assert.True((bool)sut.Inputs.Variables[0].Value!);
        Assert.True(sut.TryGetVariableByPath(path, out var updatedVar));
        Assert.True((bool)updatedVar?.Value!);
    }

    [Fact]
    public void UpdateVariable_InNestedStruct_SuccessfullyReplacesVariable()
    {
        // Arrange
        var sut = CreateSut();
        var oldMember = new S7Variable { DisplayName = "Member", Value = 123 };
        var structVar = new S7Variable { DisplayName = "MyStruct", S7Type = S7DataType.STRUCT, StructMembers = [oldMember] };
        sut.SetStructure(
            [
                new S7DataBlockGlobal
                {
                    DisplayName = "DB1",
                    Variables = [ structVar ]
                }
            ],
            [],
            null, null, null, null, null);

        sut.BuildCache();

        var newMember = new S7Variable { DisplayName = "Member", Value = 456 };
        const string path = "DataBlocksGlobal.DB1.MyStruct.Member";

        // Act
        var success = sut.UpdateVariable(path, newMember);

        // Assert
        Assert.True(success);
        var updatedStruct = (S7Variable)sut.DataBlocksGlobal[0].Variables[0];
        Assert.Equal(456, updatedStruct.StructMembers[0].Value);
        Assert.True(sut.TryGetVariableByPath(path, out var updatedVar));
        Assert.Equal(456, updatedVar?.Value);
    }

    [Fact]
    public void UpdateVariable_WithNonExistentPath_ReturnsFalse()
    {
        // Arrange
        var sut = CreateSut();
        sut.BuildCache();
        var newVar = new S7Variable { DisplayName = "Var" };

        // Act
        var success = sut.UpdateVariable("Some.Fake.Path", newVar);

        // Assert
        Assert.False(success);
    }

    #endregion UpdateVariable Tests

    #region Thread-Safety Tests

    [Fact(Timeout = 5000)] // Timeout to detect deadlocks
    public async Task RegisterVariable_WhenCalledConcurrently_MaintainsDataIntegrityAndAvoidsRaceConditions()
    {
        // Arrange
        var sut = CreateSut();
        sut.SetStructure(
            [new S7DataBlockGlobal { DisplayName = "DB1", FullPath = "DataBlocksGlobal.DB1" }],
            [], null, null, null, null, null);
        sut.BuildCache();

        const int numberOfConcurrentAdds = 100;
        var tasks = new List<Task>();

        // Act
        // Starting 100 tasks that all try to add a variable concurrently.
        for (int i = 0; i < numberOfConcurrentAdds; i++)
        {
            var taskIndex = i; // Capture variable for closure
            var newVar = new S7Variable
            {
                DisplayName = $"ConcurrentVar_{taskIndex}",
                FullPath = $"DataBlocksGlobal.DB1.ConcurrentVar_{taskIndex}"
            };

            tasks.Add(Task.Run(() => sut.RegisterVariable(newVar)));
        }

        // Wait until all tasks complete
        await Task.WhenAll(tasks);

        // Assert
        // 1. Check for the expected amount of variables in the DataBlocksGlobal section.
        //    Since we added 100 variables, we expect exactly 100 variables in the DB1 section
        //    otherwise a race condition or deadlock might have occurred.
        Assert.NotNull(sut.DataBlocksGlobal);
        Assert.Single(sut.DataBlocksGlobal);
        Assert.Equal(numberOfConcurrentAdds, sut.DataBlocksGlobal[0].Variables.Count);

        // 2. Check the cache too.
        var allVars = sut.GetAllVariables();
        Assert.Equal(numberOfConcurrentAdds, allVars.Count);

        for (int i = 0; i < numberOfConcurrentAdds; i++)
        {
            var expectedPath = $"DataBlocksGlobal.DB1.ConcurrentVar_{i}";
            Assert.True(sut.TryGetVariableByPath(expectedPath, out var foundVar), $"Variable '{expectedPath}' should be in the cache.");
            Assert.NotNull(foundVar);
            Assert.Equal($"ConcurrentVar_{i}", foundVar.DisplayName);
        }
    }

    #endregion Thread-Safety Tests
}