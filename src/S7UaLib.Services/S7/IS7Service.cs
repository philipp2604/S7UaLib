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
public interface IS7Service : IDisposable
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
    /// <remarks>A value of -1 disables automatic reconnection.</remarks>
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
    /// <param name="leaveChannelOpen">If <c>true</c>, the underlying communication channel is left open; otherwise, it is closed.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous disconnect operation.</returns>
    public Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default);

    #endregion Connection Methods

    #region Configuration Methods

    /// <summary>
    /// Configures the client for first use.
    /// </summary>
    /// <param name="appConfig">The <see cref="ApplicationConfiguration"/> to use for the client.</param>
    /// <returns>A task indicating the state of the async function.</returns>
    public Task ConfigureAsync(ApplicationConfiguration appConfig);

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

    #region Structure Discovery and Registration Methods

    /// <summary>
    /// Discovers the entire structure of the OPC UA server and populates the internal data store.
    /// This includes all data blocks, I/O areas, and their variables.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task DiscoverStructureAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new variable manually in the data store's structure.
    /// The parent element of the variable must already exist. This method will not create parent elements.
    /// If the variable is a struct with members, its members are also registered recursively.
    /// After successful registration, the internal cache is rebuilt.
    /// </summary>
    /// <param name="variable">The <see cref="IS7Variable"/> instance to register. It must contain the FullPath, NodeId and other relevant information.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that returns true if the registration was successful; otherwise, false (e.g., if the parent path does not exist or the variable already exists).</returns>
    Task<bool> RegisterVariableAsync(IS7Variable variable, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new global data block manually in the data store's structure.
    /// The parent element of the variable must already exist. This method will not create parent elements.
    /// After successful registration, the internal cache is rebuilt.
    /// </summary>
    /// <param name="dataBlock">The <see cref="IS7DataBlockGlobal"/> instance to register. It must contain the FullPath, NodeId and other relevant information.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that returns true if the registration was successful; otherwise, false (e.g., if the parent path does not exist or the variable already exists).</returns>
    Task<bool> RegisterGlobalDataBlockAsync(IS7DataBlockGlobal dataBlock, CancellationToken cancellationToken = default);

    #endregion Structure Discovery and Registration Methods

    #region Variables Access and Manipulation Methods

    /// <summary>
    /// Filters and returns variables in cache based on a predicate.
    /// </summary>
    /// <param name="predicate">A function to test each variable for a condition.</param>
    /// <returns>A list containing the variables that fulfill the condition.</returns>
    public IReadOnlyList<IS7Variable> FindVariablesWhere(Func<IS7Variable, bool> predicate);

    /// <summary>
    /// Gets the cached <see cref="IS7Inputs"/>.
    /// </summary>
    /// <returns>The cached <see cref="IS7Inputs"/>.</returns>
    public IS7Inputs? GetInputs();

    /// <summary>
    /// Gets the cached <see cref="IS7Outputs"/>.
    /// </summary>
    /// <returns>The cached <see cref="IS7Outputs"/>.</returns>
    public IS7Outputs? GetOutputs();

    /// <summary>
    /// Gets the cached <see cref="IS7Memory"/>.
    /// </summary>
    /// <returns>The cached <see cref="IS7Memory"/>.</returns>
    public IS7Memory? GetMemory();

    /// <summary>
    /// Gets the cached <see cref="IS7Counters"/>.
    /// </summary>
    /// <returns>The cached <see cref="IS7Counters"/>.</returns>
    public IS7Counters? GetCounters();

    /// <summary>
    /// Gets the cached <see cref="IS7Timers"/>.
    /// </summary>
    /// <returns>The cached <see cref="IS7Timers"/>.</returns>
    public IS7Timers? GetTimers();

    /// <summary>
    /// Gets all cached <see cref="IS7DataBlockInstance"/>s.
    /// </summary>
    /// <returns>An <see cref="IReadOnlyList{IS7DataBlockInstance}"/> containing the cached instance data blocks.</returns>
    public IReadOnlyList<IS7DataBlockInstance> GetInstanceDataBlocks();

    /// <summary>
    /// Gets all cached <see cref="IS7DataBlockGlobal"/>s.
    /// </summary>
    /// <returns>An <see cref="IReadOnlyList{IS7DataBlockGlobal}"/> containing the cached global data blocks.</returns>
    public IReadOnlyList<IS7DataBlockGlobal> GetGlobalDataBlocks();

    /// <summary>
    /// Reads the values of all discovered variables from the PLC.
    /// Raises the VariableValueChanged event for any variable whose value has changed.
    /// </summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public Task ReadAllVariablesAsync(CancellationToken cancellationToken = default);

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
    public Task<bool> UpdateVariableTypeAsync(string fullPath, S7DataType newType, CancellationToken cancellationToken = default);

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