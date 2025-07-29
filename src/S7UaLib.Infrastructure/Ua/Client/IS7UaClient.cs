using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.Events;
using System.Security.Cryptography.X509Certificates;

namespace S7UaLib.Infrastructure.Ua.Client;

/// <summary>
/// Defines a common interface for S7 UA clients that manage connections to S7 devices using the OPC UA protocol.
/// </summary>
internal interface IS7UaClient : IDisposable
{
    #region Public Events - Delegated to Implementation

    #region Connection Events

    /// <summary>
    /// Occurs when a connection attempt is initiated.
    /// </summary>
    /// <remarks>This event is raised before the connection process begins, allowing subscribers to perform
    /// any necessary preparation or logging. The event provides details about the connection attempt through the <see
    /// cref="ConnectionEventArgs"/> parameter.</remarks>
    public event EventHandler<ConnectionEventArgs>? Connecting;

    /// <summary>
    /// Occurs when a connection is successfully established.
    /// </summary>
    /// <remarks>This event is raised after a connection has been successfully established.  Subscribers can
    /// use the event arguments to access details about the connection.</remarks>
    public event EventHandler<ConnectionEventArgs>? Connected;

    /// <summary>
    /// Occurs when the connection is in the process of being disconnected.
    /// </summary>
    /// <remarks>This event is raised before the disconnection process is completed, allowing subscribers to
    /// perform  any necessary cleanup or actions prior to the connection being terminated.</remarks>
    public event EventHandler<ConnectionEventArgs>? Disconnecting;

    /// <summary>
    /// Occurs when the connection is terminated.
    /// </summary>
    /// <remarks>This event is raised whenever the connection is disconnected, either intentionally or due to
    /// an error. Subscribers can use the event arguments to obtain additional details about the
    /// disconnection.</remarks>
    public event EventHandler<ConnectionEventArgs>? Disconnected;

    /// <summary>
    /// Occurs when the connection is attempting to reconnect after being interrupted.
    /// </summary>
    /// <remarks>This event is raised whenever the connection enters a reconnecting state.  Subscribers can
    /// use this event to perform actions such as notifying the user  or logging the reconnection attempt.</remarks>
    public event EventHandler<ConnectionEventArgs>? Reconnecting;

    /// <summary>
    /// Occurs when the connection is successfully re-established after being lost.
    /// </summary>
    /// <remarks>This event is triggered whenever the connection is restored. Subscribers can use this event
    /// to perform actions such as resynchronizing state or notifying users of the reconnection.</remarks>
    public event EventHandler<ConnectionEventArgs>? Reconnected;

    #endregion Connection Events

    #region Subscription Events

    /// <summary>
    /// Occurs when a notification for a monitored item is received from the server.
    /// </summary>
    event EventHandler<MonitoredItemChangedEventArgs>? MonitoredItemChanged;

    #endregion Subscription Events

    #endregion Public Events - Delegated to Implementation

    #region Public Properties - Delegated to Implementation

    /// <summary>
    /// Gets the <see cref="Core.Ua.Configuration.ApplicationConfiguration"/> used by the client."/>
    /// </summary>
    public ApplicationConfiguration? ApplicationConfiguration { get; }

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
    /// Gets the identity information of the user.
    /// </summary>
    public UserIdentity UserIdentity { get; }

    /// <summary>
    /// Gets a value indicating whether the connection is currently active and valid.
    /// </summary>
    public bool IsConnected { get; }

    #endregion Public Properties - Delegated to Implementation

    #region Public Methods - Delegated to Implementation

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

    #region Connection Methods

    /// <summary>
    /// <param name="serverUrl">The server url to connect to.</param>
    /// <param name="useSecurity">Whether to select an endpoint that uses security.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A task indicating the state of the async function.</returns>
    public Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the server and optionally leaves the channel open.
    /// </summary>
    /// <param name="leaveChannelOpen">Whether to leave the transport channel open.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A task indicating the state of the async function.</returns>
    public Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default);

    #endregion Connection Methods

    #region Structure Browsing and Discovery Methods

    /// <summary>
    /// Retrieves all global data blocks from the OPC UA server.
    /// </summary>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a read-only list of discovered <see cref="S7DataBlockGlobal"/> shells.</returns>
    public Task<IReadOnlyList<S7DataBlockGlobal>> GetAllGlobalDataBlocksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all instance data blocks from the OPC UA server.
    /// </summary>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a read-only list of discovered <see cref="S7DataBlockInstance"/> shells.</returns>
    public Task<IReadOnlyList<S7DataBlockInstance>> GetAllInstanceDataBlocksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the S7 Memory (M) area organizational element.
    /// </summary>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a <see cref="S7Memory"/> shell, or null if not found or an error occurs.</returns>
    public Task<IS7Memory?> GetMemoryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the S7 Inputs (I) area organizational element.
    /// </summary>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a <see cref="S7Inputs"/> shell, or null if not found or an error occurs.</returns>
    public Task<IS7Inputs?> GetInputsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the S7 Outputs (Q) area organizational element.
    /// </summary>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a <see cref="S7Outputs"/> shell, or null if not found or an error occurs.</returns>
    public Task<IS7Outputs?> GetOutputsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the S7 Timers (T) area organizational element.
    /// </summary>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A task encapsulating a <see cref="S7Timers"/> shell, or null if not found or an error occurs.</returns>
    public Task<IS7Timers?> GetTimersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the S7 Counters (C) area organizational element.
    /// </summary>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a <see cref="S7Counters"/> shell, or null if not found or an error occurs.</returns>
    public Task<IS7Counters?> GetCountersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the full structure of a given UA element shell. This method acts as a dispatcher,
    /// calling the appropriate specialized "Discover" method based on the element's type.
    /// </summary>
    /// <param name="elementShell">The "shell" element, typically containing only a NodeId and DisplayName.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a fully discovered element as <see cref="IUaElement"/>, or null if the type is unsupported or an error occurs.</returns>
    public Task<IUaNode?> DiscoverElementAsync(IUaNode elementShell, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers the variables contained within a simple structure element (like a global DB or I/O area).
    /// </summary>
    /// <typeparam name="T">The type of the structure element, which must be a derivative of <see cref="S7StructureElement"/>.</typeparam>
    /// <param name="element">The structure element shell whose variables are to be discovered.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a new instance of the element with its <c>Variables</c> list populated. Returns the original element on failure.</returns>
    public Task<T> DiscoverVariablesOfElementAsync<T>(T element, CancellationToken cancellationToken = default) where T : S7StructureElement;

    /// <summary>
    /// Discovers the full nested structure of an instance data block.
    /// </summary>
    /// <param name="instanceDbShell">The instance data block shell to discover.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a new instance of the data block with its sections populated. Returns the original element on failure.</returns>
    public Task<IS7DataBlockInstance> DiscoverInstanceOfDataBlockAsync(S7DataBlockInstance instanceDbShell, CancellationToken cancellationToken = default);

    #endregion Structure Browsing and Discovery Methods

    #region Reading and Writing Methods

    #region Reading Methods

    /// <summary>
    /// Reads the values for any previously discovered S7 element.
    /// </summary>
    /// <typeparam name="T">The type of the element to read, which must implement <see cref="IUaElement"/>.</typeparam>
    /// <param name="elementWithStructure">An element whose structure has already been discovered.</param>
    /// <param name="rootContextName">The name of the root collection (e.g., "DataBlocksGlobal", "Inputs") used for building the full path.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A Task encapsulating a new instance of the element, populated with values. Returns the original element on failure.</returns>
    public Task<T> ReadValuesOfElementAsync<T>(T elementWithStructure, string? rootContextName = null, CancellationToken cancellationToken = default) where T : IUaNode;

    #endregion Reading Methods

    #region Subscription Methods

    /// <summary>
    /// Creates the main subscription object on the session.
    /// </summary>
    /// <param name="publishingInterval">The interval in milliseconds at which the server should send notifications.</param>
    /// <returns>A task that returns true if the subscription was created successfully; otherwise, false.</returns>
    Task<bool> CreateSubscriptionAsync(int publishingInterval = 100);

    /// <summary>
    /// Subscribes to a variable by adding a MonitoredItem to the subscription.
    /// </summary>
    /// <param name="variable">The variable to subscribe to. Must have a valid NodeId.</param>
    /// <returns>A task that returns true if the item was added successfully; otherwise, false.</returns>
    Task<bool> SubscribeToVariableAsync(IS7Variable variable);

    /// <summary>
    /// Unsubscribes from a variable by removing the MonitoredItem.
    /// </summary>
    /// <param name="variable">The variable to unsubscribe from.</param>
    /// <returns>A task that returns true if the item was removed successfully; otherwise, false.</returns>
    Task<bool> UnsubscribeFromVariableAsync(IS7Variable variable);

    #endregion Subscription Methods

    #region Writing Methods

    /// <summary>
    /// Writes a value to a variable, performing S7-specific type conversion before sending.
    /// </summary>
    /// <param name="nodeId">The NodeId of the variable to write to.</param>
    /// <param name="value">The user-friendly .NET value.</param>
    /// <param name="s7Type">The target S7 data type for correct conversion.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if nodeId or value is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the value conversion fails or the session is not connected.</exception>
    public Task<bool> WriteVariableAsync(string nodeId, object value, S7DataType s7Type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a value to a variable, performing S7-specific type conversion before sending.
    /// </summary>
    /// <param name="variable">The S7Variable object containing the NodeId and S7Type.</param>
    /// <param name="value">The user-friendly .NET value.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if variable or value is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the variable's NodeId is null.</exception>
    public Task<bool> WriteVariableAsync(IS7Variable variable, object value, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a raw, Variant-compatible value directly to an OPC UA variable.
    /// </summary>
    /// <param name="nodeId">The NodeId of the variable to write to.</param>
    /// <param name="rawValue">The raw value to be written.</param>
    /// <param name="cancellationToken">A <c>CancellationToken</c> to abort the async function.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if nodeId or rawValue is null.</exception>
    public Task<bool> WriteRawVariableAsync(string nodeId, object rawValue, CancellationToken cancellationToken = default);

    #endregion Writing Methods

    #region Type Converter Access

    /// <summary>
    /// Gets the appropriate type converter for a given S7 data type.
    /// </summary>
    /// <param name="s7Type">The S7 data type.</param>
    /// <param name="fallbackType">The .NET type to use for the <see cref="DefaultConverter"/> if no specific converter is found.</param>
    /// <returns>An <see cref="IS7TypeConverter"/> instance.</returns>
    IS7TypeConverter GetConverter(S7DataType s7Type, Type fallbackType);

    #endregion Type Converter Access

    #endregion Reading and Writing Methods

    #endregion Public Methods - Delegated to Implementation
}