using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using System.Security.Cryptography.X509Certificates;

namespace S7UaLib.Services.S7;

/// <summary>
/// Defines the contract for interacting with an S7 service, including operations for discovering structure,  reading
/// and writing variables, and managing variable types.
/// </summary>
/// <remarks>This interface provides methods and events for working with S7 PLCs, including reading and writing
/// variable values,  discovering the server structure, and updating variable types. Implementations of this interface
/// are expected to  handle communication with the PLC and manage the internal data store.</remarks>
internal interface IS7Service : IDisposable
{
    #region Public Events

    #region Connection Events

    /// <summary>
    /// Occurs when a connection attempt to the server is initiated.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? Connecting;

    /// <summary>
    /// Occurs when a connection to the server has been successfully established.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? Connected;

    /// <summary>
    /// Occurs when a disconnection from the server is initiated.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? Disconnecting;

    /// <summary>
    /// Occurs when the client has been disconnected from the server.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? Disconnected;

    /// <summary>
    /// Occurs when the client is attempting to reconnect to the server after a connection loss.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? Reconnecting;

    /// <summary>
    /// Occurs when the client has successfully reconnected to the server.
    /// </summary>
    public event EventHandler<ConnectionEventArgs>? Reconnected;

    #endregion Connection Events

    #region Variables Events

    /// <summary>
    /// Occurs when a variable's value changes after a read operation.
    /// IMPORTANT: This event may be raised on a background thread.
    /// Subscribers must ensure their event handling logic is thread-safe.
    /// </summary>
    public event EventHandler<VariableValueChangedEventArgs>? VariableValueChanged;

    #endregion Variables Events

    #endregion Public Events

    #region Public Properties

    /// <summary>
    /// Gets or sets the interval, in milliseconds, at which keep-alive messages are sent to maintain a connection.
    /// </summary>
    public int KeepAliveInterval { get; set; }

    /// <summary>
    /// Gets or sets the time interval, in milliseconds, between automatic reconnection attempts.
    /// <remarks></remarks>A value of -1 disables automatic reconnection.</remarks>
    /// </summary>
    public int ReconnectPeriod { get; set; }

    /// <summary>
    /// Gets or sets the maximum reconnect period for exponential backoff, in milliseconds.
    /// <remarks>A value of -1 disables exponential backoff.</remarks>
    /// </summary>
    public int ReconnectPeriodExponentialBackoff { get; set; }

    /// <summary>
    /// Gets or sets the identity information of the user.
    /// </summary>
    public UserIdentity UserIdentity { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is currently active and valid.
    /// </summary>
    public bool IsConnected { get; }

    #endregion Public Properties

    #region Public Methods

    #region Connection Methods

    /// <summary>
    /// Asynchronously connects to the specified S7 UA server endpoint.
    /// </summary>
    /// <param name="serverUrl">The URL of the server endpoint to connect to.</param>
    /// <param name="useSecurity">A flag indicating whether to use the secure endpoint.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous connect operation.</returns>
    public Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the S7 UA server.
    /// </summary>
    /// <param name="leaveChannelOpen">If <c>true</c>, the underlying communication channel is left open;
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// otherwise, it is closed.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous disconnect operation.</returns>
    public Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default);

    #endregion Connection Methods

    #region Configuration Methods

    /// <summary>
    /// Configures the client for first use.
    /// </summary>
    /// <param name="appName">The OPC-UA application name.</param>
    /// <param name="appUri">The OPC UA application uri.</param>
    /// <param name="productUri">The OPC UA product uri.</param>
    /// <param name="securityConfiguration">The <see cref="Core.Ua.Configuration.SecurityConfiguration"/> used for configuring security settings.</param>
    /// <param name="clientConfig">The <see cref="Core.Ua.Configuration.ClientConfiguration"/>, optionally used for configuring client related settings.</param>
    /// <param name="transportQuotas">The <see cref="Core.Ua.Configuration.TransportQuotas"/>, optionally used for configuring transport quotas.</param>
    /// <param name="opLimits">The <see cref="Core.Ua.Configuration.OperationLimits"/>, optionally used for configuring operation limits.</param>
    /// <returns>A task indicating the state of the async function.</returns>
    public Task ConfigureAsync(string appName, string appUri, string productUri, SecurityConfiguration securityConfiguration, ClientConfiguration? clientConfig = null, TransportQuotas? transportQuotas = null, OperationLimits? opLimits = null);

