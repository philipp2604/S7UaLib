using Opc.Ua;
using S7UaLib.Core.S7.Converters;
using System.Collections;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// A "meta-converter" that converts a list of complex objects by applying a provided element converter to each item.
/// It can produce either a simple one-dimensional array or a two-dimensional OPC UA Matrix.
/// </summary>
public class S7ElementwiseArrayConverter : IS7TypeConverter
{
    #region Private Fields

    private readonly IS7TypeConverter _elementConverter;
    private readonly Type _opcArrayElementType;
    private readonly Type _opcArrayType;

    #endregion Private Fields

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="S7ElementwiseArrayConverter"/> class.
    /// </summary>
    /// <param name="elementConverter">The converter to be applied to each individual element in the list.</param>
    /// <param name="opcArrayElementType">The underlying raw .NET type of a single element on the OPC server (e.g., typeof(byte) for a DATE_AND_TIME element).</param>
    public S7ElementwiseArrayConverter(IS7TypeConverter elementConverter, Type opcArrayElementType)
    {
        ArgumentNullException.ThrowIfNull(elementConverter);
        ArgumentNullException.ThrowIfNull(opcArrayElementType);

        _elementConverter = elementConverter;
        _opcArrayElementType = opcArrayElementType;
        _opcArrayType = opcArrayElementType.MakeArrayType();
    }

    #endregion Constructors

    #region Public Properties

    /// <summary>
    /// Gets the target .NET type, which is a generic List where T is the target type of the wrapped element converter.
    /// For example, if the element converter targets <see cref="DateTime"/>, this will be <c>typeof(List{DateTime})</c>.
    /// </summary>
    public Type TargetType => typeof(List<>).MakeGenericType(_elementConverter.TargetType);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a raw value from the OPC server (either a 1D array or a 2D Matrix) into a .NET list of objects.
    /// </summary>
    /// <param name="opcValue">The value from the OPC server.</param>
    /// <returns>A new <see cref="IList"/> containing the converted elements, or an empty list if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null
            ? Activator.CreateInstance(TargetType)
            : opcValue switch
            {
                Matrix matrix => ConvertFromMatrix(matrix),
                Array array when array.GetType() == _opcArrayType => ConvertFromArray(array),
                _ => null,
            };
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
        catch (Exception)
        {
            return null;
        }

        return firstConvertedElement is Array elementAsArray ? ConvertToMatrix(userList, elementAsArray.Length) : (object)ConvertToArray(userList);
    }

    #endregion Public Methods

    #region Private Methods

    private object? ConvertFromArray(Array opcArray)
    {
        if (Activator.CreateInstance(TargetType) is not IList resultList)
        {
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
            return null;
        }

        if (matrix.Elements is not { } flattenedArray || matrix.Dimensions.Length != 2)
        {
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
                return null;
            }

            Array.Copy(singleElementArray, 0, flattenedArray, i * innerCount, innerCount);
        }

        var builtInType = GetBuiltInType(_opcArrayElementType);
        return new Matrix(flattenedArray, builtInType, outerCount, innerCount);
    }

    private static BuiltInType GetBuiltInType(Type systemType)
    {
        var builtInType = TypeInfo.Construct(systemType).BuiltInType;

        return builtInType == BuiltInType.Null ? BuiltInType.Null : builtInType;
    }

    #endregion Private Methods
}