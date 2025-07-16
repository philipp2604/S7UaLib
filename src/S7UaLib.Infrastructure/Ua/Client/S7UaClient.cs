using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.S7.Converters;
using S7UaLib.Infrastructure.Ua.Converters;
using System.Collections;
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;

namespace S7UaLib.Infrastructure.Ua.Client;

/// <summary>
/// Represents a client for connecting to and interacting with an S7 UA server.
/// </summary>
/// <remarks>The <see cref="S7UaClient"/> class provides methods and properties for establishing and managing a
/// session with an S7 UA server, including connection, disconnection, and reconnection handling. It supports
/// configurable keep-alive intervals, session timeouts, and reconnection strategies.
internal class S7UaClient : IS7UaClient, IDisposable
{
    #region Private Fields

    private readonly ILogger? _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private SessionReconnectHandler? _reconnectHandler;
    private ISession? _session;
    private Opc.Ua.Configuration.ApplicationInstance? _appInst;
    private readonly Action<IList, IList> _validateResponse;
    private bool _disposed;
    private readonly SemaphoreSlim _sessionSemaphore = new(1, 1);
    private Subscription? _subscription;
    private readonly Dictionary<NodeId, MonitoredItem> _monitoredItems = [];
    private readonly SemaphoreSlim _subscriptionSemaphore = new(1, 1);

    private static readonly NodeId _dataBlocksGlobalRootNode = new("DataBlocksGlobal", 3);
    private static readonly NodeId _dataBlocksInstanceRootNode = new("DataBlocksInstance", 3);
    private static readonly NodeId _memoryRootNode = new("Memory", 3);
    private static readonly NodeId _inputsRootNode = new("Inputs", 3);
    private static readonly NodeId _outputsRootNode = new("Outputs", 3);
    private static readonly NodeId _timersRootNode = new("Timers", 3);
    private static readonly NodeId _countersRootNode = new("Counters", 3);

    #region Instance Type Converters

    private readonly S7CharConverter _charConverterInstance;
    private readonly S7WCharConverter _wCharConverterInstance;
    private readonly S7DateConverter _dateConverterInstance;
    private readonly S7TimeConverter _timeConverterInstance;
    private readonly S7LTimeConverter _lTimeConverterInstance;
    private readonly S7DateAndTimeConverter _dateAndTimeConverterInstance;
    private readonly S7TimeOfDayConverter _timeOfDayConverterInstance;
    private readonly S7LTimeOfDayConverter _lTimeOfDayConverterInstance;
    private readonly S7S5TimeConverter _s5TimeConverterInstance;
    private readonly S7DTLConverter _dtlConverterInstance;
    private readonly S7CounterConverter _counterConverterInstance;

    private readonly Dictionary<S7DataType, IS7TypeConverter> _typeConvertersInstance;

    #endregion Instance Type Converters

    #endregion Private Fields

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="S7UaClient"/> class with the specified application configuration,
    /// response validation action, and optional logger factory.
    /// </summary>
    /// <param name="userIdentity">The <see cref="Core.Ua.UserIdentity"/> used for authentification. If <see langword="null"/>, anonymous login will be used.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="appConfig"/> or <paramref name="validateResponse"/> is <see langword="null"/>.</exception>
    public S7UaClient(Core.Ua.UserIdentity? userIdentity = null, ILoggerFactory? loggerFactory = null) : this(userIdentity, ClientBase.ValidateResponse, loggerFactory)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="S7UaClient"/> class with the specified application configuration,
    /// response validation action, and optional logger factory.
    /// </summary>
    /// <param name="userIdentity">The <see cref="Core.Ua.UserIdentity"/> used for authentification. If <see langword="null"/>, anonymous login will be used.</param>
    /// <param name="validateResponse">A delegate that validates the response. This parameter cannot be <see langword="null"/>.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="appConfig"/> or <paramref name="validateResponse"/> is <see langword="null"/>.</exception>
    public S7UaClient(Core.Ua.UserIdentity? userIdentity, Action<IList, IList>? validateResponse, ILoggerFactory? loggerFactory = null)
    {
        UserIdentity = userIdentity ?? new Core.Ua.UserIdentity();

        _validateResponse = validateResponse ?? throw new ArgumentNullException(nameof(validateResponse));
        _loggerFactory = loggerFactory;

        if (_loggerFactory != null)
        {
            _logger = _loggerFactory.CreateLogger<S7UaClient>();
        }

        // Initialize instance converters
        _charConverterInstance = new S7CharConverter();
        _wCharConverterInstance = new S7WCharConverter();
        _dateConverterInstance = new S7DateConverter();
        _timeConverterInstance = new S7TimeConverter();
        _lTimeConverterInstance = new S7LTimeConverter();
        _dateAndTimeConverterInstance = new S7DateAndTimeConverter();
        _timeOfDayConverterInstance = new S7TimeOfDayConverter();
        _lTimeOfDayConverterInstance = new S7LTimeOfDayConverter();
        _s5TimeConverterInstance = new S7S5TimeConverter();
        _dtlConverterInstance = new S7DTLConverter();
        _counterConverterInstance = new S7CounterConverter();

        _typeConvertersInstance = new Dictionary<S7DataType, IS7TypeConverter>
        {
            [S7DataType.CHAR] = _charConverterInstance,
            [S7DataType.WCHAR] = _wCharConverterInstance,
            [S7DataType.DATE] = _dateConverterInstance,
            [S7DataType.TIME] = _timeConverterInstance,
            [S7DataType.LTIME] = _lTimeConverterInstance,
            [S7DataType.TIME_OF_DAY] = _timeOfDayConverterInstance,
            [S7DataType.LTIME_OF_DAY] = _lTimeOfDayConverterInstance,
            [S7DataType.S5TIME] = _s5TimeConverterInstance,
            [S7DataType.DATE_AND_TIME] = _dateAndTimeConverterInstance,
            [S7DataType.DTL] = _dtlConverterInstance,
            [S7DataType.COUNTER] = _counterConverterInstance,
            [S7DataType.ARRAY_OF_CHAR] = new S7ElementwiseArrayConverter(_charConverterInstance, typeof(byte)),
            [S7DataType.ARRAY_OF_WCHAR] = new S7ElementwiseArrayConverter(_wCharConverterInstance, typeof(ushort)),
            [S7DataType.ARRAY_OF_DATE] = new S7ElementwiseArrayConverter(_dateConverterInstance, typeof(ushort)),
            [S7DataType.ARRAY_OF_TIME] = new S7ElementwiseArrayConverter(_timeConverterInstance, typeof(int)),
            [S7DataType.ARRAY_OF_LTIME] = new S7ElementwiseArrayConverter(_lTimeConverterInstance, typeof(long)),
            [S7DataType.ARRAY_OF_TIME_OF_DAY] = new S7ElementwiseArrayConverter(_timeOfDayConverterInstance, typeof(uint)),
            [S7DataType.ARRAY_OF_LTIME_OF_DAY] = new S7ElementwiseArrayConverter(_lTimeOfDayConverterInstance, typeof(ulong)),
            [S7DataType.ARRAY_OF_S5TIME] = new S7ElementwiseArrayConverter(_s5TimeConverterInstance, typeof(ushort)),
            [S7DataType.ARRAY_OF_DATE_AND_TIME] = new S7ElementwiseArrayConverter(_dateAndTimeConverterInstance, typeof(byte)),
            [S7DataType.ARRAY_OF_DTL] = new S7ElementwiseArrayConverter(_dtlConverterInstance, typeof(byte[])),
            [S7DataType.ARRAY_OF_COUNTER] = new S7ElementwiseArrayConverter(_counterConverterInstance, typeof(ushort))
        };
    }

