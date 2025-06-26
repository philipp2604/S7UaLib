using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.UA;

/// <summary>
/// Defines a common interface for UA elements that have a NodeId and DisplayName.
/// </summary>
public interface IUaElement
{
    /// <summary>
    /// Gets the unique identifier of the node.
    /// </summary>
    public NodeId? NodeId { get; }

    /// <summary>
    /// Gets the display name of the node.
    /// </summary>
    public string? DisplayName { get; }
}
