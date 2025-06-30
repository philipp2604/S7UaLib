using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 TIME format and the standard .NET <see cref="TimeSpan"/>.
/// The S7 TIME type is a 32-bit signed integer representing a duration in milliseconds.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7TimeConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7TimeConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    /// <summary>
    /// Converts a 32-bit integer (milliseconds) from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="int"/>.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is int milliseconds)
        {
            return TimeSpan.FromMilliseconds(milliseconds);
        }

        _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.Int32'.", opcValue.GetType().FullName);
        return null;
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into a 32-bit integer (milliseconds) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application.</param>
    /// <returns>An <see cref="int"/> representing the total duration in milliseconds, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is TimeSpan timeSpanValue)
        {
            try
            {
                return Convert.ToInt32(timeSpanValue.TotalMilliseconds);
            }
            catch (OverflowException ex)
            {
                _logger?.LogError(ex, "Cannot convert TimeSpan {TimeSpanValue}. The value is outside the valid range for a 32-bit integer.", timeSpanValue);
                return null;
            }
        }

        _logger?.LogError("User value was of type '{ActualType}' but expected 'System.TimeSpan'.", userValue.GetType().FullName);
        return null;
    }
}