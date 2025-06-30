using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 DATE format and the standard .NET <see cref="DateTime"/>.
/// The S7 DATE type is a 16-bit unsigned integer representing the number of days since the epoch date of 1990-01-01.
/// The time component of a <see cref="DateTime"/> object is ignored during conversion to OPC and will be midnight (00:00:00) when converting from OPC.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7DateConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7DateConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;
    private readonly DateTime _s7EpochDate = new(1990, 1, 1);
    private readonly DateTime _s7MaxDate = new(2099, 12, 31);

    /// <inheritdoc/>
    public Type TargetType => typeof(DateTime);

    /// <summary>
    /// Converts a ushort value from the OPC server (days since 1990-01-01) into a .NET <see cref="DateTime"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ushort"/>.</param>
    /// <returns>The corresponding <see cref="DateTime"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is ushort daysSinceEpoch)
        {
            return _s7EpochDate.AddDays(daysSinceEpoch);
        }

        _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.UInt16'.", opcValue.GetType().FullName);
        return null;
    }

    /// <summary>
    /// Converts a .NET <see cref="DateTime"/> back into a ushort (days since 1990-01-01) for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="DateTime"/> from the user application. The time component is ignored.</param>
    /// <returns>A <see cref="ushort"/> representing the number of days since the S7 epoch, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is DateTime dateTimeValue)
        {
            if (dateTimeValue.Date < _s7EpochDate || dateTimeValue.Date > _s7MaxDate)
            {
                _logger?.LogError("Date is outside the valid S7 range.");
                return null;
            }

            TimeSpan difference = dateTimeValue.Date - _s7EpochDate;
            return (ushort)difference.TotalDays;
        }

        _logger?.LogError("User value was of type '{ActualType}' but expected 'System.DateTime'.", userValue.GetType().FullName);
        return null;
    }
}