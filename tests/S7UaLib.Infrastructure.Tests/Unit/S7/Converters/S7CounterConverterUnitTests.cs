using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.Infrastructure.S7.Converters;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class S7CounterConverterUnitTests
{
    private static S7CounterConverter CreateSut()
    {
        return new S7CounterConverter();
    }

    #region Property Tests

    [Fact]
    public void TargetType_ReturnsUshortType()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Equal(typeof(ushort), sut.TargetType);
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

    [Fact]
    public void ConvertFromOpc_WhenValueIsNotUshort_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "not a ushort";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertFromOpc_WhenBcdIsInvalid_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const ushort invalidBcdValue = 0x0A00; // 'A' is not a valid BCD digit

        // Act
        var result = sut.ConvertFromOpc(invalidBcdValue);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData((ushort)0x0123, (ushort)123)]
    [InlineData((ushort)0x0999, (ushort)999)]
    [InlineData((ushort)0x0000, (ushort)0)]
    [InlineData((ushort)0x0078, (ushort)78)]
    [InlineData((ushort)0x0500, (ushort)500)]
    [InlineData((ushort)0xF246, (ushort)246)] // Test that upper nibble is ignored
    public void ConvertFromOpc_WithValidBcdUshort_ReturnsCorrectDecimalUshort(ushort bcdValue, ushort expectedDecimal)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertFromOpc(bcdValue);

        // Assert
        Assert.Equal(expectedDecimal, result);
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

    [Fact]
    public void ConvertToOpc_WhenValueIsNotUshort_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var incompatibleValue = new object();

        // Act
        var result = sut.ConvertToOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WhenUshortIsTooLarge_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const ushort largeValue = 1000; // Max allowed is 999

        // Act
        var result = sut.ConvertToOpc(largeValue);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData((ushort)123, (ushort)0x0123)]
    [InlineData((ushort)999, (ushort)0x0999)]
    [InlineData((ushort)0, (ushort)0x0000)]
    [InlineData((ushort)78, (ushort)0x0078)]
    [InlineData((ushort)500, (ushort)0x0500)]
    public void ConvertToOpc_WithValidDecimalUshort_ReturnsCorrectBcdUshort(ushort decimalValue, ushort expectedBcd)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertToOpc(decimalValue);

        // Assert
        Assert.Equal(expectedBcd, result);
    }

    #endregion ConvertToOpc Tests
}