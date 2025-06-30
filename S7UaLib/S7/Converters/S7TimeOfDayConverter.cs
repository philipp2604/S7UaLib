using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 TIME_OF_DAY (TOD) format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 TOD type is a 32-bit unsigned integer representing the number of milliseconds elapsed since midnight.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7TimeOfDayConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7TimeOfDayConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;
    private readonly TimeSpan _oneDay = TimeSpan.FromDays(1);

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    /// <summary>
    /// Converts a 32-bit unsigned integer (milliseconds since midnight) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="uint"/>.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is uint millisecondsSinceMidnight)
        {
            return TimeSpan.FromMilliseconds(millisecondsSinceMidnight);
        }

        _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.UInt32'.", opcValue.GetType().FullName);
        return null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 32-bit unsigned integer (milliseconds since midnight) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application, representing the time of day.</param>
    /// <returns>A <see cref="uint"/> representing milliseconds since midnight, or <c>null</c> if the input is null.</returns>
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
                _logger?.LogError("TimeSpan must be a positive value less than 24 hours. Provided value was {ActualValue}.", timeOfDay);
                return null;
            }

            return Convert.ToUInt32(timeOfDay.TotalMilliseconds);
        }

        _logger?.LogError("User value was of type '{ActualType}' but expected 'System.TimeSpan'.", userValue.GetType().FullName);
        return null;
    }
}