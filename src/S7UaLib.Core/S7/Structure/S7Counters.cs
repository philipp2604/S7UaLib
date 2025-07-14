using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.S7.Structure;

/// <summary>
/// Represents the S7 Counter (C) memory area.
/// </summary>
public record S7Counters : S7StructureElement, IS7Counters;