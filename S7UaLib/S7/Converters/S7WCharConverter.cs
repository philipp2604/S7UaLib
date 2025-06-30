using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between a 16-bit wide character (S7 WCHAR), represented as a <see cref="ushort"/>,
/// and the standard .NET <see cref="char"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7WCharConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7WCharConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;

    /// <inheritdoc/>
    public Type TargetType => typeof(char);

    /// <summary>
    /// Converts a 16-bit unsigned integer value from the OPC server into a .NET character.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ushort"/>.</param>
    /// <returns>The corresponding <see cref="char"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is ushort ushortValue)
        {
            return Convert.ToChar(ushortValue);
        }

        _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.UInt16'.", opcValue.GetType().FullName);
        return null;
    }

    /// <summary>
    /// Converts a .NET character back into its 16-bit unsigned integer representation for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="char"/> from the user application.</param>
    /// <returns>The corresponding <see cref="ushort"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is char charValue)
        {
            return Convert.ToUInt16(charValue);
        }

        _logger?.LogError("User value was of type '{ActualType}' but expected 'System.Char'.", userValue.GetType().FullName);
        return null;
    }
}