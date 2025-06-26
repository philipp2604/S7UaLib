using Opc.Ua;
using S7UaLib.UA;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.S7.Structure.Contracts;

/// <summary>
/// Defines a common interface for S7 instance data block sections within the PLC's memory structure.
/// </summary>
internal interface IS7InstanceDbSection : IUaElement
{
    /// <summary>
    /// Gets the list of simple variables (e.g., BOOL, INT, REAL) contained within this section.
    /// </summary>
    public IReadOnlyList<IS7Variable> Variables { get; init; }

    /// <summary>
    /// Gets the list of nested function block instances declared within this section (typically the 'Static' section).
    /// </summary>
    public IReadOnlyList<IS7DataBlockInstance> NestedInstances { get; init; }
}
