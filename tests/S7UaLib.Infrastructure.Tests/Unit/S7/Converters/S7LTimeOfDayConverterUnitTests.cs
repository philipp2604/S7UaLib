﻿using S7UaLib.Infrastructure.S7.Converters;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class S7LTimeOfDayConverterUnitTests
{
    private static S7LTimeOfDayConverter CreateSut()
    {
        return new S7LTimeOfDayConverter();
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
    }

    #endregion ConvertToOpc Tests
}