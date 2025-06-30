using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 LTIME_OF_DAY (LTOD) format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 LTOD type is a 64-bit unsigned integer representing the number of nanoseconds elapsed since midnight.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7LTimeOfDayConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7LTimeOfDayConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;
    private readonly TimeSpan _oneDay = TimeSpan.FromDays(1);

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    /// <summary>
    /// Converts a 64-bit unsigned integer (nanoseconds since midnight) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ulong"/>.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is ulong nanosecondsSinceMidnight)
        {
            return TimeSpan.FromTicks((long)(nanosecondsSinceMidnight / 100));
        }

        _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.UInt64'.", opcValue.GetType().FullName);
        return null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 64-bit unsigned integer (nanoseconds since midnight) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application, representing the time of day.</param>
    /// <returns>A <see cref="ulong"/> representing nanoseconds since midnight, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is TimeSpan timeOfDay)
        {
            if (timeOfDay < TimeSpan.Zero || timeOfDay >= _oneDay)
            {
                _logger?.LogError("TimeSpan {timeOfDay} is outside the valid range for LTIME_OF_DAY.", timeOfDay);
                return null;
            }

            return (ulong)(timeOfDay.Ticks * 100);
        }

        _logger?.LogError("User value was of type '{ActualType}' but expected 'System.TimeSpan'.", userValue.GetType().FullName);
        return null;
    }
}