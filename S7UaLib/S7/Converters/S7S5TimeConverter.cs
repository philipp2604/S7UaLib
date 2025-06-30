using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between the legacy S7 S5TIME format and the standard .NET <see cref="TimeSpan"/>.
/// S5TIME is a 16-bit value where the two most significant bits define a time base (10ms, 100ms, 1s, 10s)
/// and the remaining 12 bits encode a 3-digit BCD (Binary Coded Decimal) value from 0 to 999.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7S5TimeConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7S5TimeConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;
    private readonly TimeSpan _maxS5Time = TimeSpan.FromSeconds(9990);

    private const ushort _timeBaseMask = 0x3000;
    private const ushort _valueMask = 0x0FFF;

    /// <inheritdoc/>
    public Type TargetType => typeof(TimeSpan);

    /// <summary>
    /// Converts a ushort value in S5TIME format from the OPC server into a .NET <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ushort"/>.</param>
    /// <returns>The corresponding <see cref="TimeSpan"/>, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is not ushort s5TimeValue)
        {
            _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.UInt16'.", opcValue.GetType().FullName);
            return null;
        }

        try
        {
            int timeBaseCode = (s5TimeValue & _timeBaseMask) >> 12;
            long multiplierMs = GetMultiplierMs(timeBaseCode);

            int bcdValue = s5TimeValue & _valueMask;
            int decimalValue = BcdToDecimal(bcdValue);

            return TimeSpan.FromMilliseconds(decimalValue * multiplierMs);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger?.LogError(ex, "Invalid S5TIME format for value {S5TimeValue}.", s5TimeValue);
            return null;
        }
    }

    /// <summary>
    /// Converts a .NET <see cref="TimeSpan"/> back into its S5TIME format (ushort) for the OPC server.
    /// The converter selects the smallest possible time base to maintain the highest precision.
    /// </summary>
    /// <param name="userValue">The <see cref="TimeSpan"/> from the user application.</param>
    /// <returns>A <see cref="ushort"/> in S5TIME format, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is not TimeSpan timeSpanValue)
        {
            _logger?.LogError("User value was of type '{ActualType}' but expected 'System.TimeSpan'.", userValue.GetType().FullName);
            return null;
        }

        if (timeSpanValue < TimeSpan.Zero || timeSpanValue > _maxS5Time)
        {
            _logger?.LogError("TimeSpan {timeSpanValue} is outside the valid range for S5TIME.", timeSpanValue);
            return null;
        }

        long totalMs = (long)timeSpanValue.TotalMilliseconds;
        ushort timeBaseCode;
        int timeValue;

        if (totalMs <= 9990 && totalMs % 10 == 0)
        {
            timeBaseCode = 0b00;
            timeValue = (int)(totalMs / 10);
        }
        else if (totalMs <= 99900 && totalMs % 100 == 0)
        {
            timeBaseCode = 0b01;
            timeValue = (int)(totalMs / 100);
        }
        else if (totalMs <= 999000 && totalMs % 1000 == 0)
        {
            timeBaseCode = 0b10;
            timeValue = (int)(totalMs / 1000);
        }
        else
        {
            timeBaseCode = 0b11;
            timeValue = (int)Math.Round(totalMs / 10000.0);
        }

        ushort bcdValue = (ushort)DecimalToBcd(timeValue);
        return (ushort)(timeBaseCode << 12 | bcdValue);
    }

    private static long GetMultiplierMs(int timeBaseCode) => timeBaseCode switch
    {
        0b00 => 10,
        0b01 => 100,
        0b10 => 1000,
        0b11 => 10000,
        _ => throw new ArgumentOutOfRangeException(nameof(timeBaseCode), "Invalid S5TIME time base code.")
    };

    private static int BcdToDecimal(int bcd)
    {
        int hundreds = (bcd & 0x0F00) >> 8;
        int tens = (bcd & 0x00F0) >> 4;
        int ones = bcd & 0x000F;

        return hundreds > 9 || tens > 9 || ones > 9
            ? throw new ArgumentOutOfRangeException(nameof(bcd), $"Value 0x{bcd:X3} contains an invalid BCD digit.")
            : (hundreds * 100) + (tens * 10) + ones;
    }

    private static int DecimalToBcd(int value)
    {
        return value < 0 || value > 999
            ? throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0 and 999 for BCD conversion.")
            : value / 100 << 8 | value % 100 / 10 << 4 | value % 10;
    }
}