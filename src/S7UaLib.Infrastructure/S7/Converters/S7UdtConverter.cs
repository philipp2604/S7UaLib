using Microsoft.Extensions.Logging;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.S7.Udt;

namespace S7UaLib.Infrastructure.S7.Converters;

/// <summary>
/// Generic converter for User-Defined Types (UDTs).
/// Converts UDT instances to/from Dictionary&lt;string, object?&gt; for dynamic access.
/// </summary>
internal class S7UdtConverter : IS7TypeConverter
{
    public S7UdtConverter()
    {
    }

    /// <inheritdoc/>
    public Type TargetType => typeof(Dictionary<string, object?>);

    /// <inheritdoc/>
    public object? ConvertFromOpc(object? opcValue)
    {
        if (opcValue == null)
            return null;

        try
        {
            return new Dictionary<string, object?> { ["_raw"] = opcValue };
        }
        catch (Exception)
        {
            return opcValue; // Return raw value as fallback
        }
    }

    /// <inheritdoc/>
    public object? ConvertToOpc(object? userValue)
    {
        if (userValue == null)
            return null;

        try
        {
            return userValue is Dictionary<string, object?> dict ? dict.TryGetValue("_raw", out var raw) ? raw : userValue : userValue;
        }
        catch (Exception)
        {
            return userValue; // Return as-is as fallback
        }
    }
}