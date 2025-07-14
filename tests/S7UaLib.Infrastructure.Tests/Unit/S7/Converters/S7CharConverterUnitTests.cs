using S7UaLib.Infrastructure.S7.Converters;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class S7CharConverterUnitTests
{
    private static S7CharConverter CreateSut()
    {
        return new S7CharConverter();
    }

    #region Property Tests

    [Fact]
    public void TargetType_ReturnsCharType()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Equal(typeof(char), sut.TargetType);
    }

    #endregion Property Tests

    #region ConvertFromOpc Tests

    [Fact]
    public void ConvertFromOpc_WhenValueIsNull_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertFromOpc(null);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData((byte)65, 'A')]
    [InlineData((byte)48, '0')]
    [InlineData((byte)0, '\0')]
    public void ConvertFromOpc_WhenValueIsByte_ReturnsCorrectChar(byte byteValue, char expectedChar)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertFromOpc(byteValue);

        // Assert
        Assert.Equal(expectedChar, result);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsIncompatibleType_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "A";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    #endregion ConvertFromOpc Tests

    #region ConvertToOpc Tests

    [Fact]
    public void ConvertToOpc_WhenValueIsNull_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertToOpc(null);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData('A', (byte)65)]
    [InlineData('0', (byte)48)]
    [InlineData('\0', (byte)0)]
    public void ConvertToOpc_WhenValueIsChar_ReturnsCorrectByte(char charValue, byte expectedByte)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertToOpc(charValue);

        // Assert
        Assert.Equal(expectedByte, result);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsByte_ReturnsSameByte()
    {
        // Arrange
        var sut = CreateSut();
        const byte byteValue = 100;

        // Act
        var result = sut.ConvertToOpc(byteValue);

        // Assert
        Assert.Equal(byteValue, result);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsIncompatibleType_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const int incompatibleValue = 123; // int, not byte or char

        // Act
        var result = sut.ConvertToOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    #endregion ConvertToOpc Tests
}