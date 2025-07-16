namespace S7UaLib.Core.Ua.Configuration;

public class ApplicationConfiguration
{
    public string ApplicationName { get; set; } = string.Empty;
    public string ApplicationUri { get; set; } = string.Empty;
    public string ProductUri { get; set; } = string.Empty;

    public ClientConfiguration ClientConfiguration { get; set; } = new();
    public SecurityConfiguration SecurityConfiguration { get; set; } = new(new SecurityConfigurationStores());
    public TransportQuotas TransportQuotas { get; set; } = new();

    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(ApplicationName)
            && !string.IsNullOrWhiteSpace(ApplicationUri)
            && !string.IsNullOrWhiteSpace(ProductUri);
    }
}