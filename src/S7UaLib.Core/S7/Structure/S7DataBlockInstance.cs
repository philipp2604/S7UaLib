using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.S7.Structure;

/// <summary>
/// Represents an S7 instance data block within the PLC's memory structure.
/// </summary>
public record S7DataBlockInstance : IS7DataBlockInstance
{
    #region Public Properties

    /// <inheritdoc cref="IUaNode.NodeId" />
    public string? NodeId { get; init; }

    /// <inheritdoc cref="IUaNode.DisplayName" />
    public string? DisplayName { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.Inputs" />
    public IS7InstanceDbSection? Inputs { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.Outputs" />
    public IS7InstanceDbSection? Outputs { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.InOuts" />
    public IS7InstanceDbSection? InOuts { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.Static" />
    public IS7InstanceDbSection? Static { get; init; }

    /// <inheritdoc cref="IS7DataBlockInstance.NestedInstances" />
    public IReadOnlyList<IS7DataBlockInstance> NestedInstances { get; init; } = [];

    /// <inheritdoc cref="IS7DataBlockInstance.FullPath" />
    public string? FullPath { get; init; } = null;

    #endregion Public Properties
}