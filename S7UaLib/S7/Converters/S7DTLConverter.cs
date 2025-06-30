using Microsoft.Extensions.Logging;
using Opc.Ua;
using S7UaLib.S7.Converters.Contracts;
using System.Buffers.Binary;

namespace S7UaLib.S7.Converters;

/// <summary>
/// Converts between the S7 DTL (DATE_AND_TIME_LONG) format (a 12-byte binary-encoded array)
/// and the standard .NET <see cref="DateTime"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="S7DTLConverter"/> class.
/// The conversion assumes the byte order from the PLC is little-endian, which is standard for S7 controllers.
/// </remarks>
/// <param name="logger">An optional logger for diagnostics.</param>
public class S7DTLConverter(ILogger? logger = null) : IS7TypeConverter
{
    private readonly ILogger? _logger = logger;
    private const string _typeId = "nsu=http://www.siemens.com/simatic-s7-opcua;s=TE_DTL";

    /// <inheritdoc/>
    public Type TargetType => typeof(DateTime);

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
            _logger?.LogError("Value from OPC was of type '{ActualType}' but expected 'Opc.Ua.ExtensionObject'.", opcValue.GetType().FullName);
            return null;
        }

        if (eo.TypeId.ToString() != _typeId)
        {
            _logger?.LogError("Datatype from OPC was of type '{ActualType}' but expected '{ExpectedType}'.", eo.TypeId.ToString(), _typeId);
            return null;
        }

        if (eo.Body is not byte[] dtlBytes)
        {
            _logger?.LogError("Value from OPC EO body was of type '{ActualType}' but expected 'System.Byte[]'.", eo.Body?.GetType().FullName ?? "null");
            return null;
        }

        if (dtlBytes.Length != 12)
        {
            _logger?.LogError("Byte array from OPC for DTL must have a length of 12, but was {Length}.", dtlBytes.Length);
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
            _logger?.LogError("Invalid date or time component in DTL byte array: Year={Year}, Month={Month}, Day={Day}, Hour={Hour}, Minute={Minute}, Second={Second}.", year, month, day, hour, minute, second);
            return null;
        }

        try
        {
            var baseDateTime = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
            long subSecondTicks = nanoseconds / 100; // Convert ns to .NET ticks (1 tick = 100 ns)

            return baseDateTime.AddTicks(subSecondTicks);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to convert byte array to S7 DTL. Input bytes: {Bytes}", string.Join(", ", dtlBytes.Select(b => $"0x{b:X2}")));
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
            _logger?.LogError("User value was of type '{ActualType}' but expected 'System.DateTime'.", userValue.GetType().FullName);
            return null;
        }

        if (dateTimeValue.Year < 1970 || dateTimeValue.Year > 2262)
        {
            _logger?.LogWarning("The year {Year} is outside the typical S7 DTL range (1970-2262), which might not be supported by all PLC firmware versions.", dateTimeValue.Year);
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
        uint nanoseconds = (uint)(millis * 1_000_000 + subMilliTicks * 100);

        BinaryPrimitives.WriteUInt32LittleEndian(dtlBytes.AsSpan(8, 4), nanoseconds);

        return new ExtensionObject(new ExpandedNodeId(_typeId), dtlBytes);
    }
}