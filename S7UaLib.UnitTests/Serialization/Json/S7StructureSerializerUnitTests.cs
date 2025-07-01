using S7UaLib.Serialization.Json;
using S7UaLib.S7.Structure;
using S7UaLib.UA;
using System.Text.Json;
using System.Text.Json.Serialization;
using S7UaLib.Serialization.Json.Converters;

namespace S7UaLib.UnitTests.Serialization.Json;

[Trait("Category", "Unit")]
public class S7StructureSerializerTests
{
    #region Configuration Tests

    [Fact]
    public void Options_IsNotNull_AfterInitialization()
    {
        // Arrange & Act
        var options = S7StructureSerializer.Options;

        // Assert
        Assert.NotNull(options);
    }

    [Fact]
    public void Options_AreInitialized_WithCorrectBasicSettings()
    {
        // Arrange & Act
        var options = S7StructureSerializer.Options;

        // Assert
        Assert.True(options.WriteIndented, "JSON should be indented for readability.");
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, options.DefaultIgnoreCondition);
    }

    [Fact]
    public void Options_Contains_RequiredCustomConverters()
    {
        // Arrange & Act
        var converters = S7StructureSerializer.Options.Converters;

        // Assert
        Assert.Contains(converters, c => c.GetType() == typeof(NodeIdJsonConverter));
        Assert.Contains(converters, c => c.GetType() == typeof(TypeJsonConverter));
    }

    #endregion Configuration Tests

    #region Serialization Behavior Tests

    [Fact]
    public void Options_IgnoresNullProperty_WhenSerializing()
    {
        // Arrange
        var variable = new S7Variable
        {
            DisplayName = "TestVarWithNulls",
            NodeId = null // This property should be omitted due to JsonIgnoreCondition.WhenWritingNull.
        };
        var options = S7StructureSerializer.Options;

        // Act
        var json = JsonSerializer.Serialize(variable, options);

        // Assert
        // Verify the non-null property is present.
        Assert.Contains("\"DisplayName\": \"TestVarWithNulls\"", json);
        // Verify the null property is NOT present.
        Assert.DoesNotContain("\"NodeId\"", json);
        // The 'Value' property is not asserted because it is non-serializable by design ([JsonIgnore]).
    }

    [Fact]
    public void Options_HandlesPolymorphism_ForIUaElementCorrectly()
    {
        // Arrange
        var originalList = new List<IUaElement>
        {
            new S7DataBlockGlobal
            {
                DisplayName = "GlobalDB",
                Variables =
                [
                    new S7Variable { DisplayName = "MyVar" }
                ]
            },
            new S7Inputs
            {
                DisplayName = "PLC_Inputs"
            },
            new S7Timers
            {
                DisplayName = "PLC_Timers"
            }
        };
        var options = S7StructureSerializer.Options;

        // Act
        var json = JsonSerializer.Serialize(originalList, options);
        var deserializedList = JsonSerializer.Deserialize<List<IUaElement>>(json, options);

        // Assert
        Assert.NotNull(deserializedList);
        Assert.Equal(originalList.Count, deserializedList.Count);

        Assert.IsType<S7DataBlockGlobal>(deserializedList[0]);
        Assert.IsType<S7Inputs>(deserializedList[1]);
        Assert.IsType<S7Timers>(deserializedList[2]);

        Assert.Equal("GlobalDB", deserializedList[0].DisplayName);
        var db = Assert.IsType<S7DataBlockGlobal>(deserializedList[0]);
        var variable = Assert.Single(db.Variables);
        Assert.IsType<S7Variable>(variable);
        Assert.Equal("MyVar", variable.DisplayName);
    }

    [Fact]
    public void Options_UsesCustomConverters_DuringSerialization()
    {
        // Arrange
        var variable = new S7Variable
        {
            DisplayName = "VariableWithCustomTypes",
            NodeId = new Opc.Ua.NodeId("MyNode", 3),
            SystemType = typeof(int),
        };
        var options = S7StructureSerializer.Options;

        // Act
        var json = JsonSerializer.Serialize(variable, options);

        // Assert
        Assert.Contains("\"NodeId\": \"ns=3;s=MyNode\"", json);
        Assert.Contains($"\"SystemType\": \"{typeof(int).AssemblyQualifiedName}\"", json);
    }

    #endregion
}