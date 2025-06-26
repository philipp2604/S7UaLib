using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Events;

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

    #endregion
    #endregion

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

    #endregion

    #region Public Methods

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

    #endregion
}