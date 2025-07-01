using Opc.Ua;
using S7UaLib.Serialization.Json;
using S7UaLib.Serialization.Json.Converters;
using System.Text.Json;
using Xunit;

namespace S7UaLib.UnitTests.Serialization.Json.Converters;

[Trait("Category", "Unit")]
public class NodeIdJsonConverterTests
{
    private readonly JsonSerializerOptions _options;

    public NodeIdJsonConverterTests()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new NodeIdJsonConverter());
    }

    #region Write Tests

    [Fact]
    public void Write_WithStringIdentifier_SerializesCorrectly()
    {
        // Arrange
        var nodeId = new NodeId("MyNode", 3);
        const string expectedJson = "\"ns=3;s=MyNode\"";

        // Act
        var json = JsonSerializer.Serialize(nodeId, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    [Fact]
    public void Write_WithNumericIdentifier_SerializesCorrectly()
    {
        // Arrange
        var nodeId = new NodeId(1234, 2);
        const string expectedJson = "\"ns=2;i=1234\"";

        // Act
        var json = JsonSerializer.Serialize(nodeId, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    [Fact]
    public void Write_WithNullNodeId_SerializesToNull()
    {
        // Arrange
        NodeId? nodeId = null;
        const string expectedJson = "null";

        // Act
        var json = JsonSerializer.Serialize(nodeId, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    #endregion Write Tests

    #region Read Tests

    [Fact]
    public void Read_WithValidStringIdentifier_DeserializesCorrectly()
    {
        // Arrange
        const string json = "\"ns=3;s=MyNode\"";
        var expectedNodeId = new NodeId("MyNode", 3);

        // Act
        var result = JsonSerializer.Deserialize<NodeId>(json, _options);

        // Assert
        Assert.Equal(expectedNodeId, result);
    }

    [Fact]
    public void Read_WithValidNumericIdentifier_DeserializesCorrectly()
    {
        // Arrange
        const string json = "\"ns=2;i=1234\"";
        var expectedNodeId = new NodeId(1234, 2);

        // Act
        var result = JsonSerializer.Deserialize<NodeId>(json, _options);

        // Assert
        Assert.Equal(expectedNodeId, result);
    }

    [Fact]
    public void Read_WithNullJson_DeserializesToNull()
    {
        // Arrange
        const string json = "null";

        // Act
        var result = JsonSerializer.Deserialize<NodeId>(json, _options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Read_WithEmptyString_DeserializesToNull()
    {
        // Arrange
        const string json = "\"\"";

        // Act
        var result = JsonSerializer.Deserialize<NodeId>(json, _options);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void Read_WithInvalidFormatMissingNamespace_ThrowsArgumentException()
    {
        // Arrange
        const string json = "\"this-is-not-a-nodeid\"";

        // Act & Assert
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<NodeId>(json, _options));
    }

    [Fact]
    public void Read_WithMalformedNamespace_ThrowsServiceResultException()
    {
        // Arrange
        const string json = "\"ns=abc;s=MyNode\"";

        // Act & Assert
        var ex = Assert.Throws<ServiceResultException>(() => JsonSerializer.Deserialize<NodeId>(json, _options));
        Assert.IsType<FormatException>(ex.InnerException);
    }

    #endregion Read Tests
}