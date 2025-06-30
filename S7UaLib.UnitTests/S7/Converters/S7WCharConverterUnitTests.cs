using Microsoft.Extensions.Logging;
using Moq;
using S7UaLib.S7.Converters;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7WCharConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;

    public S7WCharConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    private S7WCharConverter CreateSut()
    {
        return new S7WCharConverter(_mockLogger.Object);
    }

    #region Property Tests

    [Fact]
    public void TargetType_ReturnsCharType()
    {
        // Arrange
        var sut = CreateSut();

        // Act & Assert
        Assert.Equal(typeof(char), sut.TargetType);
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
    [InlineData((ushort)65, 'A')]
    [InlineData((ushort)8364, '€')] // Euro sign
    [InlineData((ushort)0, '\0')]
    public void ConvertFromOpc_WithValidUshort_ReturnsCorrectChar(ushort ushortValue, char expectedChar)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertFromOpc(ushortValue);

        // Assert
        Assert.Equal(expectedChar, result);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsNotUshort_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const string incompatibleValue = "A";

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
    [InlineData('A', (ushort)65)]
    [InlineData('€', (ushort)8364)]
    [InlineData('\0', (ushort)0)]
    public void ConvertToOpc_WithValidChar_ReturnsCorrectUshort(char charValue, ushort expectedUshort)
    {
        // Arrange
        var sut = CreateSut();

        // Act
        var result = sut.ConvertToOpc(charValue);

        // Assert
        Assert.Equal(expectedUshort, result);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsNotChar_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut();
        const int incompatibleValue = 123; // int, not char

        // Act
        var result = sut.ConvertToOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("but expected 'System.Char'")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion ConvertToOpc Tests
}