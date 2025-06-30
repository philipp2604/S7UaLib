using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 LTIME format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 LTIME type is a 64-bit signed integer representing a duration in nanoseconds.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7LTimeConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7LTimeConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    /// <summary>
    /// Converts a 64-bit integer (nanoseconds) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="long"/> representing nanoseconds.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is long nanoseconds)
        {
            return TimeSpan.FromTicks(nanoseconds / 100);
        }

        _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.Int64'.", opcValue.GetType().FullName);
        return null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 64-bit integer (nanoseconds) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application.</param>
    /// <returns>A <see cref="long"/> representing the total duration in nanoseconds, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is TimeSpan timeSpanValue)
        {
            return timeSpanValue.Ticks * 100;
        }

        _logger?.LogError("User value was of type '{ActualType}' but expected 'System.TimeSpan'.", userValue.GetType().FullName);
        return null;
    }
}