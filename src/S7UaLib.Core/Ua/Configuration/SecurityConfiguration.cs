namespace S7UaLib.Core.Ua.Configuration;

public class SecurityConfiguration(SecurityConfigurationStores stores)
{
    public SecurityConfigurationStores SecurityConfigurationStores { get; set; } = stores;
    public uint NonceLength { get; set; }
    public uint MaxRejectedCertificates { get; set; }
    public bool AutoAcceptUntrustedCertificates { get; set; }
    public DomainValidation SkipDomainValidation { get; set; } = new DomainValidation();
    public string UserRoleDirectory { get; set; } = string.Empty;
    public bool RejectSHA1SignedCertificates { get; set; }
    public bool RejectUnknownRevocationStatus { get; set; }
    public ushort MinCertificateKeySize { get; set; }
    public bool UseValidatedCertificates { get; set; }
    public bool AddAppCertToTrustedStore { get; set; }
    public bool SendCertificateChain { get; set; }
    public bool SuppressNonceValidationErrors { get; set; }
}

public class DomainValidation
{
    public bool Skip { get; set; }
}