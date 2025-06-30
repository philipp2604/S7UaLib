using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// A generic converter that handles conversions between arrays from the OPC server (T[])
/// and more flexible .NET collections like List{T}.
/// </summary>
/// <typeparam name="T">The underlying type of the array elements.</typeparam>
/// <remarks>
/// Initializes a new instance of the <see cref="S7ArrayConverter{T}"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7ArrayConverter<T>(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;

    /// <inheritdoc/>
    public Type TargetType => typeof(List<T>);

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
        if (opcValue is null)
        {
            return new List<T>();
        }

        if (opcValue is T[] opcArray)
        {
            return new List<T>(opcArray);
        }

        _logger?.LogError(
            "Value from OPC was of type '{ActualType}' but expected '{ExpectedType}[]'.",
            opcValue.GetType().FullName,
            typeof(T).FullName);

        return null;
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
        if (userValue is null)
        {
            return Array.Empty<T>();
        }

        if (userValue is IEnumerable<T> userEnumerable)
        {
            return userEnumerable.ToArray();
        }

        _logger?.LogError(
            "User value was of type '{ActualType}' but expected a compatible collection of '{ExpectedType}'.",
            userValue.GetType().FullName,
            typeof(T).FullName);

        return null;
    }
}