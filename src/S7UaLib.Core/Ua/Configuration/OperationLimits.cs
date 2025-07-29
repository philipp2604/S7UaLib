namespace S7UaLib.Core.Ua.Configuration;

/// <summary>
/// A class representing the operation limits for an OPC UA client.
/// </summary>
public class OperationLimits
{
    /// <summary>
    /// Gets or sets the maximum number of nodes that can be read in a single operation.
    /// </summary>
    public uint MaxNodesPerRead { get; set; } = 2500;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be written in a single operation.
    /// </summary>
    public uint MaxNodesPerHistoryReadData { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be read in a single history read operation for events.
    /// </summary>
    public uint MaxNodesPerHistoryReadEvents { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be written in a single operation.
    /// </summary>
    public uint MaxNodesPerWrite { get; set; } = 2500;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be updated in a single history update operation for data.
    /// </summary>
    public uint MaxNodesPerHistoryUpdateData { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be updated in a single history update operation for events.
    /// </summary>
    public uint MaxNodesPerHistoryUpdateEvents { get; set; } = 1000;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be monitored in a single operation.
    /// </summary>
    public uint MaxNodesPerMethodCall { get; set; } = 2500;


    /// <summary>
    /// Gets or sets the maximum number of nodes that can be browsed in a single operation.
    /// </summary>
    public uint MaxNodesPerBrowse { get; set; } = 2500;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be registered in a single RegisterNodes operation.
    /// </summary>
    public uint MaxNodesPerRegisterNodes { get; set; } = 2500;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be unregistered in a single UnregisterNodes operation.
    /// </summary>
    public uint MaxNodesPerTranslateBrowsePathsToNodeIds { get; set; } = 2500;

    /// <summary>
    /// Gets or sets the maximum number of nodes that can be managed in a single NodeManagement operation.
    /// </summary>
    public uint MaxNodesPerNodeManagement { get; set; } = 2500;

    /// <summary>
    /// Gets or sets the maximum number of monitored items that can be created in a single operation.
    /// </summary>
    public uint MaxMonitoredItemsPerCall { get; set; } = 2500;
}