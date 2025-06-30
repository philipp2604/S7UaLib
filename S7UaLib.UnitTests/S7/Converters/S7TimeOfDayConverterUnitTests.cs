using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.S7.Converters;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7TimeOfDayConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;

    public S7TimeOfDayConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private S7TimeOfDayConverter CreateSut()
    {
        return new S7TimeOfDayConverter(_mockLogger.Object);
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
    [InlineData(0u)]
    [InlineData(1000u)]
    [InlineData(86399999u)] // 23:59:59.999
    public void ConvertFromOpc_WithValidUint_ReturnsCorrectTimeSpan(uint milliseconds)
    {
        // Arrange
        var sut = CreateSut();
        var expectedTimeSpan = TimeSpan.FromMilliseconds(milliseconds);

        // Act
        var result = sut.ConvertFromOpc(milliseconds);

        // Assert
        Assert.Equal(expectedTimeSpan, result);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsNotUint_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "not a uint";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.UInt32'")),
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
    [InlineData(0u)]
    [InlineData(1000u)]
    [InlineData(86399999u)]
    public void ConvertToOpc_WithValidTimeSpan_ReturnsCorrectUint(uint expectedMilliseconds)
    {
        // Arrange
        var sut = CreateSut();
        var timeSpan = TimeSpan.FromMilliseconds(expectedMilliseconds);

        // Act
        var result = sut.ConvertToOpc(timeSpan);

        // Assert
        Assert.Equal(expectedMilliseconds, result);
    }

    [Fact]
    public void ConvertToOpc_WhenTimeSpanIsNegative_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var negativeTime = TimeSpan.FromMilliseconds(-1);

        // Act
        var result = sut.ConvertToOpc(negativeTime);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("must be a positive value less than 24 hours")),
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
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("must be a positive value less than 24 hours")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsNotTimeSpan_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const int incompatibleValue = 12345;

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