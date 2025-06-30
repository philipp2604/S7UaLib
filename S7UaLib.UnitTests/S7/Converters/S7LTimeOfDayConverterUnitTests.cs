using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.S7.Converters;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7LTimeOfDayConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;

    public S7LTimeOfDayConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private S7LTimeOfDayConverter CreateSut()
    {
        return new S7LTimeOfDayConverter(_mockLogger.Object);
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

    #endregion

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
    [InlineData(0UL, 0, 0, 0, 0)]
    [InlineData(100UL, 0, 0, 0, 1)] // 1 Tick
    [InlineData(37230123456700UL, 10, 20, 30, 1234567)] // 10:20:30.1234567
    [InlineData(86399999999900UL, 23, 59, 59, 9999999)] // 23:59:59.9999999
    public void ConvertFromOpc_WithValidUlong_ReturnsCorrectTimeSpan(ulong nanoseconds, int h, int m, int s, int ticks)
    {
        // Arrange
        var sut = CreateSut();
        var expectedTimeSpan = new TimeSpan(0, h, m, s).Add(TimeSpan.FromTicks(ticks));

        // Act
        var result = sut.ConvertFromOpc(nanoseconds);

        // Assert
        Assert.Equal(expectedTimeSpan, result);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsNotUlong_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "not a ulong";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.UInt64'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
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
    [InlineData(0, 0, 0, 0, 0UL)]
    [InlineData(0, 0, 0, 1, 100UL)]
    [InlineData(10, 20, 30, 1234567, 37230123456700UL)]
    [InlineData(23, 59, 59, 9999999, 86399999999900UL)]
    public void ConvertToOpc_WithValidTimeSpan_ReturnsCorrectUlong(int h, int m, int s, int ticks, ulong expectedNanoseconds)
    {
        // Arrange
        var sut = CreateSut();
        var timeSpanValue = new TimeSpan(0, h, m, s).Add(TimeSpan.FromTicks(ticks));

        // Act
        var result = sut.ConvertToOpc(timeSpanValue);

        // Assert
        Assert.Equal(expectedNanoseconds, result);
    }

    [Fact]
    public void ConvertToOpc_WhenTimeSpanIsNegative_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var negativeTime = TimeSpan.FromTicks(-1);

        // Act
        var result = sut.ConvertToOpc(negativeTime);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("is outside the valid range for LTIME_OF_DAY")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertToOpc_WhenTimeSpanIsTooLarge_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var largeTime = TimeSpan.FromDays(1);

        // Act
        var result = sut.ConvertToOpc(largeTime);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("is outside the valid range for LTIME_OF_DAY")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsNotTimeSpan_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const long incompatibleValue = 12345L;

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

    #endregion ConvertToOpc Tests
}