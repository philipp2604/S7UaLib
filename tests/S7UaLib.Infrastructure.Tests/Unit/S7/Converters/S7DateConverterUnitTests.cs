using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.Infrastructure.S7.Converters;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class S7DateConverterUnitTests
{
    private static S7DateConverter CreateSut()
    {
        return new S7DateConverter();
    }

    #region Property Tests

    [Fact]
    public void TargetType_ReturnsDateTimeType()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Equal(typeof(DateTime), sut.TargetType);
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
    [InlineData((ushort)0, 1990, 1, 1)]
    [InlineData((ushort)365, 1991, 1, 1)]
    [InlineData((ushort)12603, 2024, 7, 4)]
    public void ConvertFromOpc_WithValidUshort_ReturnsCorrectDateTime(ushort daysSinceEpoch, int year, int month, int day)
    {
        // Arrange
        var sut = CreateSut();
        var expectedDate = new DateTime(year, month, day);

        // Act
        var result = sut.ConvertFromOpc(daysSinceEpoch);

        // Assert
        Assert.Equal(expectedDate, result);
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
    [InlineData(1990, 1, 1, 12, 0, 0, (ushort)0)]
    [InlineData(1991, 1, 1, 0, 0, 0, (ushort)365)]
    [InlineData(2024, 7, 4, 23, 59, 59, (ushort)12603)]
    public void ConvertToOpc_WithValidDateTime_ReturnsCorrectUshort(int year, int month, int day, int hour, int minute, int second, ushort expectedDays)
    {
        // Arrange
        var sut = CreateSut();
        var dateValue = new DateTime(year, month, day, hour, minute, second);

        // Act
        var result = sut.ConvertToOpc(dateValue);

        // Assert
        Assert.Equal(expectedDays, result);
    }

    [Fact]
    public void ConvertToOpc_WithDateBeforeEpoch_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var dateBeforeEpoch = new DateTime(1989, 12, 31);

        // Act
        var result = sut.ConvertToOpc(dateBeforeEpoch);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WithDateAfterMax_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var dateAfterMax = new DateTime(2100, 1, 1);

        // Act
        var result = sut.ConvertToOpc(dateAfterMax);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsNotDateTime_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var incompatibleValue = new object();

        // Act
        var result = sut.ConvertToOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    #endregion ConvertToOpc Tests
}