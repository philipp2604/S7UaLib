using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.S7.Converters;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7S5TimeConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;

    public S7S5TimeConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private S7S5TimeConverter CreateSut()
    {
        return new S7S5TimeConverter(_mockLogger.Object);
    }

    #region Property Tests

    [Fact]
    public void TargetType_ReturnsTimeSpanType()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Equal(typeof(TimeSpan), sut.TargetType);
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
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Invalid S5TIME format")),
                It.IsAny<ArgumentOutOfRangeException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    // Formula: (TimeBaseCode << 12) | BCD_Value
    [InlineData((ushort)0x0123, 1230)]      // (0<<12)|0x123 => Time base 0 (10ms): 123 * 10ms = 1230ms
    [InlineData((ushort)0x1456, 45600)]     // (1<<12)|0x456 => Time base 1 (100ms): 456 * 100ms = 45600ms
    [InlineData((ushort)0x2789, 789000)]    // (2<<12)|0x789 => Time base 2 (1s): 789 * 1s = 789000ms
    [InlineData((ushort)0x3999, 9990000)]   // (3<<12)|0x999 => Time base 3 (10s): 999 * 10s = 9990000ms
    [InlineData((ushort)0x0000, 0)]         // Zero value
    public void ConvertFromOpc_WithValidUshort_ReturnsCorrectTimeSpan(ushort s5TimeValue, int expectedMilliseconds)
    {
        // Arrange
        var sut = CreateSut();
        var expectedTimeSpan = TimeSpan.FromMilliseconds(expectedMilliseconds);

        // Act
        var result = sut.ConvertFromOpc(s5TimeValue);

        // Assert
        Assert.Equal(expectedTimeSpan, result);
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
    public void ConvertToOpc_WhenValueIsNotTimeSpan_ReturnsNullAndLogsError()
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
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.TimeSpan'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertToOpc_WhenTimeSpanIsNegative_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var negativeTime = TimeSpan.FromSeconds(-1);

        // Act
        var result = sut.ConvertToOpc(negativeTime);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("is outside the valid range for S5TIME")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertToOpc_WhenTimeSpanIsTooLarge_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var largeTime = TimeSpan.FromSeconds(9991);

        // Act
        var result = sut.ConvertToOpc(largeTime);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("is outside the valid range for S5TIME")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(1230, (ushort)0x0123)]      // Should resolve to time base 0
    [InlineData(45600, (ushort)0x1456)]     // Should resolve to time base 1
    [InlineData(789000, (ushort)0x2789)]    // Should resolve to time base 2
    [InlineData(9990000, (ushort)0x3999)]   // Should resolve to time base 3
    [InlineData(1000, (ushort)0x0100)]      // Ambiguous case, should choose smallest time base (0)
    [InlineData(12345, (ushort)0x3001)]     // Rounding case -> 10s base, rounded to 1 -> (3<<12)|0x001
    [InlineData(0, (ushort)0x0000)]         // Zero value
    public void ConvertToOpc_WithValidTimeSpan_ReturnsCorrectUshort(int milliseconds, ushort expectedS5Time)
    {
        // Arrange
        var sut = CreateSut();
        var timeSpan = TimeSpan.FromMilliseconds(milliseconds);

        // Act
        var result = sut.ConvertToOpc(timeSpan);

        // Assert
        Assert.Equal(expectedS5Time, result);
    }

    #endregion ConvertToOpc Tests
}