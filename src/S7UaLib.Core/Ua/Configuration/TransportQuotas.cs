namespace S7UaLib.Core.Ua.Configuration;

public class TransportQuotas
{
    public uint OperationTimeout { get; set; } = 120000;
    public uint MaxStringLength { get; set; } = 4194304;
    public uint MaxByteStringLength { get; set; } = 4194304;
    public uint MaxArrayLength { get; set; } = 65535;
    public uint MaxMessageSize { get; set; } = 4194304;
    public uint MaxBufferSize { get; set; } = 65535;
    public uint MaxEncodingNestingLevels { get; set; } = 200;
    public uint MaxDecoderRecoveries { get; set; } = 0;
    public uint ChannelLifetime { get; set; } = 30000;
    public uint SecurityTokenLifetime { get; set; } = 3600000;
}