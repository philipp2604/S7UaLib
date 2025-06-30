using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.S7.Converters;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7DateAndTimeConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;

    public S7DateAndTimeConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private S7DateAndTimeConverter CreateSut()
    {
        return new S7DateAndTimeConverter(_mockLogger.Object);
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

    [Fact]
    public void ConvertFromOpc_WhenValueIsNotByteArray_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "not a byte array";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.Byte[]'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertFromOpc_WhenByteArrayIsWrongLength_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        var wrongLengthArray = new byte[7];

        // Act
        var result = sut.ConvertFromOpc(wrongLengthArray);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("must have a length of 8")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertFromOpc_WithInvalidBcdValue_ThrowsFormatException()
    {
        // Arrange
        var sut = CreateSut();
        var invalidBcdArray = new byte[] { 0x24, 0x1A, 0x21, 0x13, 0x45, 0x30, 0x12, 0x33 }; // 0x1A is not valid BCD

        // Act & Assert
        Assert.Throws<FormatException>(() => sut.ConvertFromOpc(invalidBcdArray));
    }

    [Theory]
    [MemberData(nameof(GetBcdToDateTimeTestData))]
    public void ConvertFromOpc_WhenValueIsCorrectByteArray_ReturnsCorrectDateTime(byte[] bcdBytes, DateTime expectedDateTime)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertFromOpc(bcdBytes);

        // Assert
        Assert.Equal(expectedDateTime, result);
    }

    public static TheoryData<byte[], DateTime> GetBcdToDateTimeTestData()
    {
        return new TheoryData<byte[], DateTime>
        {
            // 21. May 2024, 13:45:30.123 (Tuesday, DayOfWeek=2+1=3)
            {
                new byte[] { 0x24, 0x05, 0x21, 0x13, 0x45, 0x30, 0x12, 0x33 },
                new DateTime(2024, 5, 21, 13, 45, 30, 123)
            },
            // 1. October 1999, 08:10:05.987 (Friday, DayOfWeek=5+1=6)
            {
                new byte[] { 0x99, 0x10, 0x01, 0x08, 0x10, 0x05, 0x98, 0x76 },
                new DateTime(1999, 10, 1, 8, 10, 5, 987)
            }
        };
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
    public void ConvertToOpc_WhenValueIsNotDateTime_ThrowsArgumentExceptionAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const int incompatibleValue = 12345;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => sut.ConvertToOpc(incompatibleValue));
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.DateTime'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void ConvertToOpc_WithYearOutsideRange_ReturnsArrayAndLogsWarning()
    {
        // Arrange
        var sut = CreateSut();
        var dateTime = new DateTime(1989, 1, 1);

        // Act
        var result = sut.ConvertToOpc(dateTime);

        // Assert
        Assert.NotNull(result);
        Assert.IsType<byte[]>(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("is outside the recommended S7 DT range")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [MemberData(nameof(GetBcdToDateTimeTestData))]
    public void ConvertToOpc_WhenValueIsDateTime_ReturnsCorrectByteArray(byte[] expectedBcdBytes, DateTime dateTime)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertToOpc(dateTime);

        // Assert
        var actualBytes = Assert.IsType<byte[]>(result);
        Assert.Equal(expectedBcdBytes, actualBytes);
    }

    #endregion ConvertToOpc Tests
}