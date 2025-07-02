using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.S7.Converters;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7CounterConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;

    public S7CounterConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private S7CounterConverter CreateSut()
    {
        return new S7CounterConverter(_mockLogger.Object);
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
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.UInt16'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Invalid BCD format for counter value")),
                It.IsAny<ArgumentOutOfRangeException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.UInt16'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("outside the valid range (0-999)")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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