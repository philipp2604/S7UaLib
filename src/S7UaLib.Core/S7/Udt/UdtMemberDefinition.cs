using S7UaLib.Core.Enums;

namespace S7UaLib.Core.S7.Udt;

/// <summary>
/// Represents a single member within a User-Defined Type (UDT).
/// Contains all information needed to access and convert the member value.
/// </summary>
public record UdtMemberDefinition
{
    /// <summary>
    /// Gets the name of the UDT member as defined in the PLC.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets the S7 data type of this member.
    /// </summary>
    public S7DataType S7Type { get; init; }

    /// <summary>
    /// Gets the UDT type name if this member is itself a UDT (nested UDTs).
    /// Null for basic data types.
    /// </summary>
    public string? UdtTypeName { get; init; }

    /// <summary>
    /// Gets the byte offset of this member within the UDT structure.
    /// Used for efficient data access and validation.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets the array length if this member is an array.
    /// Value of 1 indicates a single value (not an array).
    /// </summary>
    public int ArrayLength { get; init; } = 1;

    /// <summary>
    /// Gets the optional description of this member if available from the PLC.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the size in bytes of this member.
    /// For arrays, this represents the total size of all array elements.
    /// </summary>
    public int SizeInBytes { get; init; }

    /// <summary>
    /// Gets a value indicating whether this member is an array.
    /// </summary>
    public bool IsArray => ArrayLength > 1;

    /// <summary>
    /// Gets a value indicating whether this member is a nested UDT.
    /// </summary>
    public bool IsNestedUdt => S7Type == S7DataType.UDT && !string.IsNullOrEmpty(UdtTypeName);
}