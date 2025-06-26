namespace S7UaLib.Client;

using System.Collections;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Events;

/// <summary>
/// Represents a client for connecting to and interacting with an S7 UA server.
/// </summary>
/// <remarks>The <see cref="S7UaClient"/> class provides methods and properties for establishing and managing a
/// session with an S7 UA server, including connection, disconnection, and reconnection handling. It supports
/// configurable keep-alive intervals, session timeouts, and reconnection strategies.  This class is thread-safe and
/// implements <see cref="IDisposable"/> to ensure proper resource cleanup.</remarks>
internal class S7UaClient : IS7UaClient, IDisposable
{
    #region Private Fields

    private readonly ILogger? _logger;
    private SessionReconnectHandler? _reconnectHandler;
    private ISession? _session;
    private readonly ApplicationConfiguration _appConfig;
    private readonly Action<IList, IList> _validateResponse;
    private bool _disposed;

    #if NET8_0
    private readonly object _sessionLock = new();
#elif NET9_0_OR_GREATER
    private readonly System.Threading.Lock _sessionLock = new();
#endif

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="S7UaClient"/> class with the specified application configuration,
    /// response validation action, and optional logger factory.
    /// </summary>
    /// <param name="appConfig">The OPC UA application configuration used to initialize the client. This parameter cannot be <see langword="null"/>.</param>
    /// <param name="validateResponse">A delegate that validates the response. This parameter cannot be <see langword="null"/>.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="appConfig"/> or <paramref name="validateResponse"/> is <see langword="null"/>.</exception>
    public S7UaClient(ApplicationConfiguration appConfig, Action<IList, IList> validateResponse, ILoggerFactory? loggerFactory = null)
    {
        _appConfig = appConfig ?? throw new ArgumentNullException(nameof(appConfig));
        _validateResponse = validateResponse ?? throw new ArgumentNullException(nameof(validateResponse));

        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<S7UaClient>();
        }
    }

    #endregion

    #region Disposing

    public void Dispose()
    {
        _disposed = true;
        Utils.SilentDispose(_session);
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Public Events
    #region Connection Events

    /// <inheritdoc cref="IS7UaClient.Connecting" />
    public event EventHandler<ConnectionEventArgs>? Connecting;

    /// <inheritdoc cref="IS7UaClient.Connected" />/>
    public event EventHandler<ConnectionEventArgs>? Connected;

    /// <inheritdoc cref="IS7UaClient.Disconnecting" />
    public event EventHandler<ConnectionEventArgs>? Disconnecting;

    /// <inheritdoc cref="IS7UaClient.Disconnected" />
    public event EventHandler<ConnectionEventArgs>? Disconnected;

    /// <inheritdoc cref="IS7UaClient.Reconnecting" />
    public event EventHandler<ConnectionEventArgs>? Reconnecting;

    /// <inheritdoc cref="IS7UaClient.Reconnected" />
    public event EventHandler<ConnectionEventArgs>? Reconnected;

    #endregion
    #endregion

    #region Public Properties

    /// <inheritdoc cref="IS7UaClient.KeepAliveInterval"/>
    public int KeepAliveInterval { get; set; } = 5000;

    /// <inheritdoc cref="IS7UaClient.ReconnectPeriod"/>
    public int ReconnectPeriod { get; set; } = 1000;

    /// <inheritdoc cref="IS7UaClient.ReconnectPeriodExponentialBackoff"/>/>
    public int ReconnectPeriodExponentialBackoff { get; set; } = -1;

    /// <inheritdoc cref="IS7UaClient.SessionTimeout"/>
    public uint SessionTimeout { get; set; } = 60000;

    /// <inheritdoc cref="IS7UaClient.AcceptUntrustedCertificates"/>
    public bool AcceptUntrustedCertificates { get; set; } = false;

    /// <inheritdoc cref="IS7UaClient.UserIdentity"/>
    public UserIdentity UserIdentity { get; set; } = new UserIdentity();

    /// <inheritdoc cref="IS7UaClient.IsConnected"/>
    public bool IsConnected => _session?.Connected == true;

    #endregion

    #region Public Methods
    #region Connection Methods

    /// <inheritdoc cref="IS7UaClient.ConnectAsync(string, bool, CancellationToken)"/>
    public async Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(S7UaClient));
        ArgumentException.ThrowIfNullOrEmpty(serverUrl, nameof(serverUrl));

        if (_session?.Connected == true)
        {
            _logger?.LogWarning("Already connected to the server.");
            return;
        }

        OnConnecting(ConnectionEventArgs.Empty);

        var endpointDescription = CoreClientUtils.SelectEndpoint(_appConfig, serverUrl, useSecurity);
        var endpointConfig = EndpointConfiguration.Create(_appConfig);
        var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig);

        var sessionFactory = TraceableSessionFactory.Instance;
        var session = await sessionFactory.CreateAsync(
            _appConfig,
            endpoint,
            true,
            false,
            _appConfig.ApplicationName,
            SessionTimeout,
            UserIdentity,
            null,
            cancellationToken
        ).ConfigureAwait(false);

        if (session?.Connected != true)
        {
            _logger?.LogError("Failed to connect to the S7 UA server.");
            return;
        }

        _session = session;

        _session.KeepAliveInterval = KeepAliveInterval;
        _session.DeleteSubscriptionsOnClose = false;
        _session.TransferSubscriptionsOnReconnect = true;

        _session.KeepAlive += Session_KeepAlive;

        _reconnectHandler = new SessionReconnectHandler(true, ReconnectPeriodExponentialBackoff);

        OnConnected(ConnectionEventArgs.Empty);
    }

    /// <inheritdoc cref="IS7UaClient.Disconnect(bool)"/>
    public void Disconnect(bool leaveChannelOpen = false)
    {
        if(_session != null)
        {
            OnDisconnecting(ConnectionEventArgs.Empty);

            lock(_sessionLock)
            {
                _session.KeepAlive -= Session_KeepAlive;
                _reconnectHandler?.Dispose();
                _reconnectHandler = null;
            }

            _session.Close(!leaveChannelOpen);

            if(leaveChannelOpen)
            {
                _session.DetachChannel();
            }

            _session.Dispose();
            _session = null;

            OnDisconnected(ConnectionEventArgs.Empty);
        }
    }

    #endregion

    #region Discovery Methods


    #endregion
    #endregion

    #region Event Callbacks
    private void Session_KeepAlive(ISession session, KeepAliveEventArgs e)
    {
        if (_session?.Equals(session) != true)
        {
            return;
        }

        if (ServiceResult.IsBad(e.Status))
        {
            if (ReconnectPeriod <= 0)
            {
                return;
            }

            OnReconnecting(new ConnectionEventArgs(e.Status.StatusCode));

            var state = _reconnectHandler?.BeginReconnect(_session, ReconnectPeriod, Client_ReconnectComplete);

            switch (state)
            {
                case SessionReconnectHandler.ReconnectState.Triggered:
                    _logger?.LogInformation("Reconnection triggered.");
                    break;
                case SessionReconnectHandler.ReconnectState.Ready:
                    _logger?.LogError("Reconnection ready.");
                    break;
                case SessionReconnectHandler.ReconnectState.Reconnecting:
                    _logger?.LogWarning("Reconnection in progress...");
                    break;
            }

            e.CancelKeepAlive = true;
        }
    }

    private void Client_ReconnectComplete(object? sender, EventArgs e)
    {
        if (!Object.ReferenceEquals(sender, _reconnectHandler))
        {
            return;
        }

        lock (_sessionLock)
        {
            if (_reconnectHandler?.Session != null)
            {
                if (!Object.ReferenceEquals(_session, _reconnectHandler.Session))
                {
                    //reconnected to a new session
                    _logger?.LogInformation("Reconnected to S7 UA server with a new session.");
                    var oldSession = _session;
                    _session = _reconnectHandler.Session;
                    Utils.SilentDispose(oldSession);
                }
                else
                {
                    //reconnected to the same session
                    _logger?.LogInformation("Reconnected to S7 UA server with the same session.");
                }
            }
            else
            {
                //reconnection stopped
                _logger?.LogInformation("KeepAlive recovered - Reconnection stopped.");
            }

            OnReconnected(new ConnectionEventArgs());
        }
    }

    #endregion

    #region Event Dispatchers
    private void OnConnecting(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Connecting to S7 UA server...");
        Connecting?.Invoke(this, e);
    }

    private void OnConnected(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Connected to S7 UA server successfully.");
        Connected?.Invoke(this, e);
    }

    private void OnDisconnecting(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Disconnecting from S7 UA server...");
        Disconnecting?.Invoke(this, e);
    }

    private void OnDisconnected(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Disconnected from S7 UA server.");
        Disconnected?.Invoke(this, e);
    }

    private void OnReconnecting(ConnectionEventArgs e)
    {
        Reconnecting?.Invoke(this, e);
    }

    private void OnReconnected(ConnectionEventArgs e)
    {
        Reconnected?.Invoke(this, e);
    }
    #endregion
}