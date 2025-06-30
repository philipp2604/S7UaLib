using Microsoft.Extensions.Logging;
using Moq;
using Opc.Ua;
using S7UaLib.S7.Converters;
using S7UaLib.S7.Converters.Contracts;
using System.Collections;

namespace S7UaLib.UnitTests.S7.Converters;

[Trait("Category", "Unit")]
public class S7ElementwiseArrayConverterUnitTests
{
    private readonly Mock<ILogger> _mockLogger;
    private readonly Mock<IS7TypeConverter> _mockElementConverter;

    public S7ElementwiseArrayConverterUnitTests()
    {
        _mockLogger = new Mock<ILogger>();
        _mockElementConverter = new Mock<IS7TypeConverter>();
    }

    private S7ElementwiseArrayConverter CreateSut(Type opcArrayElementType)
    {
        return new S7ElementwiseArrayConverter(_mockElementConverter.Object, opcArrayElementType, _mockLogger.Object);
    }

    #region Constructor and Property Tests

    [Fact]
    public void Constructor_WithNullElementConverter_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new S7ElementwiseArrayConverter(null!, typeof(byte)));
    }

    [Fact]
    public void Constructor_WithNullOpcArrayElementType_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new S7ElementwiseArrayConverter(_mockElementConverter.Object, null!));
    }

    [Fact]
    public void TargetType_ReturnsCorrectGenericListType()
    {
        // Arrange
        _mockElementConverter.Setup(c => c.TargetType).Returns(typeof(DateTime));
        var sut = CreateSut(typeof(byte[]));

        // Act
        var result = sut.TargetType;

        // Assert
        Assert.Equal(typeof(List<DateTime>), result);
    }

    #endregion

    #region ConvertFromOpc Tests

    [Fact]
    public void ConvertFromOpc_WhenValueIsNull_ReturnsEmptyList()
    {
        // Arrange
        _mockElementConverter.Setup(c => c.TargetType).Returns(typeof(string));
        var sut = CreateSut(typeof(ushort));

        // Act
        var result = sut.ConvertFromOpc(null);

        // Assert
        Assert.NotNull(result);
        var list = Assert.IsType<IList>(result, exactMatch: false);
        Assert.Empty(list);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIs1DArray_ReturnsConvertedList()
    {
        // Arrange
        _mockElementConverter.Setup(c => c.TargetType).Returns(typeof(int));
        _mockElementConverter.Setup(c => c.ConvertFromOpc(It.Is<ushort>(v => v == 10))).Returns(100);
        _mockElementConverter.Setup(c => c.ConvertFromOpc(It.Is<ushort>(v => v == 20))).Returns(200);

        var sut = CreateSut(typeof(ushort));
        var opcArray = new ushort[] { 10, 20 };

        // Act
        var result = sut.ConvertFromOpc(opcArray);

        // Assert
        var list = Assert.IsType<List<int>>(result);
        Assert.Equal(2, list.Count);
        Assert.Contains(100, list);
        Assert.Contains(200, list);
        _mockElementConverter.Verify(c => c.ConvertFromOpc(It.IsAny<ushort>()), Times.Exactly(2));
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsMatrix_ReturnsConvertedList()
    {
        // Arrange
        var convertedValue1 = new DateTime(2024, 1, 1);
        var convertedValue2 = new DateTime(2024, 1, 2);
        _mockElementConverter.Setup(c => c.TargetType).Returns(typeof(DateTime));
        _mockElementConverter.Setup(c => c.ConvertFromOpc(It.Is<byte[]>(b => b.SequenceEqual(new byte[] { 1, 2 })))).Returns(convertedValue1);
        _mockElementConverter.Setup(c => c.ConvertFromOpc(It.Is<byte[]>(b => b.SequenceEqual(new byte[] { 3, 4 })))).Returns(convertedValue2);

        var sut = CreateSut(typeof(byte));

        var flattenedArray = new byte[] { 1, 2, 3, 4 };
        var opcMatrix = new Matrix(flattenedArray, BuiltInType.Byte, 2, 2);

        // Act
        var result = sut.ConvertFromOpc(opcMatrix);

        // Assert
        var list = Assert.IsType<List<DateTime>>(result);
        Assert.Equal(2, list.Count);
        Assert.Contains(convertedValue1, list);
        Assert.Contains(convertedValue2, list);
        _mockElementConverter.Verify(c => c.ConvertFromOpc(It.IsAny<byte[]>()), Times.Exactly(2));
    }

    [Fact]
    public void ConvertFromOpc_WithUnsupportedType_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut(typeof(byte));

        // Act
        var result = sut.ConvertFromOpc("a string");

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Received an unsupported type")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion

    #region ConvertToOpc Tests

    [Fact]
    public void ConvertToOpc_WhenValueIsNull_ReturnsNull()
    {
        // Arrange
        var sut = CreateSut(typeof(byte));

        // Act
        var result = sut.ConvertToOpc(null);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WhenListIsEmpty_ReturnsNull()
    {
        // Arrange
        _mockElementConverter.Setup(c => c.TargetType).Returns(typeof(int));
        var sut = CreateSut(typeof(ushort));

        // Act
        var result = sut.ConvertToOpc(new List<int>());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ConvertToOpc_WhenElementConvertsToValue_Returns1DArray()
    {
        // Arrange
        _mockElementConverter.Setup(c => c.ConvertToOpc(100)).Returns((ushort)10);
        _mockElementConverter.Setup(c => c.ConvertToOpc(200)).Returns((ushort)20);
        var sut = CreateSut(typeof(ushort));
        var userList = new List<int> { 100, 200 };

        // Act
        var result = sut.ConvertToOpc(userList);

        // Assert
        var array = Assert.IsType<ushort[]>(result);
        Assert.Equal(2, array.Length);
        Assert.Equal((ushort)10, array[0]);
        Assert.Equal((ushort)20, array[1]);
    }

    [Fact]
    public void ConvertToOpc_WhenElementConvertsToArray_ReturnsMatrix()
    {
        // Arrange
        _mockElementConverter.Setup(c => c.ConvertToOpc(It.IsAny<DateTime>()))
            .Returns<DateTime>(dt => new[] { (byte)dt.Day, (byte)dt.Month });
        var sut = CreateSut(typeof(byte));
        var userList = new List<DateTime> { new(2024, 1, 10), new(2024, 2, 20) };

        // Act
        var result = sut.ConvertToOpc(userList);

        // Assert
        var matrix = Assert.IsType<Matrix>(result);
        Assert.Equal([2, 2], matrix.Dimensions);
        var flattened = Assert.IsType<byte[]>(matrix.Elements);
        Assert.Equal(new byte[] { 10, 1, 20, 2 }, flattened);
    }

    [Fact]
    public void ConvertToOpc_WhenElementConverterFails_ReturnsNullAndLogsError()
    {
        // Arrange
        _mockElementConverter.Setup(c => c.ConvertToOpc(It.IsAny<int>())).Throws<NotImplementedException>();
        var sut = CreateSut(typeof(ushort));
        var userList = new List<int> { 100 };

        // Act
        var result = sut.ConvertToOpc(userList);

        // Assert
        Assert.Null(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("The element converter failed")),
                It.IsAny<NotImplementedException>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}