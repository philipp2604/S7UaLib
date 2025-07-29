namespace S7UaLib.Core.Ua.Configuration;

/// <summary>
/// A class representing the configuration settings for an application in the context of OPC UA.
/// </summary>
public class ApplicationConfiguration
{
    /// <summary>
    /// Gets or sets the name of the application.
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the unique URI of the application, which is used to identify it in the OPC UA address space.
    /// </summary>
    public string ApplicationUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the product URI of the application, which is typically a unique identifier for the product.
    /// </summary>
    public string ProductUri { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the client configuration settings of type <see cref="ClientConfiguration"/>."
    /// </summary>

    public ClientConfiguration ClientConfiguration { get; set; } = new();

    /// <summary>
    /// Gets or sets the server configuration settings of type <see cref="ServerConfiguration"/>."
    /// </summary>
    public SecurityConfiguration SecurityConfiguration { get; set; } = new(new SecurityConfigurationStores());

    /// <summary>
    /// Gets or sets the transport quotas for the application, which define limits on message sizes and other transport-related parameters.
    /// </summary>
    public TransportQuotas TransportQuotas { get; set; } = new();

    /// <summary>
    /// Validates the most important properties of the application configuration.
    /// </summary>
    /// <returns></returns>
    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(ApplicationName)
            && !string.IsNullOrWhiteSpace(ApplicationUri)
            && !string.IsNullOrWhiteSpace(ProductUri);
    }
}