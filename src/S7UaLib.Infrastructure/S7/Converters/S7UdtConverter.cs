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
    private readonly ILogger<S7UdtConverter>? _logger;

    public S7UdtConverter(ILogger<S7UdtConverter>? logger = null)
    {
        _logger = logger;
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
            // For now, return a simple dictionary representation
            // In a full implementation, this would parse the OPC structure
            // based on the UDT definition
            return new Dictionary<string, object?> { ["_raw"] = opcValue };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to convert UDT from OPC value");
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
            // If value is already a dictionary, attempt to convert it back
            if (userValue is Dictionary<string, object?> dict)
            {
                // Simplified implementation - return raw value if available
                return dict.TryGetValue("_raw", out var raw) ? raw : userValue;
            }

            // For other types, return as-is for now
            return userValue;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to convert UDT to OPC value");
            return userValue; // Return as-is as fallback
        }
    }

    // Keep the advanced conversion methods for internal use when we have variable context
    internal object? ConvertFromOpcValue(object? opcValue, S7Variable variable)
    {
        if (opcValue == null)
            return null;

        if (variable.UdtDefinition == null)
        {
            _logger?.LogWarning("Cannot convert UDT '{UdtName}' - missing UDT definition", variable.UdtTypeName);
            return opcValue; // Return raw value as fallback
        }

        try
        {
            return ConvertStructToDict(opcValue, variable.UdtDefinition, variable.StructMembers);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to convert UDT '{UdtName}' from OPC value", variable.UdtTypeName);
            return opcValue; // Return raw value as fallback
        }
    }

    public object? ConvertToOpcValue(object? value, S7Variable variable)
    {
        if (value == null)
            return null;

        if (variable.UdtDefinition == null)
        {
            _logger?.LogWarning("Cannot convert UDT '{UdtName}' - missing UDT definition", variable.UdtTypeName);
            return value; // Return as-is as fallback
        }

        try
        {
            // If value is already a dictionary, convert it back to OPC structure
            if (value is Dictionary<string, object?> dict)
            {
                return ConvertDictToStruct(dict, variable.UdtDefinition, variable.StructMembers);
            }

            // For other types, attempt to extract properties/fields
            return ConvertObjectToStruct(value, variable.UdtDefinition, variable.StructMembers);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to convert UDT '{UdtName}' to OPC value", variable.UdtTypeName);
            return value; // Return as-is as fallback
        }
    }

    private Dictionary<string, object?> ConvertStructToDict(object opcValue, UdtDefinition udtDefinition, IReadOnlyList<IS7Variable> structMembers)
    {
        var result = new Dictionary<string, object?>();

        // Use struct members if available (from discovery), otherwise fall back to UDT definition
        if (structMembers.Any())
        {
            foreach (var member in structMembers)
            {
                if (member.DisplayName != null)
                {
                    // For nested structures, recursively convert
                    if (member.IsUdt && member.UdtDefinition != null)
                    {
                        result[member.DisplayName] = ConvertStructToDict(member.RawOpcValue, member.UdtDefinition, member.StructMembers);
                    }
                    else
                    {
                        result[member.DisplayName] = member.Value ?? member.RawOpcValue;
                    }
                }
            }
        }
        else
        {
            // Fallback: try to extract from raw OPC value using UDT definition
            // This is a simplified implementation - in reality, we'd need to parse the OPC structure
            _logger?.LogDebug("Using fallback conversion for UDT '{UdtName}' - struct members not available", udtDefinition.Name);

            // For now, just return the raw value wrapped in a dictionary
            result["_raw"] = opcValue;
        }

        return result;
    }

    private object ConvertDictToStruct(Dictionary<string, object?> dict, UdtDefinition udtDefinition, IReadOnlyList<IS7Variable> structMembers)
    {
        // This is a complex operation that would need to reconstruct the OPC structure
        // For now, return a simplified implementation

        // In a full implementation, we'd need to:
        // 1. Create an OPC structure based on the UDT definition
        // 2. Map dictionary values to the correct positions
        // 3. Handle nested UDTs recursively

        _logger?.LogDebug("Converting dictionary back to OPC structure for UDT '{UdtName}'", udtDefinition.Name);

        // Placeholder implementation - return the dictionary for now
        // The actual OPC structure creation would depend on the OPC UA client implementation
        return dict;
    }

    private object ConvertObjectToStruct(object value, UdtDefinition udtDefinition, IReadOnlyList<IS7Variable> structMembers)
    {
        // Convert arbitrary objects to OPC structure by reflection
        var valueType = value.GetType();
        var dict = new Dictionary<string, object?>();

        // Extract properties
        foreach (var property in valueType.GetProperties())
        {
            if (property.CanRead)
            {
                dict[property.Name] = property.GetValue(value);
            }
        }

        // Extract fields
        foreach (var field in valueType.GetFields())
        {
            dict[field.Name] = field.GetValue(value);
        }

        return ConvertDictToStruct(dict, udtDefinition, structMembers);
    }
}