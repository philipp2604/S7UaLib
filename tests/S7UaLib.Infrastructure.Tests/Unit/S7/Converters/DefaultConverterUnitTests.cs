using S7UaLib.Infrastructure.S7.Converters;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class DefaultConverterUnitTests
{
    private static DefaultConverter CreateSut(Type targetType)
    {
        return new DefaultConverter(targetType);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullTargetType_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new DefaultConverter(null!));
    }

    [Fact]
    public void Constructor_WithValidTargetType_SetsTargetTypeProperty()
    {
        // Arrange
        var targetType = typeof(int);

        // Act
        var sut = new DefaultConverter(targetType);

        // Assert
        Assert.Equal(targetType, sut.TargetType);
    }

    #endregion Constructor Tests

    #region ConvertFromOpc Tests

    [Fact]
    public void ConvertFromOpc_WithNonNullValue_ReturnsSameValue()
    {
        // Arrange
        var sut = CreateSut(typeof(string));
        const string originalValue = "test string";

        // Act
        var result = sut.ConvertFromOpc(originalValue);

        // Assert
        Assert.Equal(originalValue, result);
    }

    [Fact]
    public void ConvertFromOpc_WithNullValue_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut(typeof(object));

        // Act
        var result = sut.ConvertFromOpc(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFromOpc_WithValueType_ReturnsSameValue()
    {
        // Arrange
        var sut = CreateSut(typeof(int));
        const int originalValue = 123;

        // Act
        var result = sut.ConvertFromOpc(originalValue);

        // Assert
        Assert.Equal(originalValue, result);
    }

    #endregion ConvertFromOpc Tests

    #region ConvertToOpc Tests

    [Fact]
    public void ConvertToOpc_WithNonNullValue_ReturnsSameValue()
    {
        // Arrange
        var sut = CreateSut(typeof(string));
        const string originalValue = "another test string";

        // Act
        var result = sut.ConvertToOpc(originalValue);

        // Assert
        Assert.Equal(originalValue, result);
    }

    [Fact]
    public void ConvertToOpc_WithNullValue_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut(typeof(object));

        // Act
        var result = sut.ConvertToOpc(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WithValueType_ReturnsSameValue()
    {
        // Arrange
        var sut = CreateSut(typeof(DateTime));
        var originalValue = DateTime.UtcNow;

        // Act
        var result = sut.ConvertToOpc(originalValue);

        // Assert
        Assert.Equal(originalValue, result);
    }

    #endregion ConvertToOpc Tests
}