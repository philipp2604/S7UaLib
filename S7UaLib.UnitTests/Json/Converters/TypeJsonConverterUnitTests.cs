using S7UaLib.Json.Converters;
using System.Text.Json;

namespace S7UaLib.UnitTests.Json.Converters;

[Trait("Category", "Unit")]
public class TypeJsonConverterTests
{
    private readonly JsonSerializerOptions _options;

    public TypeJsonConverterTests()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new TypeJsonConverter());
    }

    #region Write Tests

    [Fact]
    public void Write_WithStandardType_SerializesToAssemblyQualifiedName()
    {
        // Arrange
        var type = typeof(string);
        var expectedJson = $"\"{type.AssemblyQualifiedName}\"";

        // Act
        var json = JsonSerializer.Serialize(type, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    [Fact]
    public void Write_WithCustomType_SerializesToAssemblyQualifiedName()
    {
        // Arrange
        var type = typeof(TypeJsonConverter);
        var expectedJson = $"\"{type.AssemblyQualifiedName}\"";

        // Act
        var json = JsonSerializer.Serialize(type, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    [Fact]
    public void Write_WithNullType_SerializesToNull()
    {
        // Arrange
        Type? type = null;
        var expectedJson = "null";

        // Act
        var json = JsonSerializer.Serialize(type, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    #endregion Write Tests

    #region Read Tests

    [Fact]
    public void Read_WithValidAssemblyQualifiedName_DeserializesToType()
    {
        // Arrange
        var expectedType = typeof(int);
        var json = $"\"{expectedType.AssemblyQualifiedName}\"";

        // Act
        var result = JsonSerializer.Deserialize<Type>(json, _options);

        // Assert
        Assert.Equal(expectedType, result);
    }

    [Fact]
    public void Read_WithNullJson_DeserializesToNull()
    {
        // Arrange
        var json = "null";

        // Act
        var result = JsonSerializer.Deserialize<Type>(json, _options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Read_WithEmptyString_DeserializesToNull()
    {
        // Arrange
        var json = "\"\"";

        // Act
        var result = JsonSerializer.Deserialize<Type>(json, _options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Read_WithInvalidTypeName_DeserializesToNull()
    {
        // Arrange
        var json = "\"Non.Existent.Type, Non.Existent.Assembly\"";

        // Act
        var result = JsonSerializer.Deserialize<Type>(json, _options);

        // Assert
        Assert.Null(result);
    }

    #endregion Read Tests
}