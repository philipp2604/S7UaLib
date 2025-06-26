using Opc.Ua;
using S7UaLib.S7.Structure.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.S7.Structure;

/// <summary>
/// Represents an S7 instance data block within the PLC's memory structure.
/// </summary>
internal record S7DataBlockInstance : IS7DataBlockInstance
{
    /// <inheritdoc cref="IS7DataBlockInstance.NodeId" />
    public NodeId? NodeId { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.DisplayName" />
    public string? DisplayName { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.FullPath" />
    public IS7InstanceDbSection? Input { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.Output" />
    public IS7InstanceDbSection? Output { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.InOut" />
    public IS7InstanceDbSection? InOut { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.Static" />
    public IS7InstanceDbSection? Static { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.NestedInstances" />
    public IReadOnlyList<IS7DataBlockInstance> NestedInstances { get; init; } = [];

    /// <inheritdoc cref="IS7DataBlockInstance.FullPath" />
    public string? FullPath { get; init; } = null;
}
