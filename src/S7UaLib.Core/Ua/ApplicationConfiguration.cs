using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace S7UaLib.Core.Ua;
public class ApplicationConfiguration
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationUri { get; set; } = string.Empty;
    public string ProductUri { get; set; } = string.Empty;
    public uint DefaultSessionTimeout { get; set; } = 60000;
    public bool AutoAcceptUntrustedCertificates { get; set; } = false;
    public uint ChannelLifetime { get; set; } = 300000;
    public uint OperationTimeout { get; set; } = 120000;
    public uint SecurityTokenLifetime { get; set; } = 3600000;

    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(ApplicationName)
            && !string.IsNullOrWhiteSpace(ApplicationUri)
            && !string.IsNullOrWhiteSpace(ProductUri);
    }
}
