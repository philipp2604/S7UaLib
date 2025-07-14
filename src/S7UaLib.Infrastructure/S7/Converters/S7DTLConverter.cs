using Opc.Ua;
using System.Buffers.Binary;
using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Converts between the S7 DTL (DATE_AND_TIME_LONG) format (a 12-byte binary-encoded array)
/// and the standard .NET <see cref="DateTime"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7DTLConverter"/> class.
/// The conversion assumes the byte order from the PLC is little-endian, which is standard for S7 controllers.
/// </remarks>
public class S7DTLConverter : IS7TypeConverter
{
    #region Private Fields

    private const string _typeId = "nsu=http://www.siemens.com/simatic-s7-opcua;s=TE_DTL";

    #endregion Private Fields

    #region Public Properties

    /// <inheritdoc/>
    public Type TargetType => typeof(DateTime);

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a 12-byte array from the OPC server into a .NET <see cref="DateTime"/> object.
    /// </summary>
    /// <param name="opcValue">The object from the OPC server, expected to be a 12-byte array in S7 DTL format.</param>
    /// <returns>The corresponding <see cref="DateTime"/> object, or <c>null</c> if the input is null.</returns>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue is null)
        {
            return null;
        }

        if (opcValue is not ExtensionObject eo)
        {
            return null;
        }

        if (eo.TypeId.ToString() != _typeId)
        {
            return null;
        }

        if (eo.Body is not byte[] dtlBytes)
        {
            return null;
        }

        if (dtlBytes.Length != 12)
        {
            return null;
        }

        ushort year = BinaryPrimitives.ReadUInt16LittleEndian(dtlBytes.AsSpan(0, 2));
        int month = dtlBytes[2];
        int day = dtlBytes[3];
        int hour = dtlBytes[5];
        int minute = dtlBytes[6];
        int second = dtlBytes[7];
        uint nanoseconds = BinaryPrimitives.ReadUInt32LittleEndian(dtlBytes.AsSpan(8, 4));

        if (month is < 1 or > 12 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }

        try
        {
            var baseDateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            long subSecondTicks = nanoseconds / 100; // Convert ns to .NET ticks (1 tick = 100 ns)

            return baseDateTime.AddTicks(subSecondTicks);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Converts a .NET <see cref="DateTime"/> object into an <see cref="ExtensionObject"/> with a 12-byte body for the OPC server.
    /// </summary>
    /// <param name="userValue">The <see cref="DateTime"/> object from the user application.</param>
    /// <returns>An <see cref="ExtensionObject"/> wrapping the 12-byte DTL array, or <c>null</c> if the input is null.</returns>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue is null)
        {
            return null;
        }

        if (userValue is not DateTime dateTimeValue)
        {
            return null;
        }

        byte[] dtlBytes = new byte[12];

        // Year (UINT)
        BinaryPrimitives.WriteUInt16LittleEndian(dtlBytes.AsSpan(0, 2), (ushort)dateTimeValue.Year);
        // Month (USINT)
        dtlBytes[2] = (byte)dateTimeValue.Month;
        // Day (USINT)
        dtlBytes[3] = (byte)dateTimeValue.Day;
        // Day of week (USINT, 1=Sun, 7=Sat)
        dtlBytes[4] = (byte)(((int)dateTimeValue.DayOfWeek + 1) % 8);
        // Hour, Minute, Second (USINT)
        dtlBytes[5] = (byte)dateTimeValue.Hour;
        dtlBytes[6] = (byte)dateTimeValue.Minute;
        dtlBytes[7] = (byte)dateTimeValue.Second;

        // Nanoseconds (sub-second precision)
        int millis = dateTimeValue.Millisecond;
        long subMilliTicks = dateTimeValue.Ticks % TimeSpan.TicksPerMillisecond; // TicksPerMillisecond = 10000
        uint nanoseconds = (uint)((millis * 1_000_000) + (subMilliTicks * 100));

        BinaryPrimitives.WriteUInt32LittleEndian(dtlBytes.AsSpan(8, 4), nanoseconds);

        return new ExtensionObject(new ExpandedNodeId(_typeId), dtlBytes);
    }

    #endregion Public Methods
}