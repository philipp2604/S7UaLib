using Microsoft.Extensions.Logging;
using S7UaLib.Core.Enums;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.S7.Converters;
using S7UaLib.Infrastructure.Ua.Converters;
using System.Collections;
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;

namespace S7UaLib.Infrastructure.Ua.Client;

/// <summary>
/// S7 UA Client implementation with integrated session pool for improved performance.
/// Uses a main client for connections/subscriptions and a session pool for stateless operations.
/// </summary>
internal class S7UaClient : IS7UaClient, IDisposable
{
    #region Private Fields

    private readonly ILogger<S7UaClient>? _logger;
    private readonly IS7UaMainClient _mainClient;
    private readonly IS7UaSessionPool _sessionPool;
    private readonly Action<IList, IList> _validateResponse;
    private bool _disposed;

    // Event handler references for proper cleanup
    private readonly EventHandler<ConnectionEventArgs> _connectingHandler;

    private readonly EventHandler<ConnectionEventArgs> _connectedHandler;
    private readonly EventHandler<ConnectionEventArgs> _disconnectingHandler;
    private readonly EventHandler<ConnectionEventArgs> _disconnectedHandler;
    private readonly EventHandler<ConnectionEventArgs> _reconnectingHandler;
    private readonly EventHandler<MonitoredItemChangedEventArgs> _monitoredItemChangedHandler;

    // Static node references
    private static readonly Opc.Ua.NodeId _dataBlocksGlobalRootNode = new(S7StructureConstants._s7DataBlocksGlobalNamespaceIdentifier);

    private static readonly Opc.Ua.NodeId _dataBlocksInstanceRootNode = new(S7StructureConstants._s7DataBlocksInstanceNamespaceIdentifier);
    private static readonly Opc.Ua.NodeId _memoryRootNode = new(S7StructureConstants._s7MemoryNamespaceIdentifier);
    private static readonly Opc.Ua.NodeId _inputsRootNode = new(S7StructureConstants._s7InputsNamespaceIdentifier);
    private static readonly Opc.Ua.NodeId _outputsRootNode = new(S7StructureConstants._s7OutputsNamespaceIdentifier);
    private static readonly Opc.Ua.NodeId _timersRootNode = new(S7StructureConstants._s7TimersNamespaceIdentifier);
    private static readonly Opc.Ua.NodeId _countersRootNode = new(S7StructureConstants._s7CountersNamespaceIdentifier);

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
    /// Initializes a new instance of the <see cref="S7UaClient"/> class.
    /// </summary>
    /// <param name="userIdentity">The user identity for authentication.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public S7UaClient(UserIdentity? userIdentity = null, int maxSessions = 5, ILoggerFactory? loggerFactory = null)
        : this(userIdentity, maxSessions, Opc.Ua.ClientBase.ValidateResponse, loggerFactory)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="S7UaClient"/> class.
    /// </summary>
    /// <param name="userIdentity">The user identity for authentication.</param>
    /// <param name="validateResponse">Response validation callback.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public S7UaClient(UserIdentity? userIdentity, int maxSessions, Action<IList, IList>? validateResponse, ILoggerFactory? loggerFactory = null)
    {
        UserIdentity = userIdentity ?? new UserIdentity();
        _validateResponse = validateResponse ?? throw new ArgumentNullException(nameof(validateResponse));

        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<S7UaClient>();
        }

        // Create main client and session pool
        _mainClient = new S7UaMainClient(UserIdentity, _validateResponse, loggerFactory);
        _sessionPool = new S7UaSessionPool(UserIdentity, maxSessions, _validateResponse,
            loggerFactory?.CreateLogger<S7UaSessionPool>());

        // Initialize event handlers
        _connectingHandler = (sender, e) => Connecting?.Invoke(this, e);
        _connectedHandler = OnMainClientConnected;
        _disconnectingHandler = (sender, e) => Disconnecting?.Invoke(this, e);
        _disconnectedHandler = (sender, e) => Disconnected?.Invoke(this, e);
        _reconnectingHandler = (sender, e) => Reconnecting?.Invoke(this, e);
        _monitoredItemChangedHandler = (sender, e) => MonitoredItemChanged?.Invoke(this, e);

        // Subscribe to main client events
        _mainClient.Connecting += _connectingHandler;
        _mainClient.Connected += _connectedHandler;
        _mainClient.Disconnecting += _disconnectingHandler;
        _mainClient.Disconnected += _disconnectedHandler;
        _mainClient.Reconnecting += _reconnectingHandler;
        _mainClient.Reconnected += OnMainClientReconnected;
        _mainClient.MonitoredItemChanged += _monitoredItemChangedHandler;

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

