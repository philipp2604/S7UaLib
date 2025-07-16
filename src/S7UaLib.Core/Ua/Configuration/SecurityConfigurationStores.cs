namespace S7UaLib.Core.Ua.Configuration;

public class SecurityConfigurationStores
{
    public SecurityConfigurationStores(string subjectName, string trustedRoot, string appRoot, string issuerRoot, string? rejectedRoot = null)
    {
        SubjectName = subjectName;
        TrustedRoot = trustedRoot;
        AppRoot = appRoot;
        RejectedRoot = rejectedRoot;
        IssuerRoot = issuerRoot;
    }

    public SecurityConfigurationStores()
    {
        SubjectName = "CN=S7UaLib, DC=localhost";
        TrustedRoot = "certs";
        AppRoot = "certs";
        IssuerRoot = "certs";
        RejectedRoot = "certs";
    }

    public string SubjectName { get; set; }
    public string TrustedRoot { get; set; }
    public string AppRoot { get; set; }
    public string IssuerRoot { get; set; }
    public string? RejectedRoot { get; set; }
}