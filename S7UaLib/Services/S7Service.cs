using Microsoft.Extensions.Logging;
using Opc.Ua;
using S7UaLib.Client;
using S7UaLib.Client.Contracts;
using S7UaLib.DataStore;
using S7UaLib.DataStore.Contracts;
using S7UaLib.Events;
using S7UaLib.S7.Structure;
using S7UaLib.S7.Structure.Contracts;
using S7UaLib.S7.Types;
using S7UaLib.Serialization.Json;
using S7UaLib.Serialization.Models;
using System.Collections;
using System.IO.Abstractions;
using System.Text.Json;

namespace S7UaLib.Services;

/// <summary>
/// A high-level service for interacting with a Siemens S7 PLC via OPC UA.
/// This service encapsulates the S7UaClient and an internal data store to provide a simplified API.
/// </summary>
public class S7Service : IS7Service
{
    #region Private Fields

    private readonly IS7UaClient _client;
    private readonly IS7DataStore _dataStore;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fileSystem;
    private bool _disposed;

    #endregion Private Fields

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="S7Service"/> class.
    /// </summary>
    /// <param name="client">The S7UaClient instance to use for communication.</param>
    /// <param name="dataStore">The S7DataStore instance to use as data store.</param>
    /// <param name="fileSystem">The FileSystem instance to use for file operations.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="client"/>, <paramref name="dataStore"/> or <paramref name="fileSystem"/> is <see langword="null"/>.</exception>
    internal S7Service(IS7UaClient client, IS7DataStore dataStore, IFileSystem fileSystem, ILoggerFactory? loggerFactory = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<S7Service>();
        }

        RegisterClientEventHandlers();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="S7Service"/> class.
    /// </summary>
    /// <param name="appConfig">The OPC UA application configuration used to initialize the client. This parameter cannot be <see langword="null"/>.</param>
    /// <param name="validateResponse">A delegate that validates the response. This parameter cannot be <see langword="null"/>.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    public S7Service(ApplicationConfiguration appConfig, Action<IList, IList> validateResponse, ILoggerFactory? loggerFactory = null)
    {
        _client = new S7UaClient(appConfig, validateResponse, loggerFactory);
        _dataStore = new S7DataStore(loggerFactory);
        _fileSystem = new FileSystem();

        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<S7Service>();
        }

