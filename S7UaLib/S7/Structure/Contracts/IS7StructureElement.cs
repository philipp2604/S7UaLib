using Opc.Ua;
using S7UaLib.UA;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.S7.Structure;

/// <summary>
/// Defines a common interface for S7 structure elements that have variables and a full path.
/// </summary>
internal interface IS7StructureElement : IUaElement
{
    #region Public Properties

    /// <summary>
    /// Gets the collection of S7 variables in the structure element.
    /// </summary>
    public IReadOnlyList<IS7Variable> Variables { get; init; }

    /// <summary>
    /// Gets the full path of the structure element in the OPC UA address space.
    /// </summary>
    public string? FullPath { get; init; }

    #endregion
}
