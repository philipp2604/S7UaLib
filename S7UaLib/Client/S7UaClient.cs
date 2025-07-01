namespace S7UaLib.Client;

using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Client.Contracts;
using S7UaLib.Events;
using S7UaLib.S7.Converters;
using S7UaLib.S7.Converters.Contracts;
using S7UaLib.S7.Structure;
using S7UaLib.S7.Structure.Contracts;
using S7UaLib.S7.Types;
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

    #region Static Type Converters

    private static readonly S7CharConverter _charConverter = new();
    private static readonly S7WCharConverter _wCharConverter = new();
    private static readonly S7DateConverter _dateConverter = new();
    private static readonly S7TimeConverter _timeConverter = new();
    private static readonly S7LTimeConverter _lTimeConverter = new();
    private static readonly S7DateAndTimeConverter _dateAndTimeConverter = new();
    private static readonly S7TimeOfDayConverter _timeOfDayConverter = new();
    private static readonly S7LTimeOfDayConverter _lTimeOfDayConverter = new();
    private static readonly S7S5TimeConverter _s5TimeConverter = new();
    private static readonly S7DTLConverter _dtlConverter = new();

    private static readonly Dictionary<S7DataType, IS7TypeConverter> _typeConverters =
        new()
        {
            [S7DataType.CHAR] = _charConverter,
            [S7DataType.WCHAR] = _wCharConverter,
            [S7DataType.DATE] = _dateConverter,
            [S7DataType.TIME] = _timeConverter,
            [S7DataType.LTIME] = _lTimeConverter,
            [S7DataType.TIME_OF_DAY] = _timeOfDayConverter,
            [S7DataType.LTIME_OF_DAY] = _lTimeOfDayConverter,
            [S7DataType.S5TIME] = _s5TimeConverter,
            [S7DataType.DATE_AND_TIME] = _dateAndTimeConverter,
            [S7DataType.DTL] = _dtlConverter,
            [S7DataType.ARRAY_OF_CHAR] = new S7ElementwiseArrayConverter(_charConverter, typeof(byte)),
            [S7DataType.ARRAY_OF_WCHAR] = new S7ElementwiseArrayConverter(_wCharConverter, typeof(ushort)),
            [S7DataType.ARRAY_OF_DATE] = new S7ElementwiseArrayConverter(_dateConverter, typeof(ushort)),
            [S7DataType.ARRAY_OF_TIME] = new S7ElementwiseArrayConverter(_timeConverter, typeof(int)),
            [S7DataType.ARRAY_OF_LTIME] = new S7ElementwiseArrayConverter(_lTimeConverter, typeof(long)),
            [S7DataType.ARRAY_OF_TIME_OF_DAY] = new S7ElementwiseArrayConverter(_timeOfDayConverter, typeof(uint)),
            [S7DataType.ARRAY_OF_LTIME_OF_DAY] = new S7ElementwiseArrayConverter(_lTimeOfDayConverter, typeof(ulong)),
            [S7DataType.ARRAY_OF_S5TIME] = new S7ElementwiseArrayConverter(_s5TimeConverter, typeof(ushort)),
            [S7DataType.ARRAY_OF_DATE_AND_TIME] = new S7ElementwiseArrayConverter(_dateAndTimeConverter, typeof(byte)),
            [S7DataType.ARRAY_OF_DTL] = new S7ElementwiseArrayConverter(_dtlConverter, typeof(byte[])),
        };

    #endregion Static Type Converters

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
        return instanceDbShell with { Inputs = input, Outputs = output, InOuts = inOut, Static = stat };
    }

    #endregion Structure Browsing and Discovery Methods

    #region Reading and Writing Methods

    #region Reading Methods

    /// <inheritdoc cref="IS7UaClient.ReadValuesOfElement{T}(T, string?)"/>
    public T ReadValuesOfElement<T>(T elementWithStructure, string? rootContextName = null) where T : IUaElement
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
        CollectNodesToReadRecursively(elementWithStructure, nodesToReadCollector, initialPathPrefix);

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

        return (T)RebuildHierarchyWithValuesRecursively(elementWithStructure, readResultsMap, initialPathPrefix);
    }

    #endregion Reading Methods

    #region Writing Methods

    /// <inheritdoc cref="IS7UaClient.WriteValuesOfElement{T}(T, string?)"/>
    public async Task<bool> WriteVariableAsync(NodeId nodeId, object value, S7DataType s7Type)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(value);

        var converter = GetConverter(s7Type, value.GetType());
        var opcValue = converter.ConvertToOpc(value) ?? throw new InvalidOperationException($"Conversion of value for S7Type {s7Type} resulted in null.");
        return await WriteRawVariableAsync(nodeId, opcValue);
    }

    /// <inheritdoc cref="IS7UaClient.WriteVariableAsync(S7Variable, object)"/>/>
    public async Task<bool> WriteVariableAsync(S7Variable variable, object value)
    {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(value);
        return variable.NodeId is null
            ? throw new ArgumentException($"Variable '{variable.DisplayName}' has no NodeId and cannot be written to.", nameof(variable))
            : await WriteVariableAsync(variable.NodeId, value, variable.S7Type);
    }

    /// <inheritdoc cref="IS7UaClient.WriteRawVariableAsync(NodeId, object)"/>
    public async Task<bool> WriteRawVariableAsync(NodeId nodeId, object rawValue)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        ArgumentNullException.ThrowIfNull(rawValue);

        if (!IsConnected || _session is null)
        {
            _logger?.LogError("Cannot write values for node id '{nodeId}'; session is not connected.", nodeId.ToString());
            return false;
        }

        var writeValue = new WriteValue
        {
            NodeId = nodeId,
            AttributeId = Attributes.Value,
            Value = new DataValue(new Variant(rawValue))
        };
        var response = await _session.WriteAsync(null, [writeValue], CancellationToken.None).ConfigureAwait(false);
        _validateResponse(response.Results, new[] { writeValue });

        StatusCode writeResult = response.Results[0];
        if (StatusCode.IsGood(writeResult))
        {
            return true;
        }

        _logger?.LogError("Failed to write raw value to node {NodeId}. StatusCode: {StatusCode}", nodeId, writeResult);
        return false;
    }

    #endregion Writing Methods

    #region Reading and Writing Helpers

    public static IS7TypeConverter GetConverter(S7DataType s7Type, Type fallbackType) =>
        _typeConverters.TryGetValue(s7Type, out var converter) ? converter : new DefaultConverter(fallbackType);

    #endregion Reading and Writing Helpers

    #endregion Reading and Writing Methods

    #endregion Connection Methods

    #region Private Methods

    #region Reading and Writing Helpers

    private IUaElement RebuildHierarchyWithValuesRecursively(
        IUaElement templateElement,
        IReadOnlyDictionary<NodeId, DataValue> readResultsMap,
        string currentPath)
    {
        switch (templateElement)
        {
            case S7DataBlockInstance idb:
                var newInput = idb.Inputs != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursively(idb.Inputs, readResultsMap, $"{currentPath}.{idb.Inputs.DisplayName}") : null;
                var newOutput = idb.Outputs != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursively(idb.Outputs, readResultsMap, $"{currentPath}.{idb.Outputs.DisplayName}") : null;
                var newInOut = idb.InOuts != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursively(idb.InOuts, readResultsMap, $"{currentPath}.{idb.InOuts.DisplayName}") : null;
                var newStatic = idb.Static != null ? (S7InstanceDbSection)RebuildHierarchyWithValuesRecursively(idb.Static, readResultsMap, $"{currentPath}.{idb.Static.DisplayName}") : null;
                return idb with { Inputs = newInput, Outputs = newOutput, InOuts = newInOut, Static = newStatic };

            case S7InstanceDbSection section:
                var newVars = section.Variables.Select(v => (S7Variable)RebuildHierarchyWithValuesRecursively(v, readResultsMap, currentPath)).ToList();
                var newNested = section.NestedInstances.Select(n => (S7DataBlockInstance)RebuildHierarchyWithValuesRecursively(n, readResultsMap, $"{currentPath}.{n.DisplayName}")).ToList();
                return section with { Variables = newVars, NestedInstances = newNested };

            case S7StructureElement simpleElement:
                var newSimpleVars = simpleElement.Variables.Select(v => (S7Variable)RebuildHierarchyWithValuesRecursively(v, readResultsMap, currentPath)).ToList();
                return simpleElement with { Variables = newSimpleVars };

            case S7Variable variable:
                string fullPath = $"{currentPath}.{variable.DisplayName}";

                if (variable.S7Type == S7DataType.STRUCT)
                {
                    var discoveredMembers = DiscoverVariablesOfElement(new S7StructureElement { NodeId = variable.NodeId }).Variables;

                    var templateTypeLookup = variable.StructMembers
                        .Where(m => m.DisplayName is not null)
                        .ToDictionary(m => m.DisplayName!, m => m.S7Type);

                    var membersToProcess = discoveredMembers.Cast<S7Variable>().Select(discoveredMember =>
                    {
                        templateTypeLookup.TryGetValue(discoveredMember.DisplayName ?? string.Empty, out var s7Type);

                        return (IS7Variable)(discoveredMember with { S7Type = s7Type });
                    });

                    var processedMembers = membersToProcess
                        .Select(m => (S7Variable)RebuildHierarchyWithValuesRecursively(m, readResultsMap, fullPath))
                        .ToList();

                    return variable with { FullPath = fullPath, StructMembers = processedMembers, StatusCode = StatusCodes.Good };
                }

                if (variable.NodeId != null && readResultsMap.TryGetValue(variable.NodeId, out var dataValue))
                {
                    var converter = GetConverter(variable.S7Type, dataValue.Value?.GetType() ?? typeof(object));
                    var rawValue = dataValue.Value;
                    var finalValue = converter.ConvertFromOpc(rawValue);

                    return variable with
                    {
                        RawOpcValue = rawValue,
                        Value = finalValue,
                        SystemType = converter.TargetType,
                        FullPath = fullPath,
                        StatusCode = dataValue.StatusCode
                    };
                }

                return variable with { FullPath = fullPath, StatusCode = StatusCodes.BadWaitingForInitialData };

            default:
                _logger?.LogWarning("RebuildHierarchy encountered an unhandled element type: {ElementType}", templateElement.GetType().Name);
                return templateElement;
        }
    }

    private void CollectNodesToReadRecursively(IUaElement currentElement, IDictionary<NodeId, S7Variable> collectedNodes, string currentPath)
    {
        switch (currentElement)
        {
            case S7DataBlockInstance idb:
                if (idb.Inputs != null) CollectNodesToReadRecursively(idb.Inputs, collectedNodes, $"{currentPath}.{idb.Inputs.DisplayName}");
                if (idb.Outputs != null) CollectNodesToReadRecursively(idb.Outputs, collectedNodes, $"{currentPath}.{idb.Outputs.DisplayName}");
                if (idb.InOuts != null) CollectNodesToReadRecursively(idb.InOuts, collectedNodes, $"{currentPath}.{idb.InOuts.DisplayName}");
                if (idb.Static != null) CollectNodesToReadRecursively(idb.Static, collectedNodes, $"{currentPath}.{idb.Static.DisplayName}");
                break;

            case S7InstanceDbSection section:
                foreach (var variable in section.Variables) CollectNodesToReadRecursively(variable, collectedNodes, currentPath);
                foreach (var nestedIdb in section.NestedInstances) CollectNodesToReadRecursively(nestedIdb, collectedNodes, $"{currentPath}.{nestedIdb.DisplayName}");
                break;

            case S7StructureElement simpleElement:
                foreach (var variable in simpleElement.Variables) CollectNodesToReadRecursively(variable, collectedNodes, currentPath);
                break;

            case S7Variable variable:
                string fullPath = $"{currentPath}.{variable.DisplayName}";
                if (variable.S7Type == S7DataType.STRUCT)
                {
                    var structMembers = DiscoverVariablesOfElement(new S7StructureElement { NodeId = variable.NodeId }).Variables;
                    foreach (var member in structMembers)
                    {
                        CollectNodesToReadRecursively(member, collectedNodes, fullPath);
                    }
                }
                else if (variable.NodeId != null)
                {
                    collectedNodes[variable.NodeId] = variable;
                }
                break;
        }
    }

    private static string BuildInitialPath(IUaElement element, string? rootContextName)
    {
        return string.IsNullOrEmpty(rootContextName)
            ? element.DisplayName ?? string.Empty
            : rootContextName.Equals(element.DisplayName, StringComparison.OrdinalIgnoreCase)
            ? rootContextName
            : $"{rootContextName}.{element.DisplayName}";
    }

    #endregion Reading and Writing Helpers

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

    #endregion Private Methods

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

    #endregion Public Methods
}