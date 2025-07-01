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
using System.Collections;

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

    /// <inheritdoc cref="IS7Service.VariableValueChanged"/>
    public event EventHandler<VariableValueChangedEventArgs>? VariableValueChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="S7Service"/> class.
    /// </summary>
    /// <param name="client">The S7UaClient instance to use for communication.</param>
    /// <param name="dataStore">The S7DataStore instance to use as data store.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    internal S7Service(IS7UaClient client, IS7DataStore dataStore, ILoggerFactory? loggerFactory = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _dataStore = dataStore ?? throw new ArgumentNullException(nameof(dataStore));

        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<S7Service>();
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="S7Service"/> class.
    /// </summary>
    /// <param name="appConfig">The OPC UA application configuration used to initialize the client. This parameter cannot be <see langword="null"/>.</param>
    /// <param name="validateResponse">A delegate that validates the response. This parameter cannot be <see langword="null"/>.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="appConfig"/> or <paramref name="validateResponse"/> is <see langword="null"/>.</exception>
    public S7Service(ApplicationConfiguration appConfig, Action<IList, IList> validateResponse, ILoggerFactory? loggerFactory = null)
    {
        _client = new S7UaClient(appConfig, validateResponse, loggerFactory);
        _dataStore = new S7DataStore(loggerFactory);

        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<S7Service>();
        }
    }

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
                if (!object.Equals(oldVar.Value, newVarEntry.Value.Value))
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

    protected virtual void OnVariableValueChanged(VariableValueChangedEventArgs e)
    {
        VariableValueChanged?.Invoke(this, e);
    }
}