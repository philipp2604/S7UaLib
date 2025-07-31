namespace S7UaLib.Core.S7.Udt;

/// <summary>
/// Represents a complete User-Defined Type (UDT) definition discovered from the PLC.
/// Contains all member information needed for type conversion and validation.
/// </summary>
public record UdtDefinition
{
    /// <summary>
    /// Gets the name of the UDT as defined in the PLC.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the optional description of the UDT if available from the PLC.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the complete list of members that make up this UDT.
    /// </summary>
    public IReadOnlyList<UdtMemberDefinition> Members { get; init; } = [];

    /// <summary>
    /// Gets the total size of the UDT in bytes as reported by the OPC UA server.
    /// </summary>
    public int SizeInBytes { get; init; }

    /// <summary>
    /// Gets the timestamp when this UDT definition was discovered.
    /// Used for cache management and debugging.
    /// </summary>
    public DateTime DiscoveredAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets the OPC UA NodeId of the DataType node that defines this UDT.
    /// Used for efficient re-discovery and validation.
    /// </summary>
    public string? DataTypeNodeId { get; init; }
}