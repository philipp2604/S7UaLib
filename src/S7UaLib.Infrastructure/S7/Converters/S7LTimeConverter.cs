using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 LTIME format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 LTIME type is a 64-bit signed integer representing a duration in nanoseconds.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7LTimeConverter"/> class.
/// </remarks>
public class S7LTimeConverter : IS7TypeConverter
{
    #region Private Fields

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    #endregion Private Fields

    #region Public Methods

    /// <summary>
    /// Converts a 64-bit integer (nanoseconds) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="long"/> representing nanoseconds.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null ? null : opcValue is long nanoseconds ? TimeSpan.FromTicks(nanoseconds / 100) : (object?)null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 64-bit integer (nanoseconds) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application.</param>
    /// <returns>A <see cref="long"/> representing the total duration in nanoseconds, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        return userValue is null ? null : userValue is TimeSpan timeSpanValue ? timeSpanValue.Ticks * 100 : (object?)null;
    }

    #endregion Public Methods
}