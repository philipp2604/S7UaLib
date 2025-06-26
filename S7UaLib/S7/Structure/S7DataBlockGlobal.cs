using S7UaLib.S7.Structure.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.S7.Structure;

/// <summary>
/// Represents a global data block in an S7 PLC structure.
/// </summary>
/// <remarks>This type is used to define a global data block within an S7 PLC, which typically contains shared
/// data accessible across multiple parts of the program.</remarks>
internal record S7DataBlockGlobal : S7StructureElement
{
}
