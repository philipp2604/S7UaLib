using S7UaLib.Core.Enums;
using S7UaLib.Core.S7.Udt;
using S7UaLib.Core.Ua;
using System.Text.Json.Serialization;

namespace S7UaLib.Core.S7.Structure;

/// <summary>
/// Defines a common interface for S7 variables that can be accessed via OPC UA.
/// </summary>
public interface IS7Variable : IUaNode
{
    #region Public Properties

    /// <summary>
    /// Gets the full symbolic path of the variable within the PLC (e.g., "DB100.Settings.MotorSpeed").
    /// </summary>
    public string? FullPath { get; init; }

    /// <summary>
    /// Gets the variable's value after it has been converted to its user-friendly .NET type.
    /// For example, an S7 DATE_AND_TIME byte array becomes a .NET DateTime object.
    /// </summary>
    [JsonIgnore]
    public object? Value { get; init; }

    /// <summary>
    /// Gets the raw value as it was received from the OPC UA server, before any custom conversion.
    /// </summary>
    [JsonIgnore]
    public object? RawOpcValue { get; init; }

    /// <summary>
    /// Gets the underlying S7-specific data type enumeration for this variable.
    /// </summary>
    public S7DataType S7Type { get; init; }

    /// <summary>
    /// Gets or sets the UDT (User-Defined Type) name if this variable is a struct.
    /// </summary>
    public string? UdtTypeName { get; init; }

    /// <summary>
    /// Gets the target .NET <see cref="Type"/> that this variable's value is converted to.
    /// </summary>
    public Type? SystemType { get; init; }

    /// <summary>
    /// Gets the OPC UA <see cref="Enums.StatusCode"/> indicating the quality of the variable's value (e.g., Good, Bad, Uncertain).
    /// </summary>
    public StatusCode StatusCode { get; init; }

    /// <summary>
    /// Gets a list of member variables if this variable represents a struct (UDT).
    /// The list is empty for simple data types.
    /// </summary>
    public IReadOnlyList<IS7Variable> StructMembers { get; init; }

    /// <summary>
    /// Gets a value indicating whether this variable should be actively monitored via an OPC UA subscription.
    /// This setting is persisted in the configuration.
    /// </summary>
    public bool IsSubscribed { get; init; }

    /// <summary>
    /// Gets the requested sampling interval in milliseconds for the subscription.
    /// This setting is persisted in the configuration.
    /// </summary>
    public uint SamplingInterval { get; init; }

    /// <summary>
    /// Gets the UDT definition if this variable represents a User-Defined Type.
    /// Null for simple data types.
    /// </summary>
    public UdtDefinition? UdtDefinition { get; init; }

    /// <summary>
    /// Gets a value indicating whether this variable is a User-Defined Type (UDT).
    /// </summary>
    public bool IsUdt => S7Type == S7DataType.UDT && !string.IsNullOrEmpty(UdtTypeName);

    #endregion Public Properties
}