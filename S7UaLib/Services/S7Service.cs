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
    private readonly IS7UaClient _client;
    private readonly IS7DataStore _dataStore;
    private readonly ILogger? _logger;
    private readonly IFileSystem _fileSystem;

    /// <inheritdoc cref="IS7Service.VariableValueChanged"/>
    public event EventHandler<VariableValueChangedEventArgs>? VariableValueChanged;

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

    #region Connection Methods

    /// <inheritdoc cref="IS7Service.ConnectAsync(string, bool, CancellationToken)"/>
    public async Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)
    {
        await _client.ConnectAsync(serverUrl, useSecurity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="IS7Service.Disconnect(bool)"/>
    public void Disconnect(bool leaveChannelOpen = false)
    {
        _client.Disconnect(leaveChannelOpen);
    }

    #endregion Connection Methods

    /// <inheritdoc cref="IS7Service.DiscoverStructure"/>
    public void DiscoverStructure()
    {
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Client is not connected. Please connect before discovering the structure.");
        }

        var globalDbShells = _client.GetAllGlobalDataBlocks();
        var instanceDbShells = _client.GetAllInstanceDataBlocks();
        _dataStore.Inputs = _client.GetInputs();
        _dataStore.Outputs = _client.GetOutputs();
        _dataStore.Memory = _client.GetMemory();
        _dataStore.Timers = _client.GetTimers();
        _dataStore.Counters = _client.GetCounters();

        _dataStore.DataBlocksGlobal = (globalDbShells ?? [])
            .Select(shell => _client.DiscoverElement(shell) as S7DataBlockGlobal)
            .Where(db => db is not null)
            .ToList()!;

        _dataStore.DataBlocksInstance = (instanceDbShells ?? [])
            .Select(shell => _client.DiscoverElement(shell) as S7DataBlockInstance)
            .Where(db => db is not null)
            .ToList()!;

        if (_dataStore.Inputs is not null) _dataStore.Inputs = _client.DiscoverVariablesOfElement(_dataStore.Inputs);
        if (_dataStore.Outputs is not null) _dataStore.Outputs = _client.DiscoverVariablesOfElement(_dataStore.Outputs);
        if (_dataStore.Memory is not null) _dataStore.Memory = _client.DiscoverVariablesOfElement(_dataStore.Memory);
        if (_dataStore.Timers is not null) _dataStore.Timers = _client.DiscoverVariablesOfElement(_dataStore.Timers);
        if (_dataStore.Counters is not null) _dataStore.Counters = _client.DiscoverVariablesOfElement(_dataStore.Counters);

        _dataStore.BuildCache();
    }

    /// <inheritdoc cref="IS7Service.ReadAllVariables"/>
    public void ReadAllVariables()
    {
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Client is not connected. Cannot read variables.");
        }

        var oldVariables = _dataStore.GetAllVariables();

        _dataStore.DataBlocksGlobal = [.. _dataStore.DataBlocksGlobal.Select(db => _client.ReadValuesOfElement(db, "DataBlocksGlobal"))];

        _dataStore.DataBlocksInstance = [.. _dataStore.DataBlocksInstance.Select(db => _client.ReadValuesOfElement(db, "DataBlocksInstance"))];

        if (_dataStore.Inputs is not null) _dataStore.Inputs = _client.ReadValuesOfElement(_dataStore.Inputs);
        if (_dataStore.Outputs is not null) _dataStore.Outputs = _client.ReadValuesOfElement(_dataStore.Outputs);
        if (_dataStore.Memory is not null) _dataStore.Memory = _client.ReadValuesOfElement(_dataStore.Memory);
        if (_dataStore.Timers is not null) _dataStore.Timers = _client.ReadValuesOfElement(_dataStore.Timers);
        if (_dataStore.Counters is not null) _dataStore.Counters = _client.ReadValuesOfElement(_dataStore.Counters);

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

    /// <inheritdoc cref="IS7Service.UpdateVariableType(string, S7DataType)"/>
    public bool UpdateVariableType(string fullPath, S7DataType newType)
    {
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
                    var discoveredMembers = _client.DiscoverVariablesOfElement(shellForDiscovery).Variables;

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
                var converter = S7UaClient.GetConverter(newType, newVariable.RawOpcValue.GetType());
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
        _dataStore.TryGetVariableByPath(fullPath, out var variable);
        return variable;
    }

    #region Persistence methods

    /// <inheritdoc cref="IS7Service.SaveStructureAsync(string)"/>
    public async Task SaveStructureAsync(string filePath)
    {
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

            _dataStore.DataBlocksGlobal = loadedModel.DataBlocksGlobal;
            _dataStore.DataBlocksInstance = loadedModel.DataBlocksInstance;
            _dataStore.Inputs = loadedModel.Inputs;
            _dataStore.Outputs = loadedModel.Outputs;
            _dataStore.Memory = loadedModel.Memory;
            _dataStore.Timers = loadedModel.Timers;
            _dataStore.Counters = loadedModel.Counters;

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

    #endregion Persistence methods

    protected virtual void OnVariableValueChanged(VariableValueChangedEventArgs e)
    {
        VariableValueChanged?.Invoke(this, e);
    }

    private void RegisterClientEventHandlers()
    {
        _client.Connecting += (s, e) => Connecting?.Invoke(s, e);
        _client.Connected += (s, e) => Connected?.Invoke(s, e);
        _client.Disconnecting += (s, e) => Disconnecting?.Invoke(s, e);
        _client.Disconnected += (s, e) => Disconnected?.Invoke(s, e);
        _client.Reconnecting += (s, e) => Reconnecting?.Invoke(s, e);
        _client.Reconnected += (s, e) => Reconnected?.Invoke(s, e);
    }
}