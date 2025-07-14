using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Converts between the S7 DATE_AND_TIME format (an 8-byte BCD-encoded array)
/// and the standard .NET <see cref="DateTime"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7DateAndTimeConverter"/> class.
/// </remarks>
public class S7DateAndTimeConverter : IS7TypeConverter
{
    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(DateTime);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts an 8-byte array from the OPC server into a .NET <see cref="DateTime"/> object.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be an 8-byte array in S7 DATE_AND_TIME format.</param>
    /// <returns>The corresponding <see cref="DateTime"/> object, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is not byte[] dtBytes)
        {
            return null;
        }

        if (dtBytes.Length != 8)
        {
            return null;
        }

        int year = BcdToDecimal(dtBytes[0]);
        int month = BcdToDecimal(dtBytes[1]);
        int day = BcdToDecimal(dtBytes[2]);
        int hour = BcdToDecimal(dtBytes[3]);
        int minute = BcdToDecimal(dtBytes[4]);
        int second = BcdToDecimal(dtBytes[5]);
        int msHundredAndTen = BcdToDecimal(dtBytes[6]);
        int msOne = dtBytes[7] >> 4;
        int millisecond = (msHundredAndTen * 10) + msOne;

        year += year < 90 ? 2000 : 1900;

        return new DateTime(year, month, day, hour, minute, second, millisecond);
    }

    /// <summary>
    /// Converts a .NET <see cref="DateTime"/> object into an 8-byte array for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="DateTime"/> object from the user application.</param>
    /// <returns>An 8-byte array in S7 DATE_AND_TIME format, or <c>null</c> if the input is null.</returns>
    /// <exception cref="ArgumentException">Thrown if the input is not a <see cref="DateTime"/>.</exception>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is not DateTime dateTimeValue)
        {
            throw new ArgumentException("Input must be of type System.DateTime.", nameof(userValue));
        }

        byte[] result = new byte[8];
        result[0] = DecimalToBcd(dateTimeValue.Year % 100);
        result[1] = DecimalToBcd(dateTimeValue.Month);
        result[2] = DecimalToBcd(dateTimeValue.Day);
        result[3] = DecimalToBcd(dateTimeValue.Hour);
        result[4] = DecimalToBcd(dateTimeValue.Minute);
        result[5] = DecimalToBcd(dateTimeValue.Second);

        int milliseconds = dateTimeValue.Millisecond;
        result[6] = DecimalToBcd(milliseconds / 10);

        int msOnes = milliseconds % 10;
        int dayOfWeek = (int)dateTimeValue.DayOfWeek + 1;
        result[7] = (byte)(msOnes << 4 | dayOfWeek);

        return result;
    }

    #endregion Public Methods

    #region Private Methods

    private static int BcdToDecimal(byte bcd)
    {
        int lowNibble = bcd & 0x0F;
        int highNibble = bcd >> 4;

        return lowNibble > 9 || highNibble > 9
            ? throw new FormatException($"Byte value 0x{bcd:X2} is not a valid BCD format.")
            : (highNibble * 10) + lowNibble;
    }

    private static byte DecimalToBcd(int value)
    {
        return value < 0 || value > 99
            ? throw new ArgumentOutOfRangeException(nameof(value), "Input must be between 0 and 99 for BCD conversion.")
            : (byte)(value / 10 << 4 | value % 10);
    }

    #endregion Private Methods
}