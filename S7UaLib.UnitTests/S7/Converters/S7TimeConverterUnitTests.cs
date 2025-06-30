using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.S7.Converters;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7TimeConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;

    public S7TimeConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private S7TimeConverter CreateSut()
    {
        return new S7TimeConverter(_mockLogger.Object);
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

    [Theory]
    [InlineData(1000)]
    [InlineData(-500)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void ConvertFromOpc_WithValidInt_ReturnsCorrectTimeSpan(int milliseconds)
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
    public void ConvertFromOpc_WhenValueIsNotInt_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "not an int";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.Int32'")),
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
    [InlineData(1000)]
    [InlineData(-500)]
    [InlineData(0)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void ConvertToOpc_WithValidTimeSpan_ReturnsCorrectInt(int expectedMilliseconds)
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
    public void ConvertToOpc_WithTimeSpanOutsideIntRange_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var largeTimeSpan = TimeSpan.FromMilliseconds((long)int.MaxValue + 1);

        // Act
        var result = sut.ConvertToOpc(largeTimeSpan);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("is outside the valid range for a 32-bit integer")),
                It.IsAny<OverflowException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsNotTimeSpan_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var incompatibleValue = new DateTime();

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