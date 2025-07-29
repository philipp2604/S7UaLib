namespace S7UaLib.Core.Ua.Configuration;

/// <summary>
/// A class representing the client configuration settings for an OPC UA client.
/// </summary>
public class ClientConfiguration
{
    /// <summary>
    /// Gets or sets the session timeout in milliseconds.
    /// </summary>
    public uint SessionTimeout { get; set; } = 60000;

    /// <summary>
    /// Gets or sets the 'well-known' discovery URLs for the OPC UA client.
    /// </summary>
    public List<string> WellKnownDiscoveryUrls { get; set; } = ["opc.tcp://{0}:4840", "http://{0}:52601/UADiscovery", "http://{0}/UADiscovery/Default.svc"];

    /// <summary>
    /// Gets or sets the minimum subscription lifetime in milliseconds.
    /// </summary>
    public uint MinSubscriptionLifetime { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the OperationLimits for the client.
    /// </summary>
    public OperationLimits OperationLimits { get; set; } = new();
}