    #endregion Constructors

    #region Deconstructors

    ~S7UaClient()
    {
        Dispose(false);
    }

    #endregion Deconstructors

    #region Disposing

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (_session != null)
            {
                if (_session.Connected)
                    DisconnectCore();
                _session.Dispose();
            }

            if (_appInst != null)
            {
                _appInst.ApplicationConfiguration.CertificateValidator.CertificateValidation -= Client_CertificateValidation;
            }

            _sessionSemaphore.Dispose();
            _subscriptionSemaphore.Dispose();

            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    #endregion Disposing

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

    #endregion Connection Events

    #region Subscription Events

    /// <inheritdoc cref="IS7UaClient.MonitoredItemChanged"/>
    public event EventHandler<MonitoredItemChangedEventArgs>? MonitoredItemChanged;

    #endregion Subscription Events

    #endregion Public Events

    #region Public Properties

    /// <inheritdoc cref="IS7UaClient.KeepAliveInterval"/>
    public int KeepAliveInterval { get; set; } = 5000;

    /// <inheritdoc cref="IS7UaClient.ReconnectPeriod"/>
    public int ReconnectPeriod { get; set; } = 1000;

    /// <inheritdoc cref="IS7UaClient.ReconnectPeriodExponentialBackoff"/>/>
    public int ReconnectPeriodExponentialBackoff { get; set; } = -1;

    /// <inheritdoc cref="IS7UaClient.UserIdentity"/>
    public Core.Ua.UserIdentity UserIdentity { get; }

    /// <inheritdoc cref="IS7UaClient.IsConnected"/>
    public bool IsConnected => _session?.Connected == true;

    #endregion Public Properties

    #region Public Methods

    #region Configuration Methods


    /// <inheritdoc cref="IS7UaClient.ConfigureAsync(string, string, string, Core.Ua.Configuration.SecurityConfiguration, S7UaLib.Core.Ua.Configuration.ClientConfiguration?, S7UaLib.Core.Ua.Configuration.TransportQuotas?, S7UaLib.Core.Ua.Configuration.OperationLimits?)"/>
    public async Task ConfigureAsync(string appName, string appUri, string productUri, Core.Ua.Configuration.SecurityConfiguration securityConfiguration, Core.Ua.Configuration.ClientConfiguration? clientConfig = null, Core.Ua.Configuration.TransportQuotas? transportQuotas = null, Core.Ua.Configuration.OperationLimits? opLimits = null)
    {
        ThrowIfDisposed();
        await BuildClientAsync(appName, appUri, productUri, securityConfiguration, clientConfig, transportQuotas, opLimits);
    }

    /// <inheritdoc cref="IS7UaClient.SaveConfiguration(string)"/>
    public void SaveConfiguration(string filePath)
    {
        ThrowIfDisposed();
        ThrowIfNotConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        _appInst?.ApplicationConfiguration.SaveToFile(filePath);
    }

    /// <inheritdoc cref="IS7UaClient.LoadConfigurationAsync(string)"/>
    public async Task LoadConfigurationAsync(string filePath)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        ArgumentNullException.ThrowIfNull(_appInst, nameof(_appInst));

        await _appInst.LoadApplicationConfiguration(filePath, false);

