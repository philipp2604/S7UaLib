using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.S7.Structure;
internal static class S7StructureConstants
{
    internal const int _s7NamespaceIndex = 3;
    internal const string _s7InputsNamespaceIdentifier = "ns=3;s=Inputs";
    internal const string _s7OutputsNamespaceIdentifier = "ns=3;s=Outputs";
    internal const string _s7MemoryNamespaceIdentifier = "ns=3;s=Memory";
    internal const string _s7TimersNamespaceIdentifier = "ns=3;s=Timers";
    internal const string _s7CountersNamespaceIdentifier = "ns=3;s=Counters";
    internal const string _s7DataBlocksGlobalNamespaceIdentifier = "ns=3;s=DataBlocksGlobal";
    internal const string _s7DataBlocksInstanceNamespaceIdentifier = "ns=3;s=DataBlocksInstance";
}