    /// <summary>
    /// Saves the client's currently used configuration to a file.
    /// </summary>
    /// <param name="filePath">The file path to save the configuration to.</param>
    public void SaveConfiguration(string filePath);

    /// <summary>
    /// Loads the client's configuration from a file.
    /// </summary>
    /// <param name="filePath">The file path used to load the configuration from.</param>
    /// <returns>A task indicating the state of the async function.</returns>
    public Task LoadConfigurationAsync(string filePath);


    /// <summary>
    /// Adds a certificate to the trusted certificate store.
    /// </summary>
    /// <param name="certificate">The certificate to add.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to abort the operation.</param>
    /// <returns>A task indicating the state of the async function.</returns>
    public Task AddTrustedCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default);

    #endregion Configuration Methods

    #region Structure Discovery Methods

    /// <summary>
    /// Discovers the entire structure of the OPC UA server and populates the internal data store.
    /// This includes all data blocks, I/O areas, and their variables.
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// otherwise, it is closed.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// </summary>
    public Task DiscoverStructureAsync(CancellationToken cancellationToken = default);

    #endregion Structure Discovery Methods

    #region Variables Access and Manipulation Methods

    /// <summary>
    /// Reads the values of all discovered variables from the PLC.
    /// Raises the VariableValueChanged event for any variable whose value has changed.
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// otherwise, it is closed.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    /// </summary>
    public Task ReadAllVariablesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes a value to a variable specified by its full symbolic path.
    /// </summary>
    /// <param name="fullPath">The full symbolic path of the variable to write to.</param>
    /// <param name="value">The user-friendly .NET value to write.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    public Task<bool> WriteVariableAsync(string fullPath, object value);

    /// <summary>
    /// Updates the S7 data type of a variable in the data store and attempts to reconvert its current raw value.
    /// If the conversion is successful, it raises the <see cref="VariableValueChanged"/> event.
    /// </summary>
    /// <param name="fullPath">The full path of the variable to update.</param>
    /// <param name="newType">The new <see cref="S7DataType"/> to apply.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.
    /// The TaskResult is true if the variable was found and the type was updated; otherwise, false.</returns>
    public Task<bool> UpdateVariableTypeAsync(string fullPath, S7DataType newType, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a variable from the data store by its full symbolic path.
    /// </summary>
    /// <param name="fullPath">The full path of the variable (e.g., "DataBlocksGlobal.MyDb.MyVar").</param>
    /// <returns>The <see cref="IS7Variable"/> if found; otherwise, null.</returns>
    public IS7Variable? GetVariable(string fullPath);

    /// <summary>
    /// Subscribes to a variable to receive value changes from the server.
    /// Will create the main subscription on the first call.
    /// </summary>
    /// <param name="fullPath">The full symbolic path of the variable to subscribe to.</param>
    /// <param name="samplingInterval">The sampling interval in ms.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that returns true if the subscription was successful; otherwise, false.</returns>
    Task<bool> SubscribeToVariableAsync(string fullPath, uint samplingInterval = 500, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to all configured variables inside the service's data store.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that returns true if the subscriptions were successful; otherwise, false.</returns>
    public Task<bool> SubscribeToAllConfiguredVariablesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from a variable to stop receiving value changes.
    /// </summary>
    /// <param name="fullPath">The full symbolic path of the variable to unsubscribe from.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that returns true if the unsubscription was successful; otherwise, false.</returns>
    Task<bool> UnsubscribeFromVariableAsync(string fullPath, CancellationToken cancellationToken = default);

    #endregion Variables Access and Manipulation Methods

    #region Persistence Methods

    /// <summary>
    /// Saves the current entire PLC structure from the data store to a JSON file.
    /// This includes all discovered elements and their assigned data types.
    /// </summary>
    /// <param name="filePath">The path to the file where the structure will be saved.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveStructureAsync(string filePath);

    /// <summary>
    /// Loads the PLC structure from a JSON file into the data store, bypassing the need for discovery.
    /// After loading, the internal cache is automatically rebuilt.
    /// </summary>
    /// <param name="filePath">The path to the file from which the structure will be loaded.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LoadStructureAsync(string filePath);

    #endregion Persistence Methods

    #endregion Public Methods
}