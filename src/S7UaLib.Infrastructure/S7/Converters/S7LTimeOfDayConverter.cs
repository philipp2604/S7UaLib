using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 LTIME_OF_DAY (LTOD) format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 LTOD type is a 64-bit unsigned integer representing the number of nanoseconds elapsed since midnight.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7LTimeOfDayConverter"/> class.
/// </remarks>
public class S7LTimeOfDayConverter : IS7TypeConverter
{
    #region Private Fields

    private readonly TimeSpan _oneDay = TimeSpan.FromDays(1);

    #endregion Private Fields

    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a 64-bit unsigned integer (nanoseconds since midnight) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ulong"/>.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null
            ? null
            : opcValue is ulong nanosecondsSinceMidnight ? TimeSpan.FromTicks((long)(nanosecondsSinceMidnight / 100)) : (object?)null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 64-bit unsigned integer (nanoseconds since midnight) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application, representing the time of day.</param>
    /// <returns>A <see cref="ulong"/> representing nanoseconds since midnight, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        return userValue is null
            ? null
            : userValue is TimeSpan timeOfDay
            ? timeOfDay < TimeSpan.Zero || timeOfDay >= _oneDay ? null : (ulong)(timeOfDay.Ticks * 100)
            : (object?)null;
    }

    #endregion Public Methods
}