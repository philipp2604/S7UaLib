using S7UaLib.UA;

namespace S7UaLib.S7.Structure.Contracts;

/// <summary>
/// Defnes a common interface for S7 data block instances that can be accessed via OPC UA.
/// </summary>
internal interface IS7DataBlockInstance : IUaElement
{
    /// <summary>
    /// Gets the 'Input' section of the instance, containing input parameters.
    /// This can be null if the section does not exist.
    /// </summary>
    public IS7InstanceDbSection? Input { get; init; }

    /// <summary>
    /// Gets the 'Output' section of the instance, containing output parameters.
    /// This can be null if the section does not exist.
    /// </summary>
    public IS7InstanceDbSection? Output { get; init; }

    /// <summary>
    /// Gets the 'InOut' section of the instance, containing parameters used for both input and output.
    /// This can be null if the section does not exist.
    /// </summary>
    public IS7InstanceDbSection? InOut { get; init; }

    /// <summary>
    /// Gets the 'Static' section of the instance, containing static or internal variables.
    /// This can be null if the section does not exist.
    /// </summary>
    public IS7InstanceDbSection? Static { get; init; }

    /// <summary>
    /// Gets the list of other function block instances that are nested within this instance's static data.
    /// This enables the representation of recursive or complex data structures.
    /// </summary>
    public IReadOnlyList<IS7DataBlockInstance> NestedInstances { get; init; }

    /// <summary>
    /// Gets the full symbolic path of the instance within the PLC, if available.
    /// </summary>
    public string? FullPath { get; init; }
}