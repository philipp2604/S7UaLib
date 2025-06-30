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
internal interface IS7InstanceDbSection : IUaElement;
