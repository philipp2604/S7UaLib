using S7UaLib.Core.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.S7.Structure;

public interface IS7InstanceDbSection : IUaNode
{
    #region Public Properties

    /// <summary>
    /// Gets the list of simple variables (e.g., BOOL, INT, REAL) contained within this section.
    /// </summary>
    public IReadOnlyList<IS7Variable> Variables { get; init; }

    /// <summary>
    /// Gets the list of nested function block instances declared within this section (typically the 'Static' section).
    /// </summary>
    public IReadOnlyList<IS7DataBlockInstance> NestedInstances { get; init; }

    /// <summary>
    /// Gets the full symbolic path of the instance within the PLC, if available.
    /// </summary>
    public string? FullPath { get; init; }

    #endregion Public Properties
}
