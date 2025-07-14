using S7UaLib.Infrastructure.S7.Converters;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class S7LTimeConverterUnitTests
{
    private static S7LTimeConverter CreateSut()
    {
        return new S7LTimeConverter();
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
    [InlineData(1_000_000_000L, 10_000_000L)]       // 1 second
    [InlineData(-500_000_000L, -5_000_000L)]      // -500 milliseconds
    [InlineData(0L, 0L)]                         // Zero
    [InlineData(123456789L, 1234567L)]           // High precision (integer division of ns / 100)
    public void ConvertFromOpc_WithValidLong_ReturnsCorrectTimeSpan(long nanoseconds, long expectedTicks)
    {
        // Arrange
        var sut = CreateSut();
        var expectedTimeSpan = TimeSpan.FromTicks(expectedTicks);

        // Act
        var result = sut.ConvertFromOpc(nanoseconds);

        // Assert
        Assert.Equal(expectedTimeSpan, result);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsNotLong_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "not a long";

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
    [InlineData(1, 0, 0, 1_000_000_000L)]       // 1 second
    [InlineData(0, -500, 0, -500_000_000L)]      // -500 milliseconds
    [InlineData(0, 0, 0, 0L)]                    // Zero
    [InlineData(0, 123, 456, 123456000L)]      // High precision
    public void ConvertToOpc_WithValidTimeSpan_ReturnsCorrectLong(int seconds, int milliseconds, int microseconds, long expectedNanoseconds)
    {
        // Arrange
        var sut = CreateSut();
        var timeSpanValue = new TimeSpan(0, 0, 0, seconds, milliseconds, microseconds);

        // Act
        var result = sut.ConvertToOpc(timeSpanValue);

        // Assert
        Assert.Equal(expectedNanoseconds, result);
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
    }

    #endregion ConvertToOpc Tests
}