    /// <summary>
    /// Internal constructor for unit testing with mocked dependencies.
    /// </summary>
    internal S7UaClient(
        IS7UaMainClient mainClient,
        IS7UaSessionPool sessionPool,
        UserIdentity? userIdentity,
        ILogger<S7UaClient>? logger,
        Action<IList, IList> validateResponse)
    {
        _logger = logger;
        _mainClient = mainClient;
        _sessionPool = sessionPool;
        _validateResponse = validateResponse;
        UserIdentity = userIdentity ?? new UserIdentity();

        _connectingHandler = (sender, e) => Connecting?.Invoke(this, e);
        _connectedHandler = OnMainClientConnected;
        _disconnectingHandler = (sender, e) => Disconnecting?.Invoke(this, e);
        _disconnectedHandler = (sender, e) => Disconnected?.Invoke(this, e);
        _reconnectingHandler = (sender, e) => Reconnecting?.Invoke(this, e);
        _monitoredItemChangedHandler = (sender, e) => MonitoredItemChanged?.Invoke(this, e);

        _mainClient.Connecting += _connectingHandler;
        _mainClient.Connected += _connectedHandler;
        _mainClient.Disconnecting += _disconnectingHandler;
        _mainClient.Disconnected += _disconnectedHandler;
        _mainClient.Reconnecting += _reconnectingHandler;
        _mainClient.Reconnected += OnMainClientReconnected;
        _mainClient.MonitoredItemChanged += _monitoredItemChangedHandler;

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

    #region Public Events

    #region Connection Events

    /// <inheritdoc cref="IS7UaClient.Connecting" />
    public event EventHandler<ConnectionEventArgs>? Connecting;

    /// <inheritdoc cref="IS7UaClient.Connected" />
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

    /// <inheritdoc/>
    public ApplicationConfiguration? ApplicationConfiguration => _mainClient.ApplicationConfiguration;

    /// <inheritdoc cref="IS7UaClient.KeepAliveInterval"/>
    public int KeepAliveInterval
    {
        get => _mainClient.KeepAliveInterval;
        set => _mainClient.KeepAliveInterval = value;
    }

    /// <inheritdoc cref="IS7UaClient.ReconnectPeriod"/>
    public int ReconnectPeriod
    {
        get => _mainClient.ReconnectPeriod;
        set => _mainClient.ReconnectPeriod = value;
    }

    /// <inheritdoc cref="IS7UaClient.ReconnectPeriodExponentialBackoff"/>
    public int ReconnectPeriodExponentialBackoff
    {
        get => _mainClient.ReconnectPeriodExponentialBackoff;
        set => _mainClient.ReconnectPeriodExponentialBackoff = value;
    }

    /// <inheritdoc cref="IS7UaClient.UserIdentity"/>
    public UserIdentity UserIdentity { get; }

    /// <inheritdoc cref="IS7UaClient.IsConnected"/>
    public bool IsConnected => _mainClient.IsConnected;

    #endregion Public Properties

    #region Public Methods

    #region Configuration Methods

    /// <inheritdoc/>
    public async Task ConfigureAsync(ApplicationConfiguration appConfig)
    {
        ThrowIfDisposed();
        await _mainClient.ConfigureAsync(appConfig);
    }

    /// <inheritdoc cref="IS7UaClient.SaveConfiguration(string)"/>
    public void SaveConfiguration(string filePath)
    {
        ThrowIfDisposed();
        _mainClient.SaveConfiguration(filePath);
    }

    /// <inheritdoc cref="IS7UaClient.LoadConfigurationAsync(string)"/>
    public async Task LoadConfigurationAsync(string filePath)
    {
        ThrowIfDisposed();
        await _mainClient.LoadConfigurationAsync(filePath);
    }

    /// <inheritdoc cref="IS7UaClient.AddTrustedCertificateAsync(X509Certificate2, CancellationToken)"/>
    public async Task AddTrustedCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mainClient.AddTrustedCertificateAsync(certificate, cancellationToken);
    }

    #endregion Configuration Methods

    #region Connection Methods

