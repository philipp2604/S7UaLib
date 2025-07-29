namespace S7UaLib.Core.Ua.Configuration;

/// <summary>
/// A class defining transport quotas for an OPC UA application.
/// </summary>
public class TransportQuotas
{
    /// <summary>
    /// Gets or sets the operation timeout in milliseconds.
    /// </summary>
    public uint OperationTimeout { get; set; } = 120000;

    /// <summary>
    /// Gets or sets the maximum string length in bytes.
    /// </summary>
    public uint MaxStringLength { get; set; } = 4194304;

    /// <summary>
    /// Gets or sets the maximum byte string length in bytes.
    /// </summary>
    public uint MaxByteStringLength { get; set; } = 4194304;

    /// <summary>
    /// Gets or sets the maximum array length.
    /// </summary>
    public uint MaxArrayLength { get; set; } = 65535;

    /// <summary>
    /// Gets or sets the maximum message size in bytes.
    /// </summary>
    public uint MaxMessageSize { get; set; } = 4194304;

    /// <summary>
    /// Gets or sets the maximum buffer size in bytes.
    /// </summary>
    public uint MaxBufferSize { get; set; } = 65535;

    /// <summary>
    /// Gets or sets the maximum encoding nesting levels.
    /// </summary>
    public uint MaxEncodingNestingLevels { get; set; } = 200;

    /// <summary>
    /// Gets or sets the maximum number of decoder recoveries allowed.
    /// </summary>
    public uint MaxDecoderRecoveries { get; set; } = 0;

    /// <summary>
    /// Gets or sets the lifetime of a channel in milliseconds.
    /// </summary>
    public uint ChannelLifetime { get; set; } = 30000;

    /// <summary>
    /// Gets or sets the lifetime of a security token in milliseconds.
    /// </summary>
    public uint SecurityTokenLifetime { get; set; } = 3600000;
}