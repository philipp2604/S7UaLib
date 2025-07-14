using S7UaLib.Infrastructure.S7.Converters;

namespace S7UaLib.Infrastructure.Tests.Unit.S7.Converters;

[Trait("Category", "Unit")]
public class S7ArrayConverterUnitTests
{
    private static S7ArrayConverter<T> CreateSut<T>()
    {
        return new S7ArrayConverter<T>();
    }

    #region Property Tests

    [Fact]
    public void TargetType_ReturnsCorrectGenericListType()
    {
        // Arrange
        var sut = CreateSut<int>();

        // Act
        var targetType = sut.TargetType;

        // Assert
        Assert.Equal(typeof(List<int>), targetType);
    }

    #endregion Property Tests

    #region ConvertFromOpc Tests

    [Fact]
    public void ConvertFromOpc_WhenValueIsNull_ReturnsEmptyList()
    {
        // Arrange
        var sut = CreateSut<string>();

        // Act
        var result = sut.ConvertFromOpc(null);

        // Assert
        Assert.NotNull(result);
        var list = Assert.IsType<List<string>>(result);
        Assert.Empty(list);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsCorrectArrayType_ReturnsPopulatedList()
    {
        // Arrange
        var sut = CreateSut<int>();
        var sourceArray = new[] { 10, 20, 30 };

        // Act
        var result = sut.ConvertFromOpc(sourceArray);

        // Assert
        Assert.NotNull(result);
        var list = Assert.IsType<List<int>>(result);
        Assert.Equal(sourceArray.Length, list.Count);
        Assert.Equal(sourceArray, list);
    }

    [Fact]
    public void ConvertFromOpc_WhenValueIsIncompatibleType_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut<int>();
        const string incompatibleValue = "this is not an int array";

        // Act
        var result = sut.ConvertFromOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    #endregion ConvertFromOpc Tests

    #region ConvertToOpc Tests

    [Fact]
    public void ConvertToOpc_WhenValueIsNull_ReturnsEmptyArray()
    {
        // Arrange
        var sut = CreateSut<bool>();

        // Act
        var result = sut.ConvertToOpc(null);

        // Assert
        Assert.NotNull(result);
        var array = Assert.IsType<bool[]>(result);
        Assert.Empty(array);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsList_ReturnsCorrectArray()
    {
        // Arrange
        var sut = CreateSut<string>();
        var sourceList = new List<string> { "A", "B", "C" };

        // Act
        var result = sut.ConvertToOpc(sourceList);

        // Assert
        Assert.NotNull(result);
        var array = Assert.IsType<string[]>(result);
        Assert.Equal(sourceList.Count, array.Length);
        Assert.Equal(sourceList, array);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsArray_ReturnsCorrectArray()
    {
        // Arrange
        var sut = CreateSut<double>();
        var sourceArray = new[] { 1.1, 2.2, 3.3 };

        // Act
        var result = sut.ConvertToOpc(sourceArray);

        // Assert
        Assert.NotNull(result);
        var resultArray = Assert.IsType<double[]>(result);
        Assert.Equal(sourceArray.Length, resultArray.Length);
        Assert.Equal(sourceArray, resultArray);
    }

    [Fact]
    public void ConvertToOpc_WhenValueIsIncompatibleType_ReturnsNullAndLogsError()
    {
        // Arrange
        var sut = CreateSut<int>();
        var incompatibleValue = new object();

        // Act
        var result = sut.ConvertToOpc(incompatibleValue);

        // Assert
        Assert.Null(result);
    }

    #endregion ConvertToOpc Tests
}