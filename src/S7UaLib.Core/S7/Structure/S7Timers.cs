using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.S7.Structure;

/// <summary>
/// Represents the S7 Timer (T) memory area.
/// </summary>
public record S7Timers : S7StructureElement, IS7Timers;