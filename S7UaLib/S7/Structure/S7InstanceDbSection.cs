using Opc.Ua;
using S7UaLib.UA;

namespace S7UaLib.S7.Structure;

/// <summary>
/// Represents a specific section within an S7 Instance Data Block, such as 'Input', 'Output', 'InOut', or 'Static'.
/// It contains the variables and any nested FB instances belonging to that section.
/// </summary>
internal record S7InstanceDbSection : IUaElement
{
    /// <inheritdoc cref="IUaElement.NodeId" />
    public NodeId? NodeId { get; init; }

    /// <inheritdoc cref="IUaElement.DisplayName" />
    public string? DisplayName { get; init; }

    /// <summary>
    /// Gets the list of simple variables (e.g., BOOL, INT, REAL) contained within this section.
    /// </summary>
    public IReadOnlyList<S7Variable> Variables { get; init; } = [];

    /// <summary>
    /// Gets the list of nested function block instances declared within this section (typically the 'Static' section).
    /// </summary>
    public IReadOnlyList<S7DataBlockInstance> NestedInstances { get; init; } = [];

    /// <summary>
    /// Gets the full symbolic path of the instance within the PLC, if available.
    /// </summary>
    public string? FullPath { get; init; } = null;
}
