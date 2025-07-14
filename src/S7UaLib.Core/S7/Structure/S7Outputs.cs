using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.S7.Structure;

/// <summary>
/// Represents the S7 Output (Q/A) memory area.
/// </summary>
public record S7Outputs : S7StructureElement, IS7Outputs;