namespace S7UaLib.Core.Ua.Configuration;

public class SecurityConfiguration(SecurityConfigurationStores stores)
{
    public SecurityConfigurationStores SecurityConfigurationStores { get; set; } = stores;
    public uint NonceLength { get; set; }
    public uint MaxRejectedCertificates { get; set; }
    public bool AutoAcceptUntrustedCertificates { get; set; }
    public DomainValidation SkipDomainValidation { get; set; } = new DomainValidation();
    public SHA1Validation RejectSHA1SignedCertificates { get; set; } = new SHA1Validation();
    public string UserRoleDirectory { get; set; } = string.Empty;
    public bool RejectUnknownRevocationStatus { get; set; }
    public ushort MinCertificateKeySize { get; set; }
    public bool UseValidatedCertificates { get; set; }
    public bool AddAppCertToTrustedStore { get; set; }
    public bool SendCertificateChain { get; set; }
    public bool SuppressNonceValidationErrors { get; set; }
}

public class DomainValidation
{
    public bool Skip { get; set; } = false;
}

public class SHA1Validation
{
    public bool Reject { get; set; } = true;
}