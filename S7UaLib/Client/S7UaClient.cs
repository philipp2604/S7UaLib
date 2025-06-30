namespace S7UaLib.Client;

using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Events;
using S7UaLib.S7.Structure;
using S7UaLib.UA;
using System.Collections;
using System.Collections.ObjectModel;

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

    private static readonly NodeId _dataBlocksGlobalRootNode = new("DataBlocksGlobal", 3);
    private static readonly NodeId _dataBlocksInstanceRootNode = new("DataBlocksInstance", 3);
    private static readonly NodeId _memoryRootNode = new("Memory", 3);
    private static readonly NodeId _inputsRootNode = new("Inputs", 3);
    private static readonly NodeId _outputsRootNode = new("Outputs", 3);
    private static readonly NodeId _timersRootNode = new("Timers", 3);
    private static readonly NodeId _countersRootNode = new("Counters", 3);

    #endregion Private Fields

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

    #endregion Constructors

    #region Disposing

    public void Dispose()
    {
        _disposed = true;
        Utils.SilentDispose(_session);
        GC.SuppressFinalize(this);
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

    #endregion Public Events

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

    #endregion Public Properties

    #region Public Methods
    #region Connection Methods

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
        if (_session != null)
        {
            OnDisconnecting(ConnectionEventArgs.Empty);

            lock (_sessionLock)
            {
                _session.KeepAlive -= Session_KeepAlive;
                _reconnectHandler?.Dispose();
                _reconnectHandler = null;
            }

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

    /// <inheritdoc cref="IS7UaClient.GetAllGlobalDataBlocks"/>
    public IReadOnlyList<S7DataBlockGlobal> GetAllGlobalDataBlocks() =>
        GetAllStructureElements<S7DataBlockGlobal>(_dataBlocksGlobalRootNode, NodeClass.Object);

    /// <inheritdoc cref="IS7UaClient.GetAllInstanceDataBlocks"/>
    public IReadOnlyList<S7DataBlockInstance> GetAllInstanceDataBlocks()
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
            .Select(desc => new S7DataBlockInstance { NodeId = (NodeId)desc.NodeId, DisplayName = desc.DisplayName.Text })
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc cref="IS7UaClient.GetMemory"/>
    public S7Memory? GetMemory() => GetSingletonStructureElement<S7Memory>(_memoryRootNode);

    /// <inheritdoc cref="IS7UaClient.GetInputs"/>
    public S7Inputs? GetInputs() => GetSingletonStructureElement<S7Inputs>(_inputsRootNode);

    /// <inheritdoc cref="IS7UaClient.GetOutputs"/>
    public S7Outputs? GetOutputs() => GetSingletonStructureElement<S7Outputs>(_outputsRootNode);

    /// <inheritdoc cref="IS7UaClient.GetTimers"/>
    public S7Timers? GetTimers() => GetSingletonStructureElement<S7Timers>(_timersRootNode);

    /// <inheritdoc cref="IS7UaClient.GetCounters"/>
    public S7Counters? GetCounters() => GetSingletonStructureElement<S7Counters>(_countersRootNode);

    /// <inheritdoc cref="IS7UaClient.DiscoverElement(IUaElement elementShell)"/>
    public IUaElement? DiscoverElement(IUaElement elementShell)
    {
        if (elementShell is null)
        {
            _logger?.LogWarning("DiscoverElement was called with a null element shell.");
            return null;
        }

        return elementShell switch
        {
            S7DataBlockInstance idb => DiscoverInstanceOfDataBlock(idb),
            S7StructureElement simpleElement => DiscoverVariablesOfElement((dynamic)simpleElement),
            _ => new Func<IUaElement?>(() =>
            {
                _logger?.LogWarning("DiscoverElement was called with an unsupported element type: {ElementType}", elementShell.GetType().Name);
                return null;
            })(),
        };
    }

    /// <inheritdoc cref="IS7UaClient.DiscoverInstanceOfDataBlock(S7DataBlockInstance)"/>
    public T DiscoverVariablesOfElement<T>(T element) where T : S7StructureElement
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
            .Select(desc => new S7Variable { NodeId = (NodeId)desc.NodeId, DisplayName = desc.DisplayName.Text }).ToList();

        return element with { Variables = discoveredVariables };
    }

    /// <inheritdoc cref="IS7UaClient.DiscoverInstanceOfDataBlock(S7DataBlockInstance)"/>
    public S7DataBlockInstance DiscoverInstanceOfDataBlock(S7DataBlockInstance instanceDbShell)
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
            var sectionShell = new S7InstanceDbSection { NodeId = (NodeId)childNode.NodeId, DisplayName = childNode.DisplayName.Text };
            var populatedSection = PopulateInstanceSection(sectionShell);
            switch (populatedSection.DisplayName)
            {
                case "Inputs": input = populatedSection; break;
                case "Outputs": output = populatedSection; break;
                case "InOuts": inOut = populatedSection; break;
                case "Static": stat = populatedSection; break;
            }
        }
        return instanceDbShell with { Input = input, Output = output, InOut = inOut, Static = stat };
    }

    #endregion Structure Browsing and Discovery Methods

    #endregion Public Methods

    #region Private Methods

    #region Structure Browsing and Discovery Helpers

    private T? GetSingletonStructureElement<T>(NodeId node) where T : S7StructureElement, new()
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
        if (StatusCode.IsBad(result.StatusCode))
        {
            _logger?.LogWarning("Failed to read DisplayName for node {NodeId}. It may not exist on the server. StatusCode: {StatusCode}", node, result.StatusCode);
            return null;
        }

        string displayName = (result.Value as LocalizedText)?.Text ?? node.ToString();
        return new T { NodeId = node, DisplayName = displayName };
    }

    private ReadOnlyCollection<T> GetAllStructureElements<T>(NodeId rootNode, NodeClass expectedNodeClass) where T : S7StructureElement, new()
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
            .Select(desc => new T { NodeId = (NodeId)desc.NodeId, DisplayName = desc.DisplayName.Text })
            .ToList()
            .AsReadOnly();
    }

    private S7InstanceDbSection PopulateInstanceSection(S7InstanceDbSection sectionShell)
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
                variables.Add(new S7Variable { NodeId = (NodeId)childNode.NodeId, DisplayName = childNode.DisplayName.Text });
            }
            else if (childNode.NodeClass == NodeClass.Object)
            {
                var nestedShell = new S7DataBlockInstance { NodeId = (NodeId)childNode.NodeId, DisplayName = childNode.DisplayName.Text };
                nestedInstances.Add(DiscoverInstanceOfDataBlock(nestedShell));
            }
        }
        return sectionShell with { Variables = variables, NestedInstances = nestedInstances };
    }

    #endregion Structure Browsing and Discovery Helpers
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

    #endregion Event Dispatchers

    #endregion Private Methods
}