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

    #region UpdateVariable Tests

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
}