        _appInst.ApplicationConfiguration.CertificateValidator.CertificateValidation += Client_CertificateValidation;
    }

    /// <inheritdoc cref="IS7UaClient.AddTrustedCertificateAsync(X509Certificate2, CancellationToken)"/>
    public async Task AddTrustedCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotConfigured();

        await _appInst!.AddOwnCertificateToTrustedStoreAsync(certificate, cancellationToken);
        _appInst.ApplicationConfiguration.SecurityConfiguration.AddTrustedPeer(certificate.GetRawCertData());
        await _appInst.ApplicationConfiguration.CertificateValidator.UpdateAsync(_appInst.ApplicationConfiguration.SecurityConfiguration);
    }

    internal async Task BuildClientAsync(string appName, string appUri, string productUri, Core.Ua.Configuration.SecurityConfiguration securityConfiguration, Core.Ua.Configuration.ClientConfiguration? clientConfig = null, Core.Ua.Configuration.TransportQuotas? transportQuotas = null, Core.Ua.Configuration.OperationLimits? opLimits = null)
    {
        _appInst = new()
        {
            ApplicationName = appName,
            ApplicationType = Opc.Ua.ApplicationType.Client
        };

        var build = _appInst.Build(appUri, productUri);

        if (transportQuotas != null)
        {
            build.SetChannelLifetime((int)transportQuotas.ChannelLifetime)
                .SetMaxStringLength((int)transportQuotas.MaxStringLength)
                .SetMaxByteStringLength((int)transportQuotas.MaxByteStringLength)
                .SetMaxArrayLength((int)transportQuotas.MaxArrayLength)
                .SetMaxBufferSize((int)transportQuotas.MaxBufferSize)
                .SetMaxDecoderRecoveries((int)transportQuotas.MaxDecoderRecoveries)
                .SetMaxEncodingNestingLevels((int)transportQuotas.MaxEncodingNestingLevels)
                .SetMaxMessageSize((int)transportQuotas.MaxMessageSize)
                .SetOperationTimeout((int)transportQuotas.OperationTimeout)
                .SetSecurityTokenLifetime((int)transportQuotas.SecurityTokenLifetime);
        }

        var clientBuild = build.AsClient();

        clientConfig ??= new();
        foreach (var uri in clientConfig.WellKnownDiscoveryUrls)
        {
            clientBuild.AddWellKnownDiscoveryUrls(uri);
        }

        if (opLimits != null)
        {
            clientBuild.SetClientOperationLimits(
                new Opc.Ua.OperationLimits()
                {
                    MaxMonitoredItemsPerCall = opLimits.MaxMonitoredItemsPerCall,
                    MaxNodesPerBrowse = opLimits.MaxNodesPerBrowse,
                    MaxNodesPerHistoryReadData = opLimits.MaxNodesPerHistoryReadData,
                    MaxNodesPerHistoryReadEvents = opLimits.MaxNodesPerHistoryReadEvents,
                    MaxNodesPerHistoryUpdateData = opLimits.MaxNodesPerHistoryUpdateData,
                    MaxNodesPerHistoryUpdateEvents = opLimits.MaxNodesPerHistoryUpdateEvents,
                    MaxNodesPerMethodCall = opLimits.MaxNodesPerMethodCall,
                    MaxNodesPerNodeManagement = opLimits.MaxNodesPerNodeManagement,
                    MaxNodesPerRead = opLimits.MaxNodesPerRead,
                    MaxNodesPerRegisterNodes = opLimits.MaxNodesPerRegisterNodes,
                    MaxNodesPerTranslateBrowsePathsToNodeIds = opLimits.MaxNodesPerTranslateBrowsePathsToNodeIds,
                    MaxNodesPerWrite = opLimits.MaxNodesPerWrite
                });
        }

        var finalBuild = clientBuild.SetDefaultSessionTimeout((int)clientConfig.SessionTimeout)
            .SetMinSubscriptionLifetime((int)clientConfig.MinSubscriptionLifetime)
        .AddSecurityConfigurationStores(
            securityConfiguration.SecurityConfigurationStores.SubjectName,
            securityConfiguration.SecurityConfigurationStores.AppRoot,
            securityConfiguration.SecurityConfigurationStores.TrustedRoot,
            securityConfiguration.SecurityConfigurationStores.IssuerRoot,
            securityConfiguration.SecurityConfigurationStores.RejectedRoot)
        .SetSendCertificateChain(securityConfiguration.SendCertificateChain)
        .SetAddAppCertToTrustedStore(securityConfiguration.AddAppCertToTrustedStore)
        .SetAutoAcceptUntrustedCertificates(securityConfiguration.AutoAcceptUntrustedCertificates)
        .SetMaxRejectedCertificates((int)securityConfiguration.MaxRejectedCertificates)
        .SetMinimumCertificateKeySize((ushort)securityConfiguration.MinCertificateKeySize)
        .SetRejectSHA1SignedCertificates(securityConfiguration.RejectSHA1SignedCertificates)
        .SetRejectUnknownRevocationStatus(securityConfiguration.RejectUnknownRevocationStatus)
        .SetSuppressNonceValidationErrors(securityConfiguration.SuppressNonceValidationErrors)
        .SetUseValidatedCertificates(securityConfiguration.UseValidatedCertificates);

        await finalBuild.Create();

        if (!await _appInst.CheckApplicationInstanceCertificates(false).ConfigureAwait(false))
        {
            throw new SystemException("Application instance certificate invalid!");
        }

        _appInst.ApplicationConfiguration.CertificateValidator.CertificateValidation += Client_CertificateValidation;
    }

    #endregion Configuration Methods

    #region Connection Methods

    /// <inheritdoc cref="IS7UaClient.ConnectAsync(string, bool, CancellationToken)"/>
    public async Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotConfigured();
        ArgumentException.ThrowIfNullOrEmpty(serverUrl, nameof(serverUrl));

        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session?.Connected == true)
            {
                _logger?.LogWarning("Already connected to the server.");
                return;
            }

            OnConnecting(ConnectionEventArgs.Empty);

            var endpointDescription = CoreClientUtils.SelectEndpoint(_appInst!.ApplicationConfiguration, serverUrl, useSecurity);

            // Validate server certificate
            var serverCert = endpointDescription.ServerCertificate;
            var serverCertId = new CertificateIdentifier(serverCert);
            _appInst.ApplicationConfiguration.CertificateValidator.Validate(serverCertId.Certificate);

            var endpointConfig = EndpointConfiguration.Create(_appInst!.ApplicationConfiguration);
            var endpoint = new ConfiguredEndpoint(null, endpointDescription, endpointConfig);

            Opc.Ua.UserIdentity identity = UserIdentity.Username == null && UserIdentity.Password == null
                ? new Opc.Ua.UserIdentity()
                : new Opc.Ua.UserIdentity(UserIdentity.Username, UserIdentity.Password);

            var sessionFactory = TraceableSessionFactory.Instance;
            var session = await sessionFactory.CreateAsync(
                _appInst!.ApplicationConfiguration,
                endpoint,
                true,
                false,
                _appInst!.ApplicationConfiguration.ApplicationName,
                (uint)_appInst!.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout,
                identity,
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
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.DisconnectAsync(bool, CancellationToken)"/>
    public async Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisconnectCore(leaveChannelOpen);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private void DisconnectCore(bool leaveChannelOpen = false)
    {
        if (_session != null)
        {
            OnDisconnecting(ConnectionEventArgs.Empty);

            if (_subscription != null)
            {
                _session.RemoveSubscription(_subscription);
                _subscription.Dispose();
                _subscription = null;
                _monitoredItems.Clear();
            }

            _session.KeepAlive -= Session_KeepAlive;
            _reconnectHandler?.Dispose();
            _reconnectHandler = null;

            _session.Close(!leaveChannelOpen);

            if (leaveChannelOpen)
            {
                _session.DetachChannel();
            }

            _session.Dispose();
            _session = null;

            OnDisconnected(ConnectionEventArgs.Empty);
        }
    }

    #endregion Connection Methods

    #region Structure Browsing and Discovery Methods

    /// <inheritdoc cref="IS7UaClient.GetAllGlobalDataBlocksAsync(CancellationToken)"/>
    public async Task<IReadOnlyList<S7DataBlockGlobal>> GetAllGlobalDataBlocksAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return GetAllStructureElementsCore<S7DataBlockGlobal>(_dataBlocksGlobalRootNode, NodeClass.Object);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.GetAllInstanceDataBlocksAsync(CancellationToken)"/>
    public async Task<IReadOnlyList<S7DataBlockInstance>> GetAllInstanceDataBlocksAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected)
            {
                _logger?.LogError("Cannot get instance data blocks; session is not connected.");
                return [];
            }

            var browser = new Browser(_session)
            {
                BrowseDirection = BrowseDirection.Forward,
                NodeClassMask = (int)NodeClass.Object,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true
            };
            ReferenceDescriptionCollection descriptions = browser.Browse(_dataBlocksInstanceRootNode);

            return descriptions
                .Select(desc => new S7DataBlockInstance { NodeId = ((NodeId)desc.NodeId).ToString(), DisplayName = desc.DisplayName.Text })
                .ToList()
                .AsReadOnly();
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.GetMemoryAsync(CancellationToken)"/>
    public async Task<IS7Memory?> GetMemoryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return GetSingletonStructureElementCore<S7Memory>(_memoryRootNode);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.GetInputsAsync(CancellationToken)"/>
    public async Task<IS7Inputs?> GetInputsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return GetSingletonStructureElementCore<S7Inputs>(_inputsRootNode);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.GetOutputsAsync(CancellationToken)"/>
    public async Task<IS7Outputs?> GetOutputsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return GetSingletonStructureElementCore<S7Outputs>(_outputsRootNode);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.GetTimersAsync(CancellationToken)"/>
    public async Task<IS7Timers?> GetTimersAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return GetSingletonStructureElementCore<S7Timers>(_timersRootNode);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.GetCountersAsync(CancellationToken)"/>
    public async Task<IS7Counters?> GetCountersAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return GetSingletonStructureElementCore<S7Counters>(_countersRootNode);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.DiscoverElementAsync(IUaElement, CancellationToken)"/>
    public async Task<IUaNode?> DiscoverElementAsync(IUaNode elementShell, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (elementShell is null)
        {
            _logger?.LogWarning("DiscoverElement was called with a null element shell.");
            return null;
        }

        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return DiscoverElementCore(elementShell);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private IUaNode? DiscoverElementCore(IUaNode elementShell)
    {
        return elementShell switch
        {
            S7DataBlockInstance idb => DiscoverInstanceOfDataBlockCore(idb),
            S7StructureElement simpleElement => DiscoverVariablesOfElementCore((dynamic)simpleElement),
            _ => new Func<IUaNode?>(() =>
            {
                _logger?.LogWarning("DiscoverElement was called with an unsupported element type: {ElementType}", elementShell.GetType().Name);
                return null;
            })(),
        };
    }

    /// <inheritdoc cref="IS7UaClient.DiscoverVariablesOfElementAsync(T, CancellationToken)"/>
    public async Task<T> DiscoverVariablesOfElementAsync<T>(T element, CancellationToken cancellationToken = default) where T : S7StructureElement
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return DiscoverVariablesOfElementCore(element);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private T DiscoverVariablesOfElementCore<T>(T element) where T : S7StructureElement
    {
        if (element?.NodeId is null)
        {
            _logger?.LogWarning("Cannot discover variables for element of type {ElementType} because it or its NodeId is null.", typeof(T).Name);
            return element!;
        }
        if (!IsConnected)
        {
            _logger?.LogError("Cannot discover variables for '{DisplayName}'; session is not connected.", element.DisplayName);
            return element;
        }

        var browser = new Browser(_session)
        {
            BrowseDirection = BrowseDirection.Forward,
            NodeClassMask = (int)NodeClass.Variable,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true
        };
        ReferenceDescriptionCollection variableDescriptions = browser.Browse(element.NodeId);

        var discoveredVariables = variableDescriptions
            .Where(desc => desc.DisplayName.Text != "Icon")
            .Select(desc => new S7Variable { NodeId = ((NodeId)desc.NodeId).ToString(), DisplayName = desc.DisplayName.Text }).ToList();

        if (element.DisplayName == "Counters")
        {
            discoveredVariables = discoveredVariables
                .ConvertAll(variable => variable with { S7Type = S7DataType.COUNTER })
;
        }
        else if (element.DisplayName == "Timers")
        {
            discoveredVariables = discoveredVariables
                .ConvertAll(variable => variable with { S7Type = S7DataType.S5TIME })
;
        }

        return element with { Variables = discoveredVariables };
    }

    /// <inheritdoc cref="IS7UaClient.DiscoverInstanceOfDataBlockAsync(S7DataBlockInstance, CancellationToken)"/>
    public async Task<IS7DataBlockInstance> DiscoverInstanceOfDataBlockAsync(S7DataBlockInstance instanceDbShell, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return DiscoverInstanceOfDataBlockCore(instanceDbShell);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private S7DataBlockInstance DiscoverInstanceOfDataBlockCore(S7DataBlockInstance instanceDbShell)
    {
        if (instanceDbShell?.NodeId is null)
        {
            _logger?.LogWarning("Cannot discover instance DB because the provided shell or its NodeId is null.");
            return instanceDbShell ?? new S7DataBlockInstance();
        }
        if (!IsConnected)
        {
            _logger?.LogError("Cannot discover instance DB '{DisplayName}'; session is not connected.", instanceDbShell.DisplayName);
            return instanceDbShell;
        }

        var browser = new Browser(_session)
        {
            BrowseDirection = BrowseDirection.Forward,
            NodeClassMask = (int)NodeClass.Object,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
        };
        var childNodes = browser.Browse(instanceDbShell.NodeId);

        S7InstanceDbSection? input = null, output = null, inOut = null, stat = null;
        foreach (var childNode in childNodes)
        {
            var sectionShell = new S7InstanceDbSection { NodeId = ((NodeId)childNode.NodeId).ToString(), DisplayName = childNode.DisplayName.Text };
            var populatedSection = PopulateInstanceSectionCore(sectionShell);
            switch (populatedSection.DisplayName)
            {
                case "Inputs": input = populatedSection; break;
                case "Outputs": output = populatedSection; break;
                case "InOuts": inOut = populatedSection; break;
                case "Static": stat = populatedSection; break;
            }
        }
        return instanceDbShell with { Inputs = input, Outputs = output, InOuts = inOut, Static = stat };
    }

    #endregion Structure Browsing and Discovery Methods

    #region Reading and Writing Methods

    #region Reading Methods

    /// <inheritdoc cref="IS7UaClient.ReadValuesOfElementAsync{T}(T, string?, CancellationToken)"/>
    public async Task<T> ReadValuesOfElementAsync<T>(T elementWithStructure, string? rootContextName = null, CancellationToken cancellationToken = default) where T : IUaNode
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return ReadValuesOfElementCore(elementWithStructure, rootContextName);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private T ReadValuesOfElementCore<T>(T elementWithStructure, string? rootContextName = null) where T : IUaNode
    {
        if (elementWithStructure?.NodeId is null)
        {
            _logger?.LogWarning("ReadValuesOfElement called with a null element or element with a null NodeId.");
            return elementWithStructure!;
        }
        if (!IsConnected || _session is null)
        {
            _logger?.LogError("Cannot read values for '{DisplayName}'; session is not connected.", elementWithStructure.DisplayName);
            return elementWithStructure;
        }

        string initialPathPrefix = BuildInitialPath(elementWithStructure, rootContextName);

        var nodesToReadCollector = new Dictionary<NodeId, S7Variable>();
        CollectNodesToReadRecursivelyCore(elementWithStructure, nodesToReadCollector, initialPathPrefix);

        var readResultsMap = new Dictionary<NodeId, DataValue>();
        if (nodesToReadCollector.Count > 0)
        {
            var nodesToRead = new ReadValueIdCollection(nodesToReadCollector.Keys.Select(nodeId => new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value }));
            _session.Read(null, 0, TimestampsToReturn.Neither, nodesToRead, out var results, out _);
            _validateResponse(results, nodesToRead);

            for (int i = 0; i < nodesToRead.Count; i++)
            {
                if (nodesToRead[i].NodeId != null) readResultsMap[nodesToRead[i].NodeId] = results[i];
            }
        }

        return (T)RebuildHierarchyWithValuesRecursivelyCore(elementWithStructure, readResultsMap, initialPathPrefix);
    }

    #endregion Reading Methods

    #region Subscription Methods

    /// <inheritdoc cref="IS7UaClient.CreateSubscriptionAsync(int)"/>
    public async Task<bool> CreateSubscriptionAsync(int publishingInterval = 100)
    {
        ThrowIfDisposed();
        if (!IsConnected || _session is null)
        {
            _logger?.LogError("Cannot create subscription; session is not connected.");
            return false;
        }

        await _subscriptionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_subscription != null)
            {
                return true;
            }

            _subscription = CreateNewSubscription(publishingInterval);

            _session.AddSubscription(_subscription);
            await CreateSubscriptionOnServerAsync(_subscription).ConfigureAwait(false);

            _logger?.LogInformation("Subscription created successfully with PublishingInterval={interval}ms.", publishingInterval);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create subscription.");
            return false;
        }
        finally
        {
            _subscriptionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.SubscribeToVariableAsync(IS7Variable)"/>
    public async Task<bool> SubscribeToVariableAsync(IS7Variable variable)
    {
        ThrowIfDisposed();
        if (_subscription is null)
        {
            _logger?.LogError("Cannot subscribe variable '{name}'. Subscription does not exist.", variable.DisplayName);
            return false;
        }
        if (variable.NodeId is null)
        {
            _logger?.LogWarning("Cannot subscribe variable '{name}'. NodeId is null.", variable.DisplayName);
            return false;
        }

        await _subscriptionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_monitoredItems.ContainsKey(variable.NodeId))
            {
                _logger?.LogInformation("Variable '{name}' is already subscribed.", variable.DisplayName);
                return true;
            }

            var item = new MonitoredItem(_subscription.DefaultItem)
            {
                DisplayName = variable.DisplayName,
                StartNodeId = variable.NodeId,
                AttributeId = Attributes.Value,
                SamplingInterval = (int)variable.SamplingInterval,
                QueueSize = 1,
                DiscardOldest = true,
                MonitoringMode = MonitoringMode.Reporting
            };

            item.Notification += OnMonitoredItemNotification;

            _monitoredItems.Add(variable.NodeId, item);
            _subscription.AddItem(item);
            await ApplySubscriptionChangesAsync(_subscription).ConfigureAwait(false);

            _logger?.LogDebug("Variable '{name}' ({nodeId}) subscribed.", variable.DisplayName, variable.NodeId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to subscribe to variable '{name}'.", variable.DisplayName);
            if (variable.NodeId is not null) _monitoredItems.Remove(variable.NodeId);
            return false;
        }
        finally
        {
            _subscriptionSemaphore.Release();
        }
    }

    /// <inheritdoc cref="IS7UaClient.UnsubscribeFromVariableAsync(IS7Variable)"/>
    public async Task<bool> UnsubscribeFromVariableAsync(IS7Variable variable)
    {
        ThrowIfDisposed();
        if (_subscription is null || variable.NodeId is null || !_monitoredItems.TryGetValue(variable.NodeId, out var item))
        {
            _logger?.LogWarning("Cannot unsubscribe variable '{name}'. It is not currently subscribed.", variable.DisplayName);
            return false;
        }

        await _subscriptionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            item.Notification -= OnMonitoredItemNotification;

            _subscription.RemoveItem(item);
            _monitoredItems.Remove(variable.NodeId);
            await ApplySubscriptionChangesAsync(_subscription).ConfigureAwait(false);

            _logger?.LogDebug("Variable '{name}' ({nodeId}) unsubscribed.", variable.DisplayName, variable.NodeId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to unsubscribe from variable '{name}'.", variable.DisplayName);
            return false;
        }
        finally
        {
            _subscriptionSemaphore.Release();
        }
    }

    #endregion Subscription Methods

    #region Writing Methods

    /// <inheritdoc cref="IS7UaClient.WriteVariableAsync(string, object, S7DataType, CancellationToken)"/>
    public async Task<bool> WriteVariableAsync(string nodeId, object value, S7DataType s7Type, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(value);

        var converter = this.GetConverter(s7Type, value.GetType());
        var opcValue = converter.ConvertToOpc(value) ?? throw new InvalidOperationException($"Conversion of value for S7Type {s7Type} resulted in null.");
        return await WriteRawVariableAsync(nodeId, opcValue, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="IS7UaClient.WriteVariableAsync(IS7Variable, object, CancellationToken)"/>
    public async Task<bool> WriteVariableAsync(IS7Variable variable, object value, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(value);
        return variable.NodeId is null
            ? throw new ArgumentException($"Variable '{variable.DisplayName}' has no NodeId and cannot be written to.", nameof(variable))
            : await WriteVariableAsync(variable.NodeId, value, variable.S7Type, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="IS7UaClient.WriteRawVariableAsync(string, object, CancellationToken)"/>
    public async Task<bool> WriteRawVariableAsync(string nodeId, object rawValue, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(rawValue);

        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!IsConnected || _session is null)
            {
                _logger?.LogError("Cannot write values for node id '{nodeId}'; session is not connected.", nodeId);
                return false;
            }

            var writeValue = new WriteValue
            {
                NodeId = new NodeId(nodeId),
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(rawValue))
            };
            var response = await _session.WriteAsync(null, [writeValue], cancellationToken).ConfigureAwait(false);
            _validateResponse(response.Results, new[] { writeValue });

            Opc.Ua.StatusCode writeResult = response.Results[0];
            if (Opc.Ua.StatusCode.IsGood(writeResult))
            {
                return true;
            }

            _logger?.LogError("Failed to write raw value to node {NodeId}. StatusCode: {StatusCode}", nodeId, writeResult);
            return false;
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    #endregion Writing Methods

    #region Reading and Writing Helpers

    public IS7TypeConverter GetConverter(S7DataType s7Type, Type fallbackType) =>
        _typeConvertersInstance.TryGetValue(s7Type, out var converter) ? converter : new DefaultConverter(fallbackType);

    #endregion Reading and Writing Helpers

    #endregion Reading and Writing Methods

    #endregion Public Methods

    #region Private Methods

    #region Reading and Writing Helpers

    private IUaNode RebuildHierarchyWithValuesRecursivelyCore(
        IUaNode templateElement,
        IReadOnlyDictionary<NodeId, DataValue> readResultsMap,
        string currentPath)
    {
        switch (templateElement)
        {
            case S7DataBlockInstance idb:
                var newInput = idb.Inputs != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursivelyCore(idb.Inputs, readResultsMap, $"{currentPath}.{idb.Inputs.DisplayName}") : null;
                var newOutput = idb.Outputs != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursivelyCore(idb.Outputs, readResultsMap, $"{currentPath}.{idb.Outputs.DisplayName}") : null;
                var newInOut = idb.InOuts != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursivelyCore(idb.InOuts, readResultsMap, $"{currentPath}.{idb.InOuts.DisplayName}") : null;
                var newStatic = idb.Static != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursivelyCore(idb.Static, readResultsMap, $"{currentPath}.{idb.Static.DisplayName}") : null;
                return idb with { Inputs = newInput, Outputs = newOutput, InOuts = newInOut, Static = newStatic };

            case S7InstanceDbSection section:
                var newVars = section.Variables.Select(v => (S7Variable)RebuildHierarchyWithValuesRecursivelyCore(v, readResultsMap, currentPath)).ToList();
                var newNested = section.NestedInstances.Select(n => (S7DataBlockInstance)RebuildHierarchyWithValuesRecursivelyCore(n, readResultsMap, $"{currentPath}.{n.DisplayName}")).ToList();
                return section with { Variables = newVars, NestedInstances = newNested };

            case S7StructureElement simpleElement:
                var newSimpleVars = simpleElement.Variables.Select(v => (S7Variable)RebuildHierarchyWithValuesRecursivelyCore(v, readResultsMap, currentPath)).ToList();
                return simpleElement with { Variables = newSimpleVars };

            case S7Variable variable:
                string fullPath = $"{currentPath}.{variable.DisplayName}";

                if (variable.S7Type == S7DataType.STRUCT)
                {
                    var discoveredMembers = DiscoverVariablesOfElementCore(new S7StructureElement { NodeId = variable.NodeId }).Variables;

                    var templateMembersByName = variable.StructMembers
                        .Where(m => m.DisplayName is not null)
                        .ToDictionary(m => m.DisplayName!, m => m);

                    var membersToProcess = discoveredMembers.Cast<S7Variable>().Select(discoveredMember =>
                    {
                        templateMembersByName.TryGetValue(discoveredMember.DisplayName ?? string.Empty, out var templateMember);

                        return (IS7Variable)(discoveredMember with
                        {
                            S7Type = templateMember?.S7Type ?? S7DataType.UNKNOWN,
                            StructMembers = templateMember?.StructMembers ?? []
                        });
                    });

                    var processedMembers = membersToProcess
                        .Select(m => (S7Variable)RebuildHierarchyWithValuesRecursivelyCore(m, readResultsMap, fullPath))
                        .ToList();

                    return variable with { FullPath = fullPath, StructMembers = processedMembers, StatusCode = UaStatusCodeConverter.Convert(StatusCodes.Good) };
                }

                if (variable.NodeId != null && readResultsMap.TryGetValue(variable.NodeId, out var dataValue))
                {
                    var converter = this.GetConverter(variable.S7Type, dataValue.Value?.GetType() ?? typeof(object));
                    var rawValue = dataValue.Value;
                    var finalValue = converter.ConvertFromOpc(rawValue);

                    return variable with
                    {
                        RawOpcValue = rawValue,
                        Value = finalValue,
                        SystemType = converter.TargetType,
                        FullPath = fullPath,
                        StatusCode = UaStatusCodeConverter.Convert(dataValue.StatusCode)
                    };
                }

                return variable with { FullPath = fullPath, StatusCode = UaStatusCodeConverter.Convert(StatusCodes.BadWaitingForInitialData) };

            default:
                _logger?.LogWarning("RebuildHierarchy encountered an unhandled element type: {ElementType}", templateElement.GetType().Name);
                return templateElement;
        }
    }

    private void CollectNodesToReadRecursivelyCore(IUaNode currentElement, IDictionary<NodeId, S7Variable> collectedNodes, string currentPath)
    {
        switch (currentElement)
        {
            case S7DataBlockInstance idb:
                if (idb.Inputs != null) CollectNodesToReadRecursivelyCore(idb.Inputs, collectedNodes, $"{currentPath}.{idb.Inputs.DisplayName}");
                if (idb.Outputs != null) CollectNodesToReadRecursivelyCore(idb.Outputs, collectedNodes, $"{currentPath}.{idb.Outputs.DisplayName}");
                if (idb.InOuts != null) CollectNodesToReadRecursivelyCore(idb.InOuts, collectedNodes, $"{currentPath}.{idb.InOuts.DisplayName}");
                if (idb.Static != null) CollectNodesToReadRecursivelyCore(idb.Static, collectedNodes, $"{currentPath}.{idb.Static.DisplayName}");
                break;

            case S7InstanceDbSection section:
                foreach (var variable in section.Variables) CollectNodesToReadRecursivelyCore(variable, collectedNodes, currentPath);
                foreach (var nestedIdb in section.NestedInstances) CollectNodesToReadRecursivelyCore(nestedIdb, collectedNodes, $"{currentPath}.{nestedIdb.DisplayName}");
                break;

            case S7StructureElement simpleElement:
                foreach (var variable in simpleElement.Variables) CollectNodesToReadRecursivelyCore(variable, collectedNodes, currentPath);
                break;

            case S7Variable variable:
                string fullPath = $"{currentPath}.{variable.DisplayName}";
                if (variable.S7Type == S7DataType.STRUCT)
                {
                    var discoveredMembers = DiscoverVariablesOfElementCore(new S7StructureElement { NodeId = variable.NodeId }).Variables;

                    if (variable.StructMembers.Any())
                    {
                        var templateMembersByName = variable.StructMembers.ToDictionary(m => m.DisplayName!, m => m);

                        foreach (var discoveredMember in discoveredMembers.Cast<S7Variable>())
                        {
                            if (templateMembersByName.TryGetValue(discoveredMember.DisplayName ?? string.Empty, out var templateMember))
                            {
                                var memberToRecurse = discoveredMember with
                                {
                                    S7Type = templateMember.S7Type,
                                    StructMembers = templateMember.StructMembers
                                };
                                CollectNodesToReadRecursivelyCore(memberToRecurse, collectedNodes, fullPath);
                            }
                            else
                            {
                                CollectNodesToReadRecursivelyCore(discoveredMember, collectedNodes, fullPath);
                            }
                        }
                    }
                    else
                    {
                        foreach (var member in discoveredMembers)
                        {
                            CollectNodesToReadRecursivelyCore(member, collectedNodes, fullPath);
                        }
                    }
                }
                else if (variable.NodeId != null)
                {
                    collectedNodes[variable.NodeId] = variable;
                }
                break;
        }
    }

    private static string BuildInitialPath(IUaNode element, string? rootContextName)
    {
        return string.IsNullOrEmpty(rootContextName)
            ? element.DisplayName ?? string.Empty
            : rootContextName.Equals(element.DisplayName, StringComparison.OrdinalIgnoreCase)
            ? rootContextName
            : $"{rootContextName}.{element.DisplayName}";
    }

    #endregion Reading and Writing Helpers

    #region Structure Browsing and Discovery Helpers

    private T? GetSingletonStructureElementCore<T>(NodeId node) where T : S7StructureElement, new()
    {
        if (!IsConnected || _session is null)
        {
            _logger?.LogError("Cannot get singleton element for node {NodeId}; session is not connected.", node);
            return null;
        }

        var nodeToRead = new ReadValueId { NodeId = node, AttributeId = Attributes.DisplayName };
        _session.Read(null, 0, TimestampsToReturn.Neither, [nodeToRead], out var results, out _);
        _validateResponse(results, new[] { nodeToRead });

        DataValue result = results[0];
        if (Opc.Ua.StatusCode.IsBad(result.StatusCode))
        {
            _logger?.LogWarning("Failed to read DisplayName for node {NodeId}. It may not exist on the server. StatusCode: {StatusCode}", node, result.StatusCode);
            return null;
        }

        string displayName = (result.Value as LocalizedText)?.Text ?? node.ToString();
        return new T { NodeId = node.ToString(), DisplayName = displayName };
    }

    private ReadOnlyCollection<T> GetAllStructureElementsCore<T>(NodeId rootNode, NodeClass expectedNodeClass) where T : S7StructureElement, new()
    {
        if (!IsConnected || _session is null)
        {
            _logger?.LogError("Cannot get structure elements for root {RootNode}; session is not connected.", rootNode);
            return new ReadOnlyCollection<T>([]);
        }

        var browser = new Browser(_session)
        {
            BrowseDirection = BrowseDirection.Forward,
            NodeClassMask = (int)expectedNodeClass,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true
        };
        ReferenceDescriptionCollection descriptions = browser.Browse(rootNode);

        return descriptions
            .Select(desc => new T { NodeId = ((NodeId)desc.NodeId).ToString(), DisplayName = desc.DisplayName.Text })
            .ToList()
            .AsReadOnly();
    }

    private S7InstanceDbSection PopulateInstanceSectionCore(S7InstanceDbSection sectionShell)
    {
        if (sectionShell?.NodeId is null) return sectionShell ?? new S7InstanceDbSection();
        if (!IsConnected || _session is null) return sectionShell;

#pragma warning disable RCS1130
        var browser = new Browser(_session)
        {
            BrowseDirection = BrowseDirection.Forward,
            NodeClassMask = (int)(NodeClass.Variable | NodeClass.Object),
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
        };
#pragma warning restore RCS1130
        ReferenceDescriptionCollection childNodes = browser.Browse(sectionShell.NodeId);

        var variables = new List<S7Variable>();
        var nestedInstances = new List<S7DataBlockInstance>();
        foreach (var childNode in childNodes)
        {
            //Filter out the Icon variable and process others
            if (childNode.NodeClass == NodeClass.Variable && childNode.DisplayName.Text != "Icon")
            {
                variables.Add(new S7Variable { NodeId = ((NodeId)childNode.NodeId).ToString(), DisplayName = childNode.DisplayName.Text });
            }
            else if (childNode.NodeClass == NodeClass.Object)
            {
                var nestedShell = new S7DataBlockInstance { NodeId = ((NodeId)childNode.NodeId).ToString(), DisplayName = childNode.DisplayName.Text };
                nestedInstances.Add(DiscoverInstanceOfDataBlockCore(nestedShell));
            }
        }
        return sectionShell with { Variables = variables, NestedInstances = nestedInstances };
    }

    #endregion Structure Browsing and Discovery Helpers

    #region Subscription Helpers

    protected virtual Task CreateSubscriptionOnServerAsync(Subscription subscription)
    {
        return subscription.CreateAsync();
    }

    protected virtual Subscription CreateNewSubscription(int publishingInterval)
    {
        return _session is null
            ? throw new InvalidOperationException("Session is not available to create a subscription.")
            : new Subscription(_session.DefaultSubscription)
            {
                PublishingInterval = publishingInterval,
                LifetimeCount = 600,
                MaxNotificationsPerPublish = 1000,
                TimestampsToReturn = TimestampsToReturn.Both
            };
    }

    protected virtual Task ApplySubscriptionChangesAsync(Subscription subscription)
    {
        return subscription.ApplyChangesAsync();
    }

    #endregion Subscription Helpers

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

            OnReconnecting(new ConnectionEventArgs(UaStatusCodeConverter.Convert(e.Status.StatusCode)));

            var state = _reconnectHandler?.BeginReconnect(_session, ReconnectPeriod, Client_ReconnectComplete);

            switch (state)
            {
                case SessionReconnectHandler.ReconnectState.Triggered:
                    _logger?.LogInformation("Reconnection triggered.");
                    break;

                case SessionReconnectHandler.ReconnectState.Ready:
                    _logger?.LogWarning("Reconnection handler is in 'Ready' state after BeginReconnect attempt. This might indicate automatic reconnection could not be triggered.");
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

        _sessionSemaphore.Wait();
        try
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
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private void Client_CertificateValidation(Opc.Ua.CertificateValidator certificateValidator, CertificateValidationEventArgs e)
    {
        var accepted = false;

        Opc.Ua.ServiceResult error = e.Error;
        _logger?.LogWarning($"Certificate validation error: {error.ToLongString()}");

        if (error.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted)
        {
            if (_appInst!.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                accepted = true;
            }
        }

        if (accepted)
        {
            _logger?.LogWarning($"Accepting untrusted certificate. Subject: {e.Certificate.Subject}");
            e.Accept = true;
        }
        else
        {
            _logger?.LogWarning($"Rejecting untrusted certificate. Subject: {e.Certificate.Subject}");
            e.Accept = false;
            _appInst!.ApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.OpenStore().Add(e.Certificate);
        }
    }

    #endregion Event Callbacks

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

    private void OnMonitoredItemNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
    {
        if (e.NotificationValue is MonitoredItemNotification notification)
        {
            MonitoredItemChanged?.Invoke(this, new MonitoredItemChangedEventArgs(monitoredItem, notification));
        }
    }

    #endregion Event Dispatchers

    #region Helpers

    private void ThrowIfNotConfigured()
    {
        ArgumentNullException.ThrowIfNull(_appInst);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(_appInst.ApplicationConfiguration.ApplicationName);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(_appInst.ApplicationConfiguration.ApplicationUri);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(_appInst.ApplicationConfiguration.ProductUri);
    }

    #endregion Helpers

    #endregion Private Methods
}