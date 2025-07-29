namespace S7UaLib.Core.Ua.Configuration;

/// <summary>
/// A class defining the security configuration stores for an OPC UA application.
/// </summary>
public class SecurityConfigurationStores
{
    /// <summary>
    /// Creates a new instance of <see cref="SecurityConfigurationStores"/> with specified parameters.
    /// </summary>
    /// <param name="subjectName">The subject name.</param>
    /// <param name="trustedRoot">The root dir for trusted certificates.</param>
    /// <param name="appRoot">The root dir for application certificates.</param>
    /// <param name="issuerRoot">The root dir for trusted issuer certificates.</param>
    /// <param name="rejectedRoot">The root dir for rejected certificates.</param>
    public SecurityConfigurationStores(string subjectName, string trustedRoot, string appRoot, string issuerRoot, string? rejectedRoot = null)
    {
        SubjectName = subjectName;
        TrustedRoot = trustedRoot;
        AppRoot = appRoot;
        RejectedRoot = rejectedRoot;
        IssuerRoot = issuerRoot;
    }

    /// <summary>
    /// Creates a new instance of <see cref="SecurityConfigurationStores"/> with default parameters.
    /// </summary>
    public SecurityConfigurationStores()
    {
        SubjectName = "CN=S7UaLib, DC=localhost";
        TrustedRoot = "certs";
        AppRoot = "certs";
        IssuerRoot = "certs";
        RejectedRoot = "certs";
    }

    /// <summary>
    /// Gets or sets the subject name for the application certificate.
    /// </summary>

    public string SubjectName { get; set; }

    /// <summary>
    /// Gets or sets the root directory for trusted certificates.
    /// </summary>
    public string TrustedRoot { get; set; }

    /// <summary>
    /// Gets or sets the root directory for application certificates.
    /// </summary>
    public string AppRoot { get; set; }

    /// <summary>
    /// Gets or sets the root directory for trusted issuer certificates.
    /// </summary>
    public string IssuerRoot { get; set; }

    /// <summary>
    /// Gets or sets the root directory for rejected certificates.
    /// </summary>
    public string? RejectedRoot { get; set; }
}