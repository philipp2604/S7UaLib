using Opc.Ua;
using S7UaLib.Serialization.Json;
using S7UaLib.Serialization.Json.Converters;
using System.Text.Json;
using Xunit;

namespace S7UaLib.UnitTests.Serialization.Json.Converters;

[Trait("Category", "Unit")]
public class StatusCodeJsonConverterTests
{
    private readonly JsonSerializerOptions _options;

    public StatusCodeJsonConverterTests()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new StatusCodeJsonConverter());
    }

    #region Write Tests

    [Fact]
    public void Write_WithGoodStatusCode_SerializesCorrectly()
    {
        // Arrange
        var statusCode = new StatusCode(StatusCodes.Good);
        const string expectedJson = """{"Code":0,"Symbol":"Good"}""";

        // Act
        var json = JsonSerializer.Serialize(statusCode, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    [Fact]
    public void Write_WithBadStatusCode_SerializesCorrectly()
    {
        // Arrange
        var statusCode = new StatusCode(StatusCodes.BadWaitingForInitialData);
        // The uint value for BadWaitingForInitialData is 0x80320000 = 2150760448.
        const string expectedJson = """{"Code":2150760448,"Symbol":"BadWaitingForInitialData"}""";

        // Act
        var json = JsonSerializer.Serialize(statusCode, _options);

        // Assert
        Assert.Equal(expectedJson, json);
    }

    #endregion Write Tests

    #region Read Tests

    [Fact]
    public void Read_WithValidJson_DeserializesCorrectly()
    {
        // Arrange
        // The uint value for BadNodeIdUnknown is 0x80340000 = 2150891520.
        const string json = """{"Code":2150891520,"Symbol":"BadNodeIdUnknown"}""";
        var expectedStatusCode = new StatusCode(StatusCodes.BadNodeIdUnknown);

        // Act
        var result = JsonSerializer.Deserialize<StatusCode>(json, _options);

        // Assert
        Assert.Equal(expectedStatusCode, result);
    }

    [Fact]
    public void Read_WithCaseInsensitiveCodeProperty_DeserializesCorrectly()
    {
        // Arrange
        const string json = """{"code":0,"symbol":"Good"}"""; // "code" is lowercase
        var expectedStatusCode = new StatusCode(StatusCodes.Good);

        // Act
        var result = JsonSerializer.Deserialize<StatusCode>(json, _options);

        // Assert
        Assert.Equal(expectedStatusCode, result);
    }

    [Fact]
    public void Read_WithExtraProperties_IgnoresThemAndDeserializesCorrectly()
    {
        // Arrange
        const string json = """{"Code":0,"Symbol":"Good","ExtraInfo":"This should be ignored"}""";
        var expectedStatusCode = new StatusCode(StatusCodes.Good);

        // Act
        var result = JsonSerializer.Deserialize<StatusCode>(json, _options);

        // Assert
        Assert.Equal(expectedStatusCode, result);
    }

    [Fact]
    public void Read_WithMissingCodeProperty_ReturnsDefaultStatusCode()
    {
        // Arrange
        const string json = """{"Symbol":"SomeStatus"}""";
        var expectedStatusCode = new StatusCode(0); // Default uint is 0, which is StatusCodes.Good

        // Act
        var result = JsonSerializer.Deserialize<StatusCode>(json, _options);

        // Assert
        Assert.Equal(expectedStatusCode, result);
    }

    [Fact]
    public void Read_WithInvalidJsonFormat_ThrowsJsonException()
    {
        // Arrange
        const string json = """[0, "Good"]"""; // Not a JSON object

        // Act & Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<StatusCode>(json, _options));
    }

    #endregion Read Tests
}