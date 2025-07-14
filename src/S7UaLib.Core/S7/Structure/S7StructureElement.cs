using S7UaLib.Core.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.S7.Structure;

/// <summary>
/// Represents an element of a S7 variable, containing its NodeId, display name, variables, and full path.
/// </summary>
public record S7StructureElement : IS7StructureElement
{
    #region Public Properties

    /// <inheritdoc cref="IUaNode.NodeId" />
    public string? NodeId { get; init; }

    /// <inheritdoc cref="IUaNode.DisplayName" />
    public string? DisplayName { get; init; }

    /// <inheritdoc cref="IS7StructureElement.Variables" />
    public IReadOnlyList<IS7Variable> Variables { get; init; } = [];

    /// <inheritdoc cref="IS7StructureElement.FullPath" />
    public string? FullPath { get; init; }

    #endregion
}