    /// <inheritdoc cref="IS7UaClient.ConnectAsync(string, bool, CancellationToken)"/>
    public async Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mainClient.ConnectAsync(serverUrl, useSecurity, cancellationToken);
    }

    /// <inheritdoc cref="IS7UaClient.DisconnectAsync(bool, CancellationToken)"/>
    public async Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _mainClient.DisconnectAsync(leaveChannelOpen, cancellationToken);
        _sessionPool.Dispose();
        Dispose();
    }

    #endregion Connection Methods

    #region Structure Browsing and Discovery Methods (delegated to session pool)

    /// <inheritdoc cref="IS7UaClient.GetAllGlobalDataBlocksAsync(CancellationToken)"/>
    public async Task<IReadOnlyList<S7DataBlockGlobal>> GetAllGlobalDataBlocksAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(GetAllStructureElementsCore<S7DataBlockGlobal>(session, _dataBlocksGlobalRootNode, Opc.Ua.NodeClass.Object)), cancellationToken);
    }

    /// <inheritdoc cref="IS7UaClient.GetAllInstanceDataBlocksAsync(CancellationToken)"/>
    public async Task<IReadOnlyList<S7DataBlockInstance>> GetAllInstanceDataBlocksAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session =>
        {
            if (!session.Connected)
            {
                _logger?.LogError("Cannot get instance data blocks; session is not connected.");
                return Task.FromResult<IReadOnlyList<S7DataBlockInstance>>([]);
            }

            var browser = new Opc.Ua.Client.Browser(session)
            {
                BrowseDirection = Opc.Ua.BrowseDirection.Forward,
                NodeClassMask = (int)Opc.Ua.NodeClass.Object,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true
            };
            Opc.Ua.ReferenceDescriptionCollection descriptions = browser.Browse(_dataBlocksInstanceRootNode);

            var result = descriptions
                .Select(desc => new S7DataBlockInstance { NodeId = ((Opc.Ua.NodeId)desc.NodeId).ToString(), DisplayName = desc.DisplayName.Text })
                .ToList()
                .AsReadOnly();

            return Task.FromResult<IReadOnlyList<S7DataBlockInstance>>(result);
        }, cancellationToken);
    }

    /// <inheritdoc cref="IS7UaClient.GetMemoryAsync(CancellationToken)"/>
    public async Task<IS7Memory?> GetMemoryAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(GetSingletonStructureElementCore<S7Memory>(session, _memoryRootNode)), cancellationToken);
    }

    /// <inheritdoc cref="IS7UaClient.GetInputsAsync(CancellationToken)"/>
    public async Task<IS7Inputs?> GetInputsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(GetSingletonStructureElementCore<S7Inputs>(session, _inputsRootNode)), cancellationToken);
    }

    /// <inheritdoc cref="IS7UaClient.GetOutputsAsync(CancellationToken)"/>
    public async Task<IS7Outputs?> GetOutputsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(GetSingletonStructureElementCore<S7Outputs>(session, _outputsRootNode)), cancellationToken);
    }

    /// <inheritdoc cref="IS7UaClient.GetTimersAsync(CancellationToken)"/>
    public async Task<IS7Timers?> GetTimersAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(GetSingletonStructureElementCore<S7Timers>(session, _timersRootNode)), cancellationToken);
    }

    /// <inheritdoc cref="IS7UaClient.GetCountersAsync(CancellationToken)"/>
    public async Task<IS7Counters?> GetCountersAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(GetSingletonStructureElementCore<S7Counters>(session, _countersRootNode)), cancellationToken);
    }

    /// <summary>
    /// Discovers the full structure of any UA node using a unified approach.
    /// </summary>
    /// <param name="nodeShell">The node shell to discover.</param>
    /// <param name="cancellationToken">A cancellation token to abort the operation.</param>
    /// <returns>The discovered node with populated structure, or null if the input is null.</returns>
    public async Task<IUaNode?> DiscoverNodeAsync(IUaNode nodeShell, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (nodeShell is null)
        {
            _logger?.LogWarning("DiscoverNodeAsync was called with a null node shell.");
            return null;
        }

        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(DiscoverNodeCore(session, nodeShell)), cancellationToken);
    }

    #endregion Structure Browsing and Discovery Methods (delegated to session pool)

    #region Reading and Writing Methods (delegated to session pool)

    #region Reading Methods

    /// <summary>
    /// Reads values for any discovered node structure using a unified approach.
    /// </summary>
    /// <typeparam name="T">The type of the node to read values for.</typeparam>
    /// <param name="nodeWithStructure">The node with discovered structure.</param>
    /// <param name="rootContextName">Optional root context name for path building.</param>
    /// <param name="cancellationToken">A cancellation token to abort the operation.</param>
    /// <returns>The node populated with current values.</returns>
    public async Task<T> ReadNodeValuesAsync<T>(T nodeWithStructure, string? rootContextName = null, CancellationToken cancellationToken = default) where T : IUaNode
    {
        ThrowIfDisposed();
        return await _sessionPool.ExecuteWithSessionAsync(session => Task.FromResult(ReadNodeValuesCore(session, nodeWithStructure, rootContextName)), cancellationToken);
    }

    #endregion Reading Methods

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

        return await _sessionPool.ExecuteWithSessionAsync(async session =>
        {
            if (!session.Connected)
            {
                _logger?.LogError("Cannot write values for node id '{nodeId}'; session is not connected.", nodeId);
                return false;
            }

            var writeValue = new Opc.Ua.WriteValue
            {
                NodeId = new Opc.Ua.NodeId(nodeId),
                AttributeId = Opc.Ua.Attributes.Value,
                Value = new Opc.Ua.DataValue(new Opc.Ua.Variant(rawValue))
            };
            var response = await session.WriteAsync(null, [writeValue], cancellationToken).ConfigureAwait(false);
            _validateResponse(response.Results, new[] { writeValue });

            Opc.Ua.StatusCode writeResult = response.Results[0];
            if (Opc.Ua.StatusCode.IsGood(writeResult))
            {
                return true;
            }

            _logger?.LogError("Failed to write raw value to node {NodeId}. StatusCode: {StatusCode}", nodeId, writeResult);
            return false;
        }, cancellationToken);
    }

    #endregion Writing Methods

    #region Type Converter Access

    /// <inheritdoc cref="IS7UaClient.GetConverter(S7DataType, Type)"/>
    public IS7TypeConverter GetConverter(S7DataType s7Type, Type fallbackType) =>
        _typeConvertersInstance.TryGetValue(s7Type, out var converter) ? converter : new DefaultConverter(fallbackType);

    #endregion Type Converter Access

    #endregion Reading and Writing Methods (delegated to session pool)

    #region Subscription Methods (delegated to main client)

    /// <inheritdoc cref="IS7UaClient.CreateSubscriptionAsync(int)"/>
    public async Task<bool> CreateSubscriptionAsync(int publishingInterval = 100)
    {
        ThrowIfDisposed();
        return await _mainClient.CreateSubscriptionAsync(publishingInterval);
    }

    /// <inheritdoc cref="IS7UaClient.SubscribeToVariableAsync(IS7Variable)"/>
    public async Task<bool> SubscribeToVariableAsync(IS7Variable variable)
    {
        ThrowIfDisposed();
        return await _mainClient.SubscribeToVariableAsync(variable);
    }

    /// <inheritdoc cref="IS7UaClient.UnsubscribeFromVariableAsync(IS7Variable)"/>
    public async Task<bool> UnsubscribeFromVariableAsync(IS7Variable variable)
    {
        ThrowIfDisposed();
        return await _mainClient.UnsubscribeFromVariableAsync(variable);
    }

    #endregion Subscription Methods (delegated to main client)

    #endregion Public Methods

    #region Private Methods

    #region Event Handlers

    private void OnMainClientConnected(object? sender, ConnectionEventArgs e)
    {
        // Initialize session pool synchronously after main client connects
        // This blocks the Connected event until session pool is ready
        try
        {
            if (_mainClient.OpcApplicationConfiguration != null && _mainClient.ConfiguredEndpoint != null)
            {
                var initTask = _sessionPool.InitializeAsync(_mainClient.OpcApplicationConfiguration, _mainClient.ConfiguredEndpoint);
                initTask.GetAwaiter().GetResult(); // Synchronously wait for initialization
                _logger?.LogDebug("Session pool initialized after main client connection.");
            }
            else
            {
                _logger?.LogWarning("Cannot initialize session pool - missing configuration");
                return; // Don't fire Connected event if initialization failed
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize session pool.");
            return; // Don't fire Connected event if initialization failed
        }

        // Only fire Connected event after successful session pool initialization
        Connected?.Invoke(this, e);
    }

    private void OnMainClientReconnected(object? sender, ConnectionEventArgs e)
    {
        // Re-initialize session pool after reconnection (endpoint might have changed)
        try
        {
            if (_mainClient.OpcApplicationConfiguration != null && _mainClient.ConfiguredEndpoint != null)
            {
                var initTask = _sessionPool.InitializeAsync(_mainClient.OpcApplicationConfiguration, _mainClient.ConfiguredEndpoint);
                initTask.GetAwaiter().GetResult(); // Synchronously wait for initialization
                _logger?.LogDebug("Session pool re-initialized after main client reconnection.");
            }
            else
            {
                _logger?.LogWarning("Cannot re-initialize session pool - missing configuration");
                return; // Don't fire Reconnected event if initialization failed
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to re-initialize session pool after reconnection.");
            return; // Don't fire Reconnected event if initialization failed
        }

        // Only fire Reconnected event after successful session pool initialization
        Reconnected?.Invoke(this, e);
    }

    #endregion Event Handlers

    #region Shared Discovery Logic

    /// <summary>
    /// Common browsing logic for discovering child nodes based on provided context.
    /// </summary>
    /// <param name="session">The OPC UA session.</param>
    /// <param name="nodeId">The NodeId to browse.</param>
    /// <param name="context">The discovery context containing browsing parameters.</param>
    /// <returns>A collection of discovered child nodes.</returns>
    private IEnumerable<IUaNode> BrowseAndCreateNodes(Opc.Ua.Client.ISession session, string nodeId, NodeDiscoveryContext context)
    {
        if (!session.Connected)
        {
            _logger?.LogError("Cannot browse nodes for '{NodeId}'; session is not connected.", nodeId);
            return [];
        }

        var browser = new Opc.Ua.Client.Browser(session)
        {
            BrowseDirection = Opc.Ua.BrowseDirection.Forward,
            NodeClassMask = (int)context.NodeClassMask,
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true
        };

        try
        {
            var childDescriptions = browser.Browse(nodeId);
            return childDescriptions
                .Where(context.Filter)
                .Select(context.NodeFactory);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to browse nodes for '{NodeId}'.", nodeId);
            return [];
        }
    }

    /// <summary>
    /// Unified node discovery method that dispatches to specialized handlers based on node type.
    /// </summary>
    /// <param name="session">The OPC UA session.</param>
    /// <param name="nodeShell">The node shell to discover.</param>
    /// <returns>The discovered node with populated structure.</returns>
    private IUaNode DiscoverNodeCore(Opc.Ua.Client.ISession session, IUaNode nodeShell)
    {
        if (nodeShell?.NodeId is null)
        {
            _logger?.LogWarning("DiscoverNodeCore called with a null node or node with null NodeId.");
            return nodeShell ?? throw new ArgumentNullException(nameof(nodeShell));
        }

        return nodeShell switch
        {
            S7DataBlockGlobal globalDb => DiscoverGlobalDbCore(session, globalDb),
            S7DataBlockInstance instanceDb => DiscoverInstanceDbCore(session, instanceDb),
            S7InstanceDbSection section => DiscoverSectionCore(session, section),
            S7StructureElement element => DiscoverSimpleElementCore(session, element),
            _ => nodeShell
        };
    }

    /// <summary>
    /// Discovers the structure of a global data block.
    /// </summary>
    private S7DataBlockGlobal DiscoverGlobalDbCore(Opc.Ua.Client.ISession session, S7DataBlockGlobal globalDb)
    {
        var context = new NodeDiscoveryContext(
            Opc.Ua.NodeClass.Variable,
            desc => desc.DisplayName.Text != "Icon",
            desc => new S7Variable
            {
                NodeId = ((Opc.Ua.NodeId)desc.NodeId).ToString(),
                DisplayName = desc.DisplayName.Text
            }
        );

        var variables = BrowseAndCreateNodes(session, globalDb.NodeId!, context).Cast<S7Variable>().ToList();
        return globalDb with { Variables = variables };
    }

    /// <summary>
    /// Discovers the structure of an instance data block with its four sections.
    /// </summary>
    private S7DataBlockInstance DiscoverInstanceDbCore(Opc.Ua.Client.ISession session, S7DataBlockInstance instanceDb)
    {
        var context = new NodeDiscoveryContext(
            Opc.Ua.NodeClass.Object,
            _ => true,
            desc => new S7InstanceDbSection
            {
                NodeId = ((Opc.Ua.NodeId)desc.NodeId).ToString(),
                DisplayName = desc.DisplayName.Text
            }
        );

        var sections = BrowseAndCreateNodes(session, instanceDb.NodeId!, context)
            .Cast<S7InstanceDbSection>()
            .Select(section => (S7InstanceDbSection)DiscoverNodeCore(session, section))
            .ToArray();

        return instanceDb with
        {
            Inputs = sections.FirstOrDefault(s => s.DisplayName == "Inputs"),
            Outputs = sections.FirstOrDefault(s => s.DisplayName == "Outputs"),
            InOuts = sections.FirstOrDefault(s => s.DisplayName == "InOuts"),
            Static = sections.FirstOrDefault(s => s.DisplayName == "Static")
        };
    }

    /// <summary>
    /// Discovers the contents of an instance data block section.
    /// </summary>
    private S7InstanceDbSection DiscoverSectionCore(Opc.Ua.Client.ISession session, S7InstanceDbSection section)
    {
#pragma warning disable RCS1130 // Bitwise operation on enum without Flags attribute
        var context = new NodeDiscoveryContext(
            Opc.Ua.NodeClass.Variable | Opc.Ua.NodeClass.Object,
            desc => desc.DisplayName.Text != "Icon",
            desc => desc.NodeClass == Opc.Ua.NodeClass.Variable
                ? new S7Variable
                {
                    NodeId = ((Opc.Ua.NodeId)desc.NodeId).ToString(),
                    DisplayName = desc.DisplayName.Text
                }
                : new S7DataBlockInstance
                {
                    NodeId = ((Opc.Ua.NodeId)desc.NodeId).ToString(),
                    DisplayName = desc.DisplayName.Text
                }
        );
#pragma warning restore RCS1130 // Bitwise operation on enum without Flags attribute

        var children = BrowseAndCreateNodes(session, section.NodeId!, context).ToList();

        var variables = children.OfType<S7Variable>().ToList();
        var nestedInstances = children.OfType<S7DataBlockInstance>()
            .Select(nested => (S7DataBlockInstance)DiscoverNodeCore(session, nested))
            .ToList();

        return section with { Variables = variables, NestedInstances = nestedInstances };
    }

    /// <summary>
    /// Discovers variables within a simple structure element.
    /// </summary>
    private S7StructureElement DiscoverSimpleElementCore(Opc.Ua.Client.ISession session, S7StructureElement element)
    {
        var context = new NodeDiscoveryContext(
            Opc.Ua.NodeClass.Variable,
            desc => desc.DisplayName.Text != "Icon",
            desc => new S7Variable
            {
                NodeId = ((Opc.Ua.NodeId)desc.NodeId).ToString(),
                DisplayName = desc.DisplayName.Text
            }
        );

        var variables = BrowseAndCreateNodes(session, element.NodeId!, context).Cast<S7Variable>().ToList();

        // Apply special type handling for Counters and Timers
        if (element.DisplayName == "Counters")
        {
            variables = variables.ConvertAll(variable => variable with { S7Type = S7DataType.COUNTER });
        }
        else if (element.DisplayName == "Timers")
        {
            variables = variables.ConvertAll(variable => variable with { S7Type = S7DataType.S5TIME });
        }

        return element with { Variables = variables };
    }

    #endregion Shared Discovery Logic

    #region Helper Methods - Session pool implementation

    private ReadOnlyCollection<T> GetAllStructureElementsCore<T>(Opc.Ua.Client.ISession session, Opc.Ua.NodeId rootNode, Opc.Ua.NodeClass expectedNodeClass) where T : S7StructureElement, new()
    {
        if (!session.Connected)
        {
            _logger?.LogError("Cannot get structure elements for root {RootNode}; session is not connected.", rootNode);
            return new ReadOnlyCollection<T>([]);
        }

        var browser = new Opc.Ua.Client.Browser(session)
        {
            BrowseDirection = Opc.Ua.BrowseDirection.Forward,
            NodeClassMask = (int)expectedNodeClass,
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true
        };
        Opc.Ua.ReferenceDescriptionCollection descriptions = browser.Browse(rootNode);

        return descriptions
            .Select(desc => new T { NodeId = ((Opc.Ua.NodeId)desc.NodeId).ToString(), DisplayName = desc.DisplayName.Text })
            .ToList()
            .AsReadOnly();
    }

    private T? GetSingletonStructureElementCore<T>(Opc.Ua.Client.ISession session, Opc.Ua.NodeId node) where T : S7StructureElement, new()
    {
        if (!session.Connected)
        {
            _logger?.LogError("Cannot get singleton element for node {NodeId}; session is not connected.", node);
            return null;
        }

        var nodeToRead = new Opc.Ua.ReadValueId { NodeId = node, AttributeId = Opc.Ua.Attributes.DisplayName };
        session.Read(null, 0, Opc.Ua.TimestampsToReturn.Neither, [nodeToRead], out var results, out _);
        _validateResponse(results, new[] { nodeToRead });

        Opc.Ua.DataValue result = results[0];
        if (Opc.Ua.StatusCode.IsBad(result.StatusCode))
        {
            _logger?.LogWarning("Failed to read DisplayName for node {NodeId}. It may not exist on the server. StatusCode: {StatusCode}", node, result.StatusCode);
            return null;
        }

        string displayName = (result.Value as Opc.Ua.LocalizedText)?.Text ?? node.ToString();
        return new T { NodeId = node.ToString(), DisplayName = displayName };
    }

    private S7DataBlockInstance DiscoverInstanceOfDataBlockCore(Opc.Ua.Client.ISession session, S7DataBlockInstance instanceDbShell)
    {
        if (instanceDbShell?.NodeId is null)
        {
            _logger?.LogWarning("Cannot discover instance DB because the provided shell or its NodeId is null.");
            return instanceDbShell ?? new S7DataBlockInstance();
        }
        if (!session.Connected)
        {
            _logger?.LogError("Cannot discover instance DB '{DisplayName}'; session is not connected.", instanceDbShell.DisplayName);
            return instanceDbShell;
        }

        var browser = new Opc.Ua.Client.Browser(session)
        {
            BrowseDirection = Opc.Ua.BrowseDirection.Forward,
            NodeClassMask = (int)Opc.Ua.NodeClass.Object,
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences
        };
        var childNodes = browser.Browse(instanceDbShell.NodeId);

        S7InstanceDbSection? input = null, output = null, inOut = null, stat = null;
        foreach (var childNode in childNodes)
        {
            var sectionShell = new S7InstanceDbSection { NodeId = ((Opc.Ua.NodeId)childNode.NodeId).ToString(), DisplayName = childNode.DisplayName.Text };
            var populatedSection = PopulateInstanceSectionCore(session, sectionShell);
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

    private S7InstanceDbSection PopulateInstanceSectionCore(Opc.Ua.Client.ISession session, S7InstanceDbSection sectionShell)
    {
        if (sectionShell?.NodeId is null) return sectionShell ?? new S7InstanceDbSection();
        if (!session.Connected) return sectionShell;

#pragma warning disable RCS1130
        var browser = new Opc.Ua.Client.Browser(session)
        {
            BrowseDirection = Opc.Ua.BrowseDirection.Forward,
            NodeClassMask = (int)(Opc.Ua.NodeClass.Variable | Opc.Ua.NodeClass.Object),
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
        };
#pragma warning restore RCS1130
        Opc.Ua.ReferenceDescriptionCollection childNodes = browser.Browse(sectionShell.NodeId);

        var variables = new List<S7Variable>();
        var nestedInstances = new List<S7DataBlockInstance>();
        foreach (var childNode in childNodes)
        {
            //Filter out the Icon variable and process others
            if (childNode.NodeClass == Opc.Ua.NodeClass.Variable && childNode.DisplayName.Text != "Icon")
            {
                variables.Add(new S7Variable { NodeId = ((Opc.Ua.NodeId)childNode.NodeId).ToString(), DisplayName = childNode.DisplayName.Text });
            }
            else if (childNode.NodeClass == Opc.Ua.NodeClass.Object)
            {
                var nestedShell = new S7DataBlockInstance { NodeId = ((Opc.Ua.NodeId)childNode.NodeId).ToString(), DisplayName = childNode.DisplayName.Text };
                nestedInstances.Add(DiscoverInstanceOfDataBlockCore(session, nestedShell));
            }
        }
        return sectionShell with { Variables = variables, NestedInstances = nestedInstances };
    }

    /// <summary>
    /// Core method for reading values from any discovered node structure.
    /// </summary>
    private T ReadNodeValuesCore<T>(Opc.Ua.Client.ISession session, T nodeWithStructure, string? rootContextName = null) where T : IUaNode
    {
        if (nodeWithStructure?.NodeId is null)
        {
            _logger?.LogWarning("ReadNodeValuesCore called with a null node or node with null NodeId.");
            return nodeWithStructure!;
        }
        if (!session.Connected)
        {
            _logger?.LogError("Cannot read values for '{DisplayName}'; session is not connected.", nodeWithStructure.DisplayName);
            return nodeWithStructure;
        }

        var pathBuilder = new PathBuilder(rootContextName);
        var initialPath = pathBuilder.BuildInitialPath(nodeWithStructure);

        var nodesToReadCollector = new Dictionary<Opc.Ua.NodeId, S7Variable>();
        CollectNodesToReadRecursively(session, nodeWithStructure, nodesToReadCollector, pathBuilder.Child(nodeWithStructure.DisplayName));

        var readResultsMap = new Dictionary<Opc.Ua.NodeId, Opc.Ua.DataValue>();
        if (nodesToReadCollector.Count > 0)
        {
            var nodesToRead = new Opc.Ua.ReadValueIdCollection(nodesToReadCollector.Keys.Select(nodeId => new Opc.Ua.ReadValueId { NodeId = nodeId, AttributeId = Opc.Ua.Attributes.Value }));
            session.Read(null, 0, Opc.Ua.TimestampsToReturn.Neither, nodesToRead, out var results, out _);
            _validateResponse(results, nodesToRead);

            for (int i = 0; i < nodesToRead.Count; i++)
            {
                if (nodesToRead[i].NodeId != null) readResultsMap[nodesToRead[i].NodeId] = results[i];
            }
        }

        return (T)RebuildHierarchyWithValues(session, nodeWithStructure, readResultsMap, pathBuilder.Child(nodeWithStructure.DisplayName));
    }

    /// <summary>
    /// Recursively rebuilds the node hierarchy with read values using unified logic.
    /// </summary>
    private IUaNode RebuildHierarchyWithValues(
        Opc.Ua.Client.ISession session,
        IUaNode templateNode,
        IReadOnlyDictionary<Opc.Ua.NodeId, Opc.Ua.DataValue> readResultsMap,
        PathBuilder pathBuilder)
    {
        return templateNode switch
        {
            S7DataBlockInstance idb => idb with
            {
                Inputs = idb.Inputs != null ? (S7InstanceDbSection)RebuildHierarchyWithValues(session, idb.Inputs, readResultsMap, pathBuilder.Child(idb.Inputs.DisplayName)) : null,
                Outputs = idb.Outputs != null ? (S7InstanceDbSection)RebuildHierarchyWithValues(session, idb.Outputs, readResultsMap, pathBuilder.Child(idb.Outputs.DisplayName)) : null,
                InOuts = idb.InOuts != null ? (S7InstanceDbSection)RebuildHierarchyWithValues(session, idb.InOuts, readResultsMap, pathBuilder.Child(idb.InOuts.DisplayName)) : null,
                Static = idb.Static != null ? (S7InstanceDbSection)RebuildHierarchyWithValues(session, idb.Static, readResultsMap, pathBuilder.Child(idb.Static.DisplayName)) : null
            },

            S7InstanceDbSection section => section with
            {
                Variables = section.Variables.Select(v => (S7Variable)RebuildHierarchyWithValues(session, v, readResultsMap, pathBuilder)).ToList(),
                NestedInstances = section.NestedInstances.Select(n => (S7DataBlockInstance)RebuildHierarchyWithValues(session, n, readResultsMap, pathBuilder.Child(n.DisplayName))).ToList()
            },

            S7StructureElement simpleElement => simpleElement with
            {
                Variables = simpleElement.Variables.Select(v => (S7Variable)RebuildHierarchyWithValues(session, v, readResultsMap, pathBuilder)).ToList()
            },

            S7Variable variable => ProcessVariableWithValues(session, variable, readResultsMap, pathBuilder),

            _ => templateNode
        };
    }

    /// <summary>
    /// Processes a single variable with its values, handling both simple and struct types.
    /// </summary>
    private S7Variable ProcessVariableWithValues(
        Opc.Ua.Client.ISession session,
        S7Variable variable,
        IReadOnlyDictionary<Opc.Ua.NodeId, Opc.Ua.DataValue> readResultsMap,
        PathBuilder pathBuilder)
    {
        var fullPath = pathBuilder.Child(variable.DisplayName).CurrentPath;

        if (variable.S7Type == S7DataType.STRUCT)
        {
            var discoveredMembers = DiscoverSimpleElementCore(session, new S7StructureElement { NodeId = variable.NodeId }).Variables;

            var templateMembersByName = variable.StructMembers
                .Where(m => m.DisplayName is not null)
                .ToDictionary(m => m.DisplayName!, m => m);

            var membersToProcess = discoveredMembers.Cast<S7Variable>().Select(discoveredMember =>
            {
                templateMembersByName.TryGetValue(discoveredMember.DisplayName ?? string.Empty, out var templateMember);

                return discoveredMember with
                {
                    S7Type = templateMember?.S7Type ?? S7DataType.UNKNOWN,
                    StructMembers = templateMember?.StructMembers ?? []
                };
            });

            var processedMembers = membersToProcess
                .Select(m => (S7Variable)RebuildHierarchyWithValues(session, m, readResultsMap, pathBuilder.Child(variable.DisplayName)))
                .ToList();

            return variable with { FullPath = fullPath, StructMembers = processedMembers, StatusCode = UaStatusCodeConverter.Convert(Opc.Ua.StatusCodes.Good) };
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

        return variable with { FullPath = fullPath, StatusCode = UaStatusCodeConverter.Convert(Opc.Ua.StatusCodes.BadWaitingForInitialData) };
    }

    /// <summary>
    /// Recursively collects all readable nodes using unified logic.
    /// </summary>
    private void CollectNodesToReadRecursively(Opc.Ua.Client.ISession session, IUaNode currentNode, IDictionary<Opc.Ua.NodeId, S7Variable> collectedNodes, PathBuilder pathBuilder)
    {
        switch (currentNode)
        {
            case S7DataBlockInstance idb:
                if (idb.Inputs != null) CollectNodesToReadRecursively(session, idb.Inputs, collectedNodes, pathBuilder.Child(idb.Inputs.DisplayName));
                if (idb.Outputs != null) CollectNodesToReadRecursively(session, idb.Outputs, collectedNodes, pathBuilder.Child(idb.Outputs.DisplayName));
                if (idb.InOuts != null) CollectNodesToReadRecursively(session, idb.InOuts, collectedNodes, pathBuilder.Child(idb.InOuts.DisplayName));
                if (idb.Static != null) CollectNodesToReadRecursively(session, idb.Static, collectedNodes, pathBuilder.Child(idb.Static.DisplayName));
                break;

            case S7InstanceDbSection section:
                foreach (var variable in section.Variables) CollectNodesToReadRecursively(session, variable, collectedNodes, pathBuilder);
                foreach (var nestedIdb in section.NestedInstances) CollectNodesToReadRecursively(session, nestedIdb, collectedNodes, pathBuilder.Child(nestedIdb.DisplayName));
                break;

            case S7StructureElement simpleElement:
                foreach (var variable in simpleElement.Variables) CollectNodesToReadRecursively(session, variable, collectedNodes, pathBuilder);
                break;

            case S7Variable variable:
                if (variable.S7Type == S7DataType.STRUCT)
                {
                    var discoveredMembers = DiscoverSimpleElementCore(session, new S7StructureElement { NodeId = variable.NodeId }).Variables;

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
                                CollectNodesToReadRecursively(session, memberToRecurse, collectedNodes, pathBuilder.Child(variable.DisplayName));
                            }
                            else
                            {
                                CollectNodesToReadRecursively(session, discoveredMember, collectedNodes, pathBuilder.Child(variable.DisplayName));
                            }
                        }
                    }
                    else
                    {
                        foreach (var member in discoveredMembers)
                        {
                            CollectNodesToReadRecursively(session, member, collectedNodes, pathBuilder.Child(variable.DisplayName));
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


    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    #endregion Helper Methods - Session pool implementation

    #endregion Private Methods

    #region Dispose

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
            // Unsubscribe from main client events to prevent race conditions
            if (_mainClient != null)
            {
                _mainClient.Connecting -= _connectingHandler;
                _mainClient.Connected -= _connectedHandler;
                _mainClient.Disconnecting -= _disconnectingHandler;
                _mainClient.Disconnected -= _disconnectedHandler;
                _mainClient.Reconnecting -= _reconnectingHandler;
                _mainClient.Reconnected -= OnMainClientReconnected;
                _mainClient.MonitoredItemChanged -= _monitoredItemChangedHandler;
            }

            _disposed = true;
            _mainClient?.Dispose();
            _sessionPool?.Dispose();
        }
    }

    ~S7UaClient()
    {
        Dispose(false);
    }

    #endregion Dispose
}