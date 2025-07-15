using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.Ua;
public class OperationLimits
{
    public uint MaxNodesPerRead { get; set; } = 2500;
    public uint MaxNodesPerHistoryReadData { get; set; } = 1000;
    public uint MaxNodesPerHistoryReadEvents { get; set; } = 1000;
    public uint MaxNodesPerWrite { get; set; } = 2500;
    public uint MaxNodesPerHistoryUpdateData { get; set; } = 1000;
    public uint MaxNodesPerHistoryUpdateEvents { get; set; } = 1000;
    public uint MaxNodesPerMethodCall { get; set; } = 2500;
    public uint MaxNodesPerBrowse { get; set; } = 2500;
    public uint MaxNodesPerRegisterNodes { get; set; } = 2500;
    public uint MaxNodesPerTranslateBrowsePathsToNodeIds { get; set; } = 2500;
    public uint MaxNodesPerNodeManagement { get; set; } = 2500;
    public uint MaxMonitoredItemsPerCall { get; set; } = 2500;
}
