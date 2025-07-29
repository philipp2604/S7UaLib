using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.Events;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace S7UaLib.Infrastructure.Ua.Client;
internal interface IS7UaMainClient : IDisposable
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

    #region Public Properties

    /// <summary>
    /// Gets the application configuration used by the client.
    /// </summary>
    public ApplicationConfiguration? ApplicationConfiguration { get; }

    /// <summary>
    /// Gets the OPC UA application configuration for use by session pools.
    /// </summary>
    public Opc.Ua.ApplicationConfiguration? OpcApplicationConfiguration { get; }

    /// <summary>
    /// Gets the configured endpoint for use by session pools.
    /// </summary>
    public Opc.Ua.ConfiguredEndpoint? ConfiguredEndpoint { get; }

    /// <summary>
    /// Gets or sets the interval, in milliseconds, at which keep-alive messages are sent.
    /// </summary>
    public int KeepAliveInterval { get; set; }

    /// <summary>
    /// Gets or sets the time interval, in milliseconds, between automatic reconnection attempts.
    /// </summary>
    public int ReconnectPeriod { get; set; }

    /// <summary>
    /// Gets or sets the maximum reconnect period for exponential backoff, in milliseconds.
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

    /// <summary>
    /// Gets a value indicating whether the client has been configured.
    /// </summary>
    public bool IsConfigured { get; }

    #endregion Public Properties

    #region Public Methods

    #region Configuration Methods

    /// <summary>
    /// Configures the client for first use.
    /// </summary>
    /// <param name="appConfig">The application configuration to use.</param>
    public Task ConfigureAsync(ApplicationConfiguration appConfig);

    /// <summary>
    /// Saves the client's currently used configuration to a file.
    /// </summary>
    /// <param name="filePath">The file path to save the configuration to.</param>
    public void SaveConfiguration(string filePath);

    /// <summary>
    /// Loads the client's configuration from a file.
    /// </summary>
    /// <param name="filePath">The file path to load the configuration from.</param>
    public Task LoadConfigurationAsync(string filePath);

    /// <summary>
    /// Adds a certificate to the trusted certificate store.
    /// </summary>
    /// <param name="certificate">The certificate to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AddTrustedCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default);

    #endregion Configuration Methods

    #region Connection Methods

    /// <summary>
    /// Connects to the OPC UA server using the same logic as the original S7UaClient.
    /// </summary>
    /// <param name="serverUrl">The server URL to connect to.</param>
    /// <param name="useSecurity">Whether to use security.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the OPC UA server.
    /// </summary>
    /// <param name="leaveChannelOpen">Whether to leave the channel open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default);

    #endregion Connection Methods

    #region Subscription Methods

    /// <summary>
    /// Creates the main subscription object on the session.
    /// </summary>
    /// <param name="publishingInterval">The publishing interval in milliseconds.</param>
    public Task<bool> CreateSubscriptionAsync(int publishingInterval = 100);

    /// <summary>
    /// Subscribes to a variable by adding a MonitoredItem to the subscription.
    /// </summary>
    /// <param name="variable">The variable to subscribe to.</param>
    public Task<bool> SubscribeToVariableAsync(IS7Variable variable);

    /// <summary>
    /// Unsubscribes from a variable by removing the MonitoredItem.
    /// </summary>
    /// <param name="variable">The variable to unsubscribe from.</param>
    public Task<bool> UnsubscribeFromVariableAsync(IS7Variable variable);

    #endregion Subscription Methods

    #endregion Public Methods
}
