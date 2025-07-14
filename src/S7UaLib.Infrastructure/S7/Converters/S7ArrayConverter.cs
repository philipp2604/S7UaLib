using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// A generic converter that handles conversions between arrays from the OPC server (T[])
/// and more flexible .NET collections like List{T}.
/// </summary>
/// <typeparam name="T">The underlying type of the array elements.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="S7ArrayConverter{T}"/> class.
/// </remarks>
public class S7ArrayConverter<T> : IS7TypeConverter
{
    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(List<T>);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts an array from the OPC server (T[]) into a .NET List{T}.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be an array of type T[].</param>
    /// <returns>
    /// A new List{T} containing the elements from the source array.
    /// Returns an empty list if the input is null.
    /// </returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null ? new List<T>() : opcValue is T[] opcArray ? new List<T>(opcArray) : (object?)null;
    }

    /// <summary>
    /// Converts a user-provided collection (e.g., List{T}, T[], IEnumerable{T}) back
    /// to a simple array (T[]) as expected by the OPC server.
    /// </summary>
    /// <param name="userValue">The collection from the user application.</param>
    /// <returns>
    /// An array of type T[] for the OPC server. Returns an empty array if the input is null.
    /// </returns>
    public object? ConvertToOpc(object? userValue)
    {
        return userValue is null ? Array.Empty<T>() : userValue is IEnumerable<T> userEnumerable ? userEnumerable.ToArray() : (object?)null;
    }

    #endregion Public Methods
}