        RegisterClientEventHandlers();
    }

    #endregion Constructors

    #region Deconstructors

    ~S7Service()
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

    private void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _client.Connecting -= Client_Connecting;
            _client.Connected -= Client_Connected;
            _client.Disconnecting -= Client_Disconnecting;
            _client.Disconnected -= Client_Disconnected;
            _client.Reconnecting -= Client_Reconnecting;
            _client.Reconnected -= Client_Reconnected;

            _client.Dispose();

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

    /// <inheritdoc cref="IS7Service.Connecting"/>
    public event EventHandler<ConnectionEventArgs>? Connecting;

    /// <inheritdoc cref="IS7Service.Connected"/>
    public event EventHandler<ConnectionEventArgs>? Connected;

    /// <inheritdoc cref="IS7Service.Disconnecting"/>
    public event EventHandler<ConnectionEventArgs>? Disconnecting;

    /// <inheritdoc cref="IS7Service.Disconnected"/>
    public event EventHandler<ConnectionEventArgs>? Disconnected;

    /// <inheritdoc cref="IS7Service.Reconnecting"/>
    public event EventHandler<ConnectionEventArgs>? Reconnecting;

    /// <inheritdoc cref="IS7Service.Reconnected"/>
    public event EventHandler<ConnectionEventArgs>? Reconnected;

    #endregion Connection Events

    #region Variables Events

    /// <inheritdoc cref="IS7Service.VariableValueChanged"/>
    public event EventHandler<VariableValueChangedEventArgs>? VariableValueChanged;

    #endregion Variables Events

    #endregion Public Events

    #region Public Properties

    /// <inheritdoc cref="IS7Service.KeepAliveInterval"/>
    public int KeepAliveInterval { get => _client.KeepAliveInterval; set => _client.KeepAliveInterval = value; }

    /// <inheritdoc cref="IS7Service.ReconnectPeriod"/>
    public int ReconnectPeriod { get => _client.ReconnectPeriod; set => _client.ReconnectPeriod = value; }

    /// <inheritdoc cref="IS7Service.ReconnectPeriodExponentialBackoff"/>/>
    public int ReconnectPeriodExponentialBackoff { get => _client.ReconnectPeriodExponentialBackoff; set => _client.ReconnectPeriodExponentialBackoff = value; }

    /// <inheritdoc cref="IS7Service.SessionTimeout"/>
    public uint SessionTimeout { get => _client.SessionTimeout; set => _client.SessionTimeout = value; }

    /// <inheritdoc cref="IS7Service.AcceptUntrustedCertificates"/>
    public bool AcceptUntrustedCertificates { get => _client.AcceptUntrustedCertificates; set => _client.AcceptUntrustedCertificates = value; }

    /// <inheritdoc cref="IS7Service.UserIdentity"/>
    public UserIdentity UserIdentity { get => _client.UserIdentity; set => _client.UserIdentity = value; }

    /// <inheritdoc cref="IS7Service.IsConnected"/>
    public bool IsConnected => _client.IsConnected;

    #endregion Public Properties

    #region Public Methods

    #region Connection Methods

    /// <inheritdoc cref="IS7Service.ConnectAsync(string, bool, CancellationToken)"/>
    public async Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _client.ConnectAsync(serverUrl, useSecurity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="IS7Service.DisconnectAsync(bool, CancellationToken)"/>
    public async Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _client.DisconnectAsync(leaveChannelOpen, cancellationToken);
    }

    #endregion Connection Methods

    #region Structure Discovery Methods

    /// <inheritdoc cref="IS7Service.DiscoverStructureAsync(CancellationToken)"/>
    public async Task DiscoverStructureAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Client is not connected. Please connect before discovering the structure.");
        }

        var globalDbShellsTask = _client.GetAllGlobalDataBlocksAsync(cancellationToken);
        var instanceDbShellsTask = _client.GetAllInstanceDataBlocksAsync(cancellationToken);
        var inputsTask = _client.GetInputsAsync(cancellationToken);
        var outputsTask = _client.GetOutputsAsync(cancellationToken);
        var memoryTask = _client.GetMemoryAsync(cancellationToken);
        var timersTask = _client.GetTimersAsync(cancellationToken);
        var countersTask = _client.GetCountersAsync(cancellationToken);

        await Task.WhenAll(
            globalDbShellsTask, instanceDbShellsTask, inputsTask,
            outputsTask, memoryTask, timersTask, countersTask)
            .ConfigureAwait(false);

        var globalDbShells = await globalDbShellsTask.ConfigureAwait(false);
        var instanceDbShells = await instanceDbShellsTask.ConfigureAwait(false);

        var globalDbDiscoveryTasks = (globalDbShells ?? [])
            .Select(shell => _client.DiscoverElementAsync(shell, cancellationToken));

        var instanceDbDiscoveryTasks = (instanceDbShells ?? [])
            .Select(shell => _client.DiscoverElementAsync(shell, cancellationToken));

        await Task.WhenAll(globalDbDiscoveryTasks.Concat(instanceDbDiscoveryTasks)).ConfigureAwait(false);

        var globalDbs = globalDbDiscoveryTasks
            .Select(task => task.Result as S7DataBlockGlobal)
            .Where(db => db is not null)
            .ToList()!;

        var instanceDbs = instanceDbDiscoveryTasks
            .Select(task => task.Result as S7DataBlockInstance)
            .Where(db => db is not null)
            .ToList()!;

        var inputs = await inputsTask.ConfigureAwait(false);
        var outputs = await outputsTask.ConfigureAwait(false);
        var memory = await memoryTask.ConfigureAwait(false);
        var timers = await timersTask.ConfigureAwait(false);
        var counters = await countersTask.ConfigureAwait(false);

        var simpleElementTasks = new List<Task>();
        if (inputs is not null) simpleElementTasks.Add(Task.Run(async () => inputs = await _client.DiscoverVariablesOfElementAsync(inputs, cancellationToken).ConfigureAwait(false), cancellationToken));
        if (outputs is not null) simpleElementTasks.Add(Task.Run(async () => outputs = await _client.DiscoverVariablesOfElementAsync(outputs, cancellationToken).ConfigureAwait(false), cancellationToken));
        if (memory is not null) simpleElementTasks.Add(Task.Run(async () => memory = await _client.DiscoverVariablesOfElementAsync(memory, cancellationToken).ConfigureAwait(false), cancellationToken));
        if (timers is not null) simpleElementTasks.Add(Task.Run(async () => timers = await _client.DiscoverVariablesOfElementAsync(timers, cancellationToken).ConfigureAwait(false), cancellationToken));
        if (counters is not null) simpleElementTasks.Add(Task.Run(async () => counters = await _client.DiscoverVariablesOfElementAsync(counters, cancellationToken).ConfigureAwait(false), cancellationToken));

        await Task.WhenAll(simpleElementTasks).ConfigureAwait(false);

        _dataStore.SetStructure(globalDbs!, instanceDbs!, inputs, outputs, memory, timers, counters);

        _dataStore.BuildCache();
    }

    #endregion Structure Discovery Methods

    #region Variables Access and Manipulation Methods

    /// <inheritdoc cref="IS7Service.ReadAllVariablesAsync(CancellationToken)"/>
    public async Task ReadAllVariablesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Client is not connected. Cannot read variables.");
        }

        var oldVariables = _dataStore.GetAllVariables();

        // Schritt 1: Erstellen Sie Listen von Tasks für die Leseoperationen.
        var readTasks = new List<Task>();

        var globalDbReadTasks = _dataStore.DataBlocksGlobal
            .Select(db => _client.ReadValuesOfElementAsync(db, "DataBlocksGlobal", cancellationToken))
            .ToList();
        readTasks.AddRange(globalDbReadTasks);

        var instanceDbReadTasks = _dataStore.DataBlocksInstance
            .Select(db => _client.ReadValuesOfElementAsync(db, "DataBlocksInstance", cancellationToken))
            .ToList();
        readTasks.AddRange(instanceDbReadTasks);

        Task<S7Inputs> inputsReadTask = _dataStore.Inputs is not null
            ? _client.ReadValuesOfElementAsync(_dataStore.Inputs, null, cancellationToken)
            : Task.FromResult<S7Inputs?>(null)!;
        readTasks.Add(inputsReadTask);

        Task<S7Outputs> outputsReadTask = _dataStore.Outputs is not null
            ? _client.ReadValuesOfElementAsync(_dataStore.Outputs, null, cancellationToken)
            : Task.FromResult<S7Outputs?>(null)!;
        readTasks.Add(outputsReadTask);

        Task<S7Memory> memoryReadTask = _dataStore.Memory is not null
            ? _client.ReadValuesOfElementAsync(_dataStore.Memory, null, cancellationToken)
            : Task.FromResult<S7Memory?>(null)!;
        readTasks.Add(memoryReadTask);

        Task<S7Timers> timersReadTask = _dataStore.Timers is not null
            ? _client.ReadValuesOfElementAsync(_dataStore.Timers, null, cancellationToken)
            : Task.FromResult<S7Timers?>(null)!;
        readTasks.Add(timersReadTask);

        Task<S7Counters> countersReadTask = _dataStore.Counters is not null
            ? _client.ReadValuesOfElementAsync(_dataStore.Counters, null, cancellationToken)
            : Task.FromResult<S7Counters?>(null)!;
        readTasks.Add(countersReadTask);

        await Task.WhenAll(readTasks).ConfigureAwait(false);

        var globalDbs = globalDbReadTasks.ConvertAll(task => task.Result);
        var instanceDbs = instanceDbReadTasks.ConvertAll(task => task.Result);

        var inputs = await inputsReadTask.ConfigureAwait(false);
        var outputs = await outputsReadTask.ConfigureAwait(false);
        var memory = await memoryReadTask.ConfigureAwait(false);
        var timers = await timersReadTask.ConfigureAwait(false);
        var counters = await countersReadTask.ConfigureAwait(false);

        _dataStore.SetStructure(globalDbs, instanceDbs, inputs, outputs, memory, timers, counters);
        _dataStore.BuildCache();

        var newVariables = _dataStore.GetAllVariables();

        foreach (var newVarEntry in newVariables)
        {
            if (oldVariables.TryGetValue(newVarEntry.Key, out var oldVar))
            {
                if (oldVar.Value is Array oldGenericArray && newVarEntry.Value.Value is Array newGenericArray)
                {
                    if (!oldGenericArray.Cast<object>().SequenceEqual(newGenericArray.Cast<object>()))
                    {
                        OnVariableValueChanged(new VariableValueChangedEventArgs(oldVar, newVarEntry.Value));
                    }
                }
                else if (!object.Equals(oldVar.Value, newVarEntry.Value.Value))
                {
                    OnVariableValueChanged(new VariableValueChangedEventArgs(oldVar, newVarEntry.Value));
                }
            }
        }
    }

    /// <inheritdoc cref="IS7Service.WriteVariableAsync(string, object)"/>
    public async Task<bool> WriteVariableAsync(string fullPath, object value)
    {
        ThrowIfDisposed();

        if (!_dataStore.TryGetVariableByPath(fullPath, out var variable) || variable?.NodeId is null)
        {
            _logger?.LogWarning("Cannot write to variable: Path '{Path}' not found in data store or variable has no NodeId.", fullPath);
            return false;
        }

        try
        {
            return await _client.WriteVariableAsync(variable.NodeId, value, variable.S7Type);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to write value for variable '{Path}'.", fullPath);
            return false;
        }
    }

    /// <inheritdoc cref="IS7Service.UpdateVariableTypeAsync(string, S7DataType, CancellationToken)"/>
    public async Task<bool> UpdateVariableTypeAsync(string fullPath, S7DataType newType, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!_dataStore.TryGetVariableByPath(fullPath, out var oldVariable) || oldVariable is not S7Variable oldS7Var)
        {
            _logger?.LogWarning("Cannot update type: Path '{Path}' not found or variable is not of type S7Variable.", fullPath);
            return false;
        }

        var newVariable = oldS7Var with { S7Type = newType };
        if (newType == S7DataType.STRUCT)
        {
            if (!_client.IsConnected || oldS7Var.NodeId is null)
            {
                _logger?.LogWarning("Cannot discover struct members for '{Path}' because client is disconnected or NodeId is null. The members will not be available until the next ReadAllVariables call.", fullPath);
            }
            else
            {
                try
                {
                    _logger?.LogDebug("Variable '{Path}' set to STRUCT. Discovering members immediately.", fullPath);
                    var shellForDiscovery = new S7StructureElement { NodeId = oldS7Var.NodeId, DisplayName = oldS7Var.DisplayName };
                    var discoveredMembers = (await _client.DiscoverVariablesOfElementAsync(shellForDiscovery, cancellationToken)).Variables;

                    newVariable = newVariable with { StructMembers = discoveredMembers.Cast<S7Variable>().ToList() };
                    _logger?.LogDebug("Discovered {MemberCount} members for struct '{Path}'.", discoveredMembers.Count, fullPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to discover members for struct '{Path}' during type update.", fullPath);
                }
            }
        }

        if (newVariable.RawOpcValue is not null)
        {
            try
            {
                var converter = _client.GetConverter(newType, newVariable.RawOpcValue.GetType());
                var convertedValue = converter.ConvertFromOpc(newVariable.RawOpcValue);
                newVariable = newVariable with { Value = convertedValue, SystemType = converter.TargetType };
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not reconvert raw value for '{Path}' after type change to {NewType}. Value is now null.", fullPath, newType);
                newVariable = newVariable with { Value = null, SystemType = null };
            }
        }

        bool updateSuccess = _dataStore.UpdateVariable(fullPath, newVariable);

        if (updateSuccess)
        {
            _dataStore.TryGetVariableByPath(fullPath, out var finalNewVariable);

            if (!object.Equals(oldVariable.Value, finalNewVariable?.Value))
            {
                OnVariableValueChanged(new VariableValueChangedEventArgs(oldVariable, finalNewVariable ?? newVariable));
            }
        }

        return updateSuccess;
    }

    /// <inheritdoc cref="IS7Service.GetVariable(string)"/>
    public IS7Variable? GetVariable(string fullPath)
    {
        ThrowIfDisposed();

        _dataStore.TryGetVariableByPath(fullPath, out var variable);
        return variable;
    }

    #endregion Variables Access and Manipulation Methods

    #region Persistence Methods

    /// <inheritdoc cref="IS7Service.SaveStructureAsync(string)"/>
    public async Task SaveStructureAsync(string filePath)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrEmpty(filePath);

        var structureModel = new S7StructureStorageModel
        {
            DataBlocksGlobal = _dataStore.DataBlocksGlobal,
            DataBlocksInstance = _dataStore.DataBlocksInstance,
            Inputs = _dataStore.Inputs,
            Outputs = _dataStore.Outputs,
            Memory = _dataStore.Memory,
            Timers = _dataStore.Timers,
            Counters = _dataStore.Counters
        };

        try
        {
            _logger?.LogInformation("Saving S7 structure to file: {FilePath}", filePath);
            await using var fileStream = _fileSystem.File.Create(filePath);

            await JsonSerializer.SerializeAsync(fileStream, structureModel, S7StructureSerializer.Options);
            _logger?.LogInformation("S7 structure successfully saved.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save S7 structure to file: {FilePath}", filePath);
            throw;
        }
    }

    /// <inheritdoc cref="IS7Service.SaveStructureAsync(string)"/>
    public async Task LoadStructureAsync(string filePath)
    {
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrEmpty(filePath);

        if (!_fileSystem.File.Exists(filePath))
        {
            _logger?.LogError("Cannot load S7 structure. File not found: {FilePath}", filePath);
            throw new FileNotFoundException("The structure file was not found.", filePath);
        }

        try
        {
            _logger?.LogInformation("Loading S7 structure from file: {FilePath}", filePath);
            await using var fileStream = _fileSystem.File.OpenRead(filePath);

            var loadedModel = await JsonSerializer.DeserializeAsync<S7StructureStorageModel>(fileStream, S7StructureSerializer.Options)
                ?? throw new JsonException("Deserialization resulted in a null structure model.");

            _dataStore.SetStructure(loadedModel.DataBlocksGlobal, loadedModel.DataBlocksInstance, loadedModel.Inputs, loadedModel.Outputs, loadedModel.Memory, loadedModel.Timers, loadedModel.Counters);

            _logger?.LogInformation("S7 structure successfully loaded. Rebuilding internal cache...");

            _dataStore.BuildCache();
            _logger?.LogInformation("Internal cache rebuilt.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load S7 structure from file: {FilePath}", filePath);
            throw;
        }
    }

    #endregion Persistence Methods

    #endregion Public Methods

    #region Private Methods

    protected virtual void OnVariableValueChanged(VariableValueChangedEventArgs e)
    {
        VariableValueChanged?.Invoke(this, e);
    }

    private void RegisterClientEventHandlers()
    {
        _client.Connecting += Client_Connecting;
        _client.Connected += Client_Connected;
        _client.Disconnecting += Client_Disconnecting;
        _client.Disconnected += Client_Disconnected;
        _client.Reconnecting += Client_Reconnecting;
        _client.Reconnected += Client_Reconnected;
    }

    private void Client_Connecting(object? sender, ConnectionEventArgs args)
    {
        Connecting?.Invoke(sender, args);
    }

    private void Client_Connected(object? sender, ConnectionEventArgs args)
    {
        Connected?.Invoke(sender, args);
    }

    private void Client_Disconnecting(object? sender, ConnectionEventArgs args)
    {
        Disconnecting?.Invoke(sender, args);
    }

    private void Client_Disconnected(object? sender, ConnectionEventArgs args)
    {
        Disconnected?.Invoke(sender, args);
    }

    private void Client_Reconnecting(object? sender, ConnectionEventArgs args)
    {
        Reconnecting?.Invoke(sender, args);
    }

    private void Client_Reconnected(object? sender, ConnectionEventArgs args)
    {
        Reconnected?.Invoke(sender, args);
    }

    #endregion Private Methods
}