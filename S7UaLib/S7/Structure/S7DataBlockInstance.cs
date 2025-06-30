using Opc.Ua;
using S7UaLib.UA;

namespace S7UaLib.S7.Structure;

/// <summary>
/// Represents an S7 instance data block within the PLC's memory structure.
/// </summary>
internal record S7DataBlockInstance : IUaElement
{
    /// <inheritdoc cref="IUaElement.NodeId" />
    public NodeId? NodeId { get; init; }

    /// <inheritdoc cref="IUaElement.DisplayName" />
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the 'Input' section of the instance, containing input parameters.
    /// This can be null if the section does not exist.
    /// </summary>
    public S7InstanceDbSection? Input { get; init; }

    /// <summary>
    /// Gets the 'Output' section of the instance, containing output parameters.
    /// This can be null if the section does not exist.
    /// </summary>
    public S7InstanceDbSection? Output { get; init; }

    /// <summary>
    /// Gets the 'InOut' section of the instance, containing parameters used for both input and output.
    /// This can be null if the section does not exist.
    /// </summary>
    public S7InstanceDbSection? InOut { get; init; }

    /// <summary>
    /// Gets the 'Static' section of the instance, containing static or internal variables.
    /// This can be null if the section does not exist.
    /// </summary>
    public S7InstanceDbSection? Static { get; init; }

    /// <summary>
    /// Gets the list of other function block instances that are nested within this instance's static data.
    /// This enables the representation of recursive or complex data structures.
    /// </summary>
    public IReadOnlyList<S7DataBlockInstance> NestedInstances { get; init; } = [];

    /// <summary>
    /// Gets the full symbolic path of the instance within the PLC, if available.
    /// </summary>
    public string? FullPath { get; init; } = null;
}