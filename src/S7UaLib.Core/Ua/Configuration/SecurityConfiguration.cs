namespace S7UaLib.Core.Ua.Configuration;

/// <summary>
/// A class representing the security configuration settings for an OPC UA application.
/// </summary>
/// <param name="stores">The certification stores of type <see cref="Configuration.SecurityConfigurationStores"/></param>
public class SecurityConfiguration(SecurityConfigurationStores stores)
{
    /// <summary>
    /// Gets or sets the security configuration stores, which include trusted and rejected certificate stores.
    /// </summary>
    public SecurityConfigurationStores SecurityConfigurationStores { get; set; } = stores;

    /// <summary>
    /// Gets or sets the nonce length used for security tokens in the OPC UA protocol.
    /// </summary>
    public uint NonceLength { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of rejected certificates.
    /// </summary>
    public uint MaxRejectedCertificates { get; set; }

    /// <summary>
    /// Gets or sets whether to automatically accept untrusted certificates without prompting the user.
    /// </summary>
    public bool AutoAcceptUntrustedCertificates { get; set; }

    /// <summary>
    /// Gets or sets whether to skip domain validation for certificates.
    /// </summary>
    public DomainValidation SkipDomainValidation { get; set; } = new DomainValidation();

    /// <summary>
    /// Gets or sets the validation settings for SHA1 signed certificates.
    /// </summary>
    public SHA1Validation RejectSHA1SignedCertificates { get; set; } = new SHA1Validation();

    /// <summary>
    /// Gets or sets the directory where user roles are stored.
    /// </summary>
    public string UserRoleDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether to reject certificates with unknown revocation status.
    /// </summary>
    public bool RejectUnknownRevocationStatus { get; set; }

    /// <summary>
    /// Gets or sets the minimum key size for certificates in bits.
    /// </summary>
    public ushort MinCertificateKeySize { get; set; }

    /// <summary>
    /// Gets or sets whether to use prevalidated certificates.
    /// </summary>
    public bool UseValidatedCertificates { get; set; }

    /// <summary>
    /// Gets or sets whether to add the application certificate to the trusted store automatically.
    /// </summary>
    public bool AddAppCertToTrustedStore { get; set; }

    /// <summary>
    /// Gets or sets whether to send the certificate chain when sending a certificate.
    /// </summary>
    public bool SendCertificateChain { get; set; }

    /// <summary>
    /// Gets or sets whether to suppress nonce validation errors.
    /// </summary>
    public bool SuppressNonceValidationErrors { get; set; }
}

/// <summary>
/// A helper class for domain validation settings.
/// </summary>
public class DomainValidation
{
    /// <summary>
    /// Gets or sets whether to skip domain validation for certificates.
    /// </summary>
    public bool Skip { get; set; } = false;
}

/// <summary>
/// A helper class for SHA1 certificate validation settings.
/// </summary>
public class SHA1Validation
{
    /// <summary>
    /// Gets or sets whether to reject SHA1 signed certificates.
    /// </summary>
    public bool Reject { get; set; } = true;
}