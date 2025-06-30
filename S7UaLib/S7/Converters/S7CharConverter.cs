using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between a single byte from an OPC server (representing an S7 CHAR)
/// and a .NET <see cref="char"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7CharConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7CharConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;

    /// <inheritdoc/>
    public Type TargetType => typeof(char);

    /// <summary>
    /// Converts a byte value from the OPC server into a .NET character.
    /// S7 CHAR is treated as an 8-bit ASCII-compatible character.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="byte"/>.</param>
    /// <returns>The corresponding <see cref="char"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is byte byteValue)
        {
            return Convert.ToChar(byteValue);
        }

        _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.Byte'.", opcValue.GetType().FullName);

        return null;
    }

    /// <summary>
    /// Converts a .NET character back into a byte for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="char"/> (or <see cref="byte"/>) from the user application.</param>
    /// <returns>The corresponding <see cref="byte"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        switch (userValue)
        {
            case char charValue:
                return Convert.ToByte(charValue);

            case byte byteValue:
                return byteValue;

            default:
                _logger?.LogError("User value was of type '{ActualType}' but expected 'System.Char' or 'System.Byte'.", userValue.GetType().FullName);
                return null;
        }
    }
}