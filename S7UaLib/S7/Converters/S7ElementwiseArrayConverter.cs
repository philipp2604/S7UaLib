using Microsoft.Extensions.Logging;
using Opc.Ua;
using S7UaLib.S7.Converters.Contracts;
using System.Collections;

namespace S7UaLib.S7.Converters;

/// <summary>
/// A "meta-converter" that converts a list of complex objects by applying a provided element converter to each item.
/// It can produce either a simple one-dimensional array or a two-dimensional OPC UA Matrix.
/// </summary>
public class S7ElementwiseArrayConverter : IS7TypeConverter
{
    private readonly ILogger? _logger;
    private readonly IS7TypeConverter _elementConverter;
    private readonly Type _opcArrayElementType;
    private readonly Type _opcArrayType;

    /// <summary>
    /// Initializes a new instance of the <see cref="S7ElementwiseArrayConverter"/> class.
    /// </summary>
    /// <param name="elementConverter">The converter to be applied to each individual element in the list.</param>
    /// <param name="opcArrayElementType">The underlying raw .NET type of a single element on the OPC server (e.g., typeof(byte) for a DATE_AND_TIME element).</param>
    /// <param name="logger">An optional logger for diagnostics.</param>
    public S7ElementwiseArrayConverter(IS7TypeConverter elementConverter, Type opcArrayElementType, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(elementConverter);
        ArgumentNullException.ThrowIfNull(opcArrayElementType);

        _logger = logger;
        _elementConverter = elementConverter;
        _opcArrayElementType = opcArrayElementType;
        _opcArrayType = opcArrayElementType.MakeArrayType();
    }

    /// <summary>
    /// Gets the target .NET type, which is a generic List where T is the target type of the wrapped element converter.
    /// For example, if the element converter targets <see cref="DateTime"/>, this will be <c>typeof(List{DateTime})</c>.
    /// </summary>
    public Type TargetType => typeof(List<>).MakeGenericType(_elementConverter.TargetType);

    /// <summary>
    /// Converts a raw value from the OPC server (either a 1D array or a 2D Matrix) into a .NET list of objects.
    /// </summary>
    /// <param name="opcValue">The value from the OPC server.</param>
    /// <returns>A new <see cref="IList"/> containing the converted elements, or an empty list if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return Activator.CreateInstance(TargetType);
        }

        switch (opcValue)
        {
            case Matrix matrix:
                return ConvertFromMatrix(matrix);

            case Array array when array.GetType() == _opcArrayType:
                return ConvertFromArray(array);

            default:
                _logger?.LogError(
                    "Received an unsupported type '{ActualType}' from OPC. Expected a Matrix or '{ExpectedType}'.",
                    opcValue.GetType().FullName,
                    _opcArrayType.FullName);
                return null;
        }
    }

    /// <summary>
    /// Converts a .NET list of objects back into a raw OPC UA value (either a 1D array or a 2D Matrix).
    /// </summary>
    /// <param name="userValue">The <see cref="IList"/> from the user application.</param>
    /// <returns>An <see cref="Array"/> or a <see cref="Matrix"/> for the OPC server, or null if the input is null or empty.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is not IList userList || userList.Count == 0)
        {
            return null;
        }

        object? firstNonNullItem = userList.Cast<object?>().FirstOrDefault(item => item is not null);
        if (firstNonNullItem is null)
        {
            return null;
        }

        object? firstConvertedElement;
        try
        {
            firstConvertedElement = _elementConverter.ConvertToOpc(firstNonNullItem);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "The element converter failed to process the first non-null item of type '{ItemType}'.", firstNonNullItem.GetType().FullName);
            return null;
        }

        return firstConvertedElement is Array elementAsArray ? ConvertToMatrix(userList, elementAsArray.Length) : (object)ConvertToArray(userList);
    }

    private object? ConvertFromArray(Array opcArray)
    {
        if (Activator.CreateInstance(TargetType) is not IList resultList)
        {
            _logger?.LogError("Could not create an instance of the target list type: {TargetType}.", TargetType.FullName);
            return null;
        }

        foreach (var opcElement in opcArray)
        {
            resultList.Add(_elementConverter.ConvertFromOpc(opcElement));
        }

        return resultList;
    }

    private object? ConvertFromMatrix(Matrix matrix)
    {
        if (Activator.CreateInstance(TargetType) is not IList resultList)
        {
            _logger?.LogError("Could not create an instance of the target list type: {TargetType}.", TargetType.FullName);
            return null;
        }

        if (matrix.Elements is not { } flattenedArray || matrix.Dimensions.Length != 2)
        {
            _logger?.LogWarning("Matrix from OPC is malformed: Elements are null or dimensions are not 2D. Returning empty list.");
            return resultList;
        }

        int outerCount = matrix.Dimensions[0];
        int innerCount = matrix.Dimensions[1];

        for (int i = 0; i < outerCount; i++)
        {
            var singleElementArray = Array.CreateInstance(_opcArrayElementType, innerCount);
            Array.Copy(flattenedArray, i * innerCount, singleElementArray, 0, innerCount);
            resultList.Add(_elementConverter.ConvertFromOpc(singleElementArray));
        }

        return resultList;
    }

    private Array ConvertToArray(IList userList)
    {
        var opcArray = Array.CreateInstance(_opcArrayElementType, userList.Count);
        for (int i = 0; i < userList.Count; i++)
        {
            opcArray.SetValue(_elementConverter.ConvertToOpc(userList[i]), i);
        }

        return opcArray;
    }

    private Matrix? ConvertToMatrix(IList userList, int innerCount)
    {
        int outerCount = userList.Count;
        var flattenedArray = Array.CreateInstance(_opcArrayElementType, outerCount * innerCount);

        for (int i = 0; i < userList.Count; i++)
        {
            var elementToConvert = userList[i];
            if (elementToConvert is null) continue;

            if (_elementConverter.ConvertToOpc(elementToConvert) is not Array singleElementArray)
            {
                _logger?.LogError("Element at index {Index} did not convert to an array as expected.", i);
                return null;
            }

            Array.Copy(singleElementArray, 0, flattenedArray, i * innerCount, innerCount);
        }

        var builtInType = GetBuiltInType(_opcArrayElementType);
        return new Matrix(flattenedArray, builtInType, outerCount, innerCount);
    }

    private BuiltInType GetBuiltInType(Type systemType)
    {
        var builtInType = TypeInfo.Construct(systemType).BuiltInType;

        if(builtInType == BuiltInType.Null)
        {
            _logger?.LogError("The .NET type '{TypeName}' does not have a valid BuiltInType mapping.", systemType.FullName);
            return BuiltInType.Null;
        }
        else
        {
            return builtInType;
        }
    }
}