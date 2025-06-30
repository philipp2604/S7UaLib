using Opc.Ua;
using S7UaLib.Events;
using S7UaLib.S7.Structure;
using S7UaLib.S7.Types;
using S7UaLib.UA;

namespace S7UaLib.Client;

/// <summary>
/// Defines a common interface for S7 UA clients that manage connections to S7 devices using the OPC UA protocol.
/// </summary>
internal interface IS7UaClient
{
    #region Public Events

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
    /// Gets or sets the session timeout in milliseconds after which the session is considered invalid after the last communication.
    /// </summary>
    public uint SessionTimeout { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether untrusted SSL/TLS certificates are accepted.
    /// </summary>
    /// <remarks>Use this property with caution, as accepting untrusted certificates can expose the
    /// application to security risks. This setting is typically used for testing or development purposes and should not
    /// be enabled in production environments.</remarks>
    public bool AcceptUntrustedCertificates { get; set; }

    /// <summary>
    /// Gets or sets the identity information of the user.
    /// </summary>
    public UserIdentity UserIdentity { get; set; }

    /// <summary>
    /// Gets a value indicating whether the connection is currently active and valid.
    /// </summary>
    public bool IsConnected { get; }

    #endregion Public Properties

    #region Public Methods

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
    public void Disconnect(bool leaveChannelOpen = false);

    #endregion Connection Methods

    #region Structure Browsing and Discovery Methods

    /// <summary>
    /// Retrieves all global data blocks from the OPC UA server.
    /// </summary>
    /// <returns>A read-only list of discovered <see cref="S7DataBlockGlobal"/> shells.</returns>
    public IReadOnlyList<S7DataBlockGlobal> GetAllGlobalDataBlocks();

    /// <summary>
    /// Retrieves all instance data blocks from the OPC UA server.
    /// </summary>
    /// <returns>A read-only list of discovered <see cref="S7DataBlockInstance"/> shells.</returns>
    public IReadOnlyList<S7DataBlockInstance> GetAllInstanceDataBlocks();

    /// <summary>
    /// Retrieves the S7 Memory (M) area organizational element.
    /// </summary>
    /// <returns>A <see cref="S7Memory"/> shell, or null if not found or an error occurs.</returns>
    public S7Memory? GetMemory();

    /// <summary>
    /// Retrieves the S7 Inputs (I) area organizational element.
    /// </summary>
    /// <returns>A <see cref="S7Inputs"/> shell, or null if not found or an error occurs.</returns>
    public S7Inputs? GetInputs();

    /// <summary>
    /// Retrieves the S7 Outputs (Q) area organizational element.
    /// </summary>
    /// <returns>A <see cref="S7Outputs"/> shell, or null if not found or an error occurs.</returns>
    public S7Outputs? GetOutputs();

    /// <summary>
    /// Retrieves the S7 Timers (T) area organizational element.
    /// </summary>
    /// <returns>A <see cref="S7Timers"/> shell, or null if not found or an error occurs.</returns>
    public S7Timers? GetTimers();

    /// <summary>
    /// Retrieves the S7 Counters (C) area organizational element.
    /// </summary>
    /// <returns>A <see cref="S7Counters"/> shell, or null if not found or an error occurs.</returns>
    public S7Counters? GetCounters();

    /// <summary>
    /// Discovers the full structure of a given UA element shell. This method acts as a dispatcher,
    /// calling the appropriate specialized "Discover" method based on the element's type.
    /// </summary>
    /// <param name="elementShell">The "shell" element, typically containing only a NodeId and DisplayName.</param>
    /// <returns>A fully discovered element as <see cref="IUaElement"/>, or null if the type is unsupported or an error occurs.</returns>
    public IUaElement? DiscoverElement(IUaElement elementShell);

    /// <summary>
    /// Discovers the variables contained within a simple structure element (like a global DB or I/O area).
    /// </summary>
    /// <typeparam name="T">The type of the structure element, which must be a derivative of <see cref="S7StructureElement"/>.</typeparam>
    /// <param name="element">The structure element shell whose variables are to be discovered.</param>
    /// <returns>A new instance of the element with its <c>Variables</c> list populated. Returns the original element on failure.</returns>
    public T DiscoverVariablesOfElement<T>(T element) where T : S7StructureElement;

    /// <summary>
    /// Discovers the full nested structure of an instance data block.
    /// </summary>
    /// <param name="instanceDbShell">The instance data block shell to discover.</param>
    /// <returns>A new instance of the data block with its sections populated. Returns the original element on failure.</returns>
    public S7DataBlockInstance DiscoverInstanceOfDataBlock(S7DataBlockInstance instanceDbShell);

    #endregion Structure Browsing and Discovery Methods

    #region Reading and Writing Methods
    #region Reading Methods

    /// <summary>
    /// Reads the values for any previously discovered S7 element.
    /// </summary>
    /// <typeparam name="T">The type of the element to read, which must implement <see cref="IUaElement"/>.</typeparam>
    /// <param name="elementWithStructure">An element whose structure has already been discovered.</param>
    /// <param name="rootContextName">The name of the root collection (e.g., "DataBlocksGlobal", "Inputs") used for building the full path.</param>
    /// <returns>A new instance of the element, populated with values. Returns the original element on failure.</returns>
    public T ReadValuesOfElement<T>(T elementWithStructure, string? rootContextName = null) where T : IUaElement;

    #endregion
    #region Writing Methods

    /// <summary>
    /// Writes a value to a variable, performing S7-specific type conversion before sending.
    /// </summary>
    /// <param name="nodeId">The NodeId of the variable to write to.</param>
    /// <param name="value">The user-friendly .NET value.</param>
    /// <param name="s7Type">The target S7 data type for correct conversion.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if nodeId or value is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if the value conversion fails or the session is not connected.</exception>
    public Task<bool> WriteVariableAsync(NodeId nodeId, object value, S7DataType s7Type);

    /// <summary>
    /// Writes a value to a variable, performing S7-specific type conversion before sending.
    /// </summary>
    /// <param name="variable">The S7Variable object containing the NodeId and S7Type.</param>
    /// <param name="value">The user-friendly .NET value.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if variable or value is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the variable's NodeId is null.</exception>
    public Task<bool> WriteVariableAsync(S7Variable variable, object value);

    /// <summary>
    /// Writes a raw, Variant-compatible value directly to an OPC UA variable.
    /// </summary>
    /// <param name="nodeId">The NodeId of the variable to write to.</param>
    /// <param name="rawValue">The raw value to be written.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if nodeId or rawValue is null.</exception>
    public Task<bool> WriteRawVariableAsync(NodeId nodeId, object rawValue);

    #endregion

    #endregion Reading and Writing Methods

    #endregion Public Methods
}