using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 TIME_OF_DAY (TOD) format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 TOD type is a 32-bit unsigned integer representing the number of milliseconds elapsed since midnight.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7TimeOfDayConverter"/> class.
/// </remarks>
public class S7TimeOfDayConverter : IS7TypeConverter
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
    /// Converts a 32-bit unsigned integer (milliseconds since midnight) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="uint"/>.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        return opcValue is null
            ? null
            : opcValue is uint millisecondsSinceMidnight ? TimeSpan.FromMilliseconds(millisecondsSinceMidnight) : (object?)null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 32-bit unsigned integer (milliseconds since midnight) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application, representing the time of day.</param>
    /// <returns>A <see cref="uint"/> representing milliseconds since midnight, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        return userValue is null
            ? null
            : userValue is TimeSpan timeOfDay
            ? timeOfDay < TimeSpan.Zero || timeOfDay >= _oneDay ? null : Convert.ToUInt32(timeOfDay.TotalMilliseconds)
            : (object?)null;
    }

    #endregion Public Methods
}