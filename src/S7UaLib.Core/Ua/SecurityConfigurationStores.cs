namespace S7UaLib.Core.Ua;

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
        TrustedRoot = "trusted";
        AppRoot = "app";
        IssuerRoot = "issuer";
        RejectedRoot = null;
    }

    public string SubjectName { get; set; }
    public string TrustedRoot { get; set; }
    public string AppRoot { get; set; }
    public string IssuerRoot { get; set; }
    public string? RejectedRoot { get; set; }
}