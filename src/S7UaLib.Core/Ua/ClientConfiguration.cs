namespace S7UaLib.Core.Ua;

public class ClientConfiguration
{
    public uint SessionTimeout { get; set; } = 60000;
    public List<string> WellKnownDiscoveryUrls { get; set; } = ["opc.tcp://{0}:4840", "http://{0}:52601/UADiscovery", "http://{0}/UADiscovery/Default.svc"];
    public uint MinSubscriptionLifetime { get; set; } = 10000;
    public OperationLimits OperationLimits { get; set; } = new();
}