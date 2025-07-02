using Microsoft.Extensions.Logging;
using S7UaLib.S7.Converters.Contracts;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Handles the conversion between the S7 BCD (Binary Coded Decimal) counter format
/// and a standard .NET <see cref="ushort"/>. S7 counters are represented as a
/// 3-digit BCD value (0-999) within a 16-bit word. The upper 4 bits are ignored.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7CounterConverter"/> class.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
internal class S7CounterConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;

    private const ushort _maxValue = 999;
    private const ushort _valueMask = 0x0FFF;

    /// <inheritdoc/>
    public Type TargetType => typeof(ushort);

    /// <summary>
    /// Converts a ushort value in 3-digit BCD format from the OPC server into a standard .NET <see cref="ushort"/>.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a <see cref="ushort"/> containing BCD data.</param>
    /// <returns>The corresponding decimal <see cref="ushort"/>, or <c>null</c> if the input is invalid.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is not ushort bcdValue)
        {
            _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'System.UInt16'.", opcValue.GetType().FullName);
            return null;
        }

        try
        {
            // Mask the value to ignore the top 4 bits, as they are not part of the counter value.
            int decimalValue = BcdToDecimal(bcdValue & _valueMask);
            return (ushort)decimalValue;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger?.LogError(ex, "Invalid BCD format for counter value {BcdValue}.", bcdValue);
            return null;
        }
    }

    /// <summary>
    /// Converts a standard .NET <see cref="ushort"/> into its S7 3-digit BCD format for the OPC server.
    /// </summary>
    /// <param name="userValue">The decimal <see cref="ushort"/> from the user application (0-999).</param>
    /// <returns>A <see cref="ushort"/> in BCD format, or <c>null</c> if the input is out of range.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is not ushort decimalValue)
        {
            _logger?.LogError("User value was of type '{ActualType}' but expected 'System.UInt16'.", userValue.GetType().FullName);
            return null;
        }

        if (decimalValue > _maxValue)
        {
            _logger?.LogError("Counter value {DecimalValue} is outside the valid range (0-999).", decimalValue);
            return null;
        }

        return (ushort)DecimalToBcd(decimalValue);
    }

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
        return value < 0 || value > _maxValue
            ? throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0 and 999 for BCD conversion.")
            : (value / 100 << 8) | (value % 100 / 10 << 4) | (value % 10);
    }
}