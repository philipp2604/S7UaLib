using Opc.Ua;
using S7UaLib.S7.Types;
using S7UaLib.UA;
using System.Text.Json.Serialization;

namespace S7UaLib.S7.Structure.Contracts;

/// <summary>
/// Defines a common interface for S7 variables that can be accessed via OPC UA.
/// </summary>
public interface IS7Variable : IUaElement
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
    /// Gets the target .NET <see cref="Type"/> that this variable's value is converted to.
    /// </summary>
    public Type? SystemType { get; init; }

    /// <summary>
    /// Gets the OPC UA <see cref="Opc.Ua.StatusCode"/> indicating the quality of the variable's value (e.g., Good, Bad, Uncertain).
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

    #endregion Public Properties
}