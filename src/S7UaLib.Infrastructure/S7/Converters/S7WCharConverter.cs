using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Handles the conversion between a 16-bit wide character (S7 WCHAR), represented as a <see cref="ushort"/>,
/// and the standard .NET <see cref="char"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7WCharConverter"/> class.
/// </remarks>
public class S7WCharConverter : IS7TypeConverter
{
    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(char);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a 16-bit unsigned integer value from the OPC server into a .NET character.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ushort"/>.</param>
    /// <returns>The corresponding <see cref="char"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null ? null : opcValue is ushort ushortValue ? Convert.ToChar(ushortValue) : (object?)null;
    }

    /// <summary>
    /// Converts a .NET character back into its 16-bit unsigned integer representation for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="char"/> from the user application.</param>
    /// <returns>The corresponding <see cref="ushort"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        return userValue is null ? null : userValue is char charValue ? Convert.ToUInt16(charValue) : (object?)null;
    }

    #endregion Public Methods
}