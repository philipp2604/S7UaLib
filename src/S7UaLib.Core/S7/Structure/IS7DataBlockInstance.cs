using S7UaLib.Core.Ua;

namespace S7UaLib.Core.S7.Structure;

public interface IS7DataBlockInstance : IUaNode
{
    #region Public Properties

    /// <summary>
    /// Gets the 'Inputs' section of the instance, containing input parameters.
    /// This can be null if the section does not exist.
    /// </summary>
    public IS7InstanceDbSection? Inputs { get; init; }

    /// <summary>
    /// Gets the 'Outputs' section of the instance, containing output parameters.
    /// This can be null if the section does not exist.
    /// </summary>
    public IS7InstanceDbSection? Outputs { get; init; }

    /// <summary>
    /// Gets the 'InOuts' section of the instance, containing parameters used for both input and output.
    /// This can be null if the section does not exist.
    /// </summary>
    public IS7InstanceDbSection? InOuts { get; init; }

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
    /// Gets the full path of the structure element in the OPC UA address space.
    /// </summary>
    public string? FullPath { get; init; }

    #endregion Public Properties
}