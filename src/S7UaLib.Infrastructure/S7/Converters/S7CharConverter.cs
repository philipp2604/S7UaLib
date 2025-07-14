using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Handles the conversion between a single byte from an OPC server (representing an S7 CHAR)
/// and a .NET <see cref="char"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7CharConverter"/> class.
/// </remarks>
public class S7CharConverter : IS7TypeConverter
{
    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(char);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a byte value from the OPC server into a .NET character.
    /// S7 CHAR is treated as an 8-bit ASCII-compatible character.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="byte"/>.</param>
    /// <returns>The corresponding <see cref="char"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null ? null : opcValue is byte byteValue ? Convert.ToChar(byteValue) : (object?)null;
    }

    /// <summary>
    /// Converts a .NET character back into a byte for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="char"/> (or <see cref="byte"/>) from the user application.</param>
    /// <returns>The corresponding <see cref="byte"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        return userValue is null
            ? null
            : userValue switch
            {
                char charValue => Convert.ToByte(charValue),
                byte byteValue => byteValue,
                _ => null,
            };
    }

    #endregion Public Methods
}