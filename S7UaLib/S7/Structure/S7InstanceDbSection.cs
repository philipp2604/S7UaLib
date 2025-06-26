using Opc.Ua;
using S7UaLib.S7.Structure.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.S7.Structure;

/// <summary>
/// Represents a specific section within an S7 Instance Data Block, such as 'Input', 'Output', 'InOut', or 'Static'.
/// It contains the variables and any nested FB instances belonging to that section.
/// </summary>
internal record S7InstanceDbSection : IS7InstanceDbSection
{
    /// <inheritdoc cref="IS7InstanceDbSection.NodeId" />
    public NodeId? NodeId { get; init; }

    /// <inheritdoc cref="IS7InstanceDbSection.DisplayName" />
    public string? DisplayName { get; init; }

    /// <inheritdoc cref="IS7InstanceDbSection.Variables" />/>
    public IReadOnlyList<IS7Variable> Variables { get; init; } = [];

    /// <inheritdoc cref="IS7InstanceDbSection.NestedInstances" />
    public IReadOnlyList<IS7DataBlockInstance> NestedInstances { get; init; } = [];

    /// <inheritdoc cref="IS7InstanceDbSection.FullPath" />
    public string? FullPath { get; init; } = null;
}
