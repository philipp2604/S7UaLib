using Microsoft.Extensions.Logging;
using Opc.Ua;
using S7UaLib.Client;
using S7UaLib.Client.Contracts;
using S7UaLib.DataStore;
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
public class S7Service
{
    private readonly IS7UaClient _client;
    private readonly S7DataStore _dataStore;
    private readonly ILogger? _logger;

    /// <summary>
    /// Occurs when a variable's value changes after a read operation.
    /// </summary>
    public event EventHandler<VariableValueChangedEventArgs>? VariableValueChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="S7Service"/> class.
    /// </summary>
    /// <param name="client">The S7UaClient instance to use for communication.</param>
    /// <param name="dataStore">The S7DataStore instance to use as data store.</param>
    /// <param name="loggerFactory">An optional factory for creating loggers. If <see langword="null"/>, logging will not be enabled.</param>
    internal S7Service(IS7UaClient client, S7DataStore dataStore, ILoggerFactory? loggerFactory = null)
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

    /// <summary>
    /// Discovers the entire structure of the OPC UA server and populates the internal data store.
    /// This includes all data blocks, I/O areas, and their variables.
    /// </summary>
    public void DiscoverStructure()
    {
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Client is not connected. Please connect before discovering the structure.");
        }

        // 1. Get all top-level "shell" elements
        var globalDbShells = _client.GetAllGlobalDataBlocks();
        var instanceDbShells = _client.GetAllInstanceDataBlocks();
        _dataStore.Inputs = _client.GetInputs();
        _dataStore.Outputs = _client.GetOutputs();
        _dataStore.Memory = _client.GetMemory();
        _dataStore.Timers = _client.GetTimers();
        _dataStore.Counters = _client.GetCounters();

        // 2. Discover the full structure of each shell element
        _dataStore.DataBlocksGlobal = (globalDbShells ?? []) // <--- FIX
            .Select(shell => _client.DiscoverElement(shell) as S7DataBlockGlobal)
            .Where(db => db is not null)
            .ToList()!;

        _dataStore.DataBlocksInstance = (instanceDbShells ?? []) // <--- FIX
            .Select(shell => _client.DiscoverElement(shell) as S7DataBlockInstance)
            .Where(db => db is not null)
            .ToList()!;

        if (_dataStore.Inputs is not null) _dataStore.Inputs = _client.DiscoverVariablesOfElement(_dataStore.Inputs);
        if (_dataStore.Outputs is not null) _dataStore.Outputs = _client.DiscoverVariablesOfElement(_dataStore.Outputs);
        if (_dataStore.Memory is not null) _dataStore.Memory = _client.DiscoverVariablesOfElement(_dataStore.Memory);
        if (_dataStore.Timers is not null) _dataStore.Timers = _client.DiscoverVariablesOfElement(_dataStore.Timers);
        if (_dataStore.Counters is not null) _dataStore.Counters = _client.DiscoverVariablesOfElement(_dataStore.Counters);

        // 3. Build the cache for fast path-based access
        _dataStore.BuildCache();
    }

    /// <summary>
    /// Reads the values of all discovered variables from the PLC.
    /// Raises the VariableValueChanged event for any variable whose value has changed.
    /// </summary>
    public void ReadAllVariables()
    {
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("Client is not connected. Cannot read variables.");
        }

        var oldVariables = _dataStore.GetAllVariables();

        // Read and update each top-level element in the store
        _dataStore.DataBlocksGlobal = [.. _dataStore.DataBlocksGlobal.Select(db => _client.ReadValuesOfElement(db, "DataBlocksGlobal"))];

        _dataStore.DataBlocksInstance = [.. _dataStore.DataBlocksInstance.Select(db => _client.ReadValuesOfElement(db, "DataBlocksInstance"))];

        if (_dataStore.Inputs is not null) _dataStore.Inputs = _client.ReadValuesOfElement(_dataStore.Inputs);
        if (_dataStore.Outputs is not null) _dataStore.Outputs = _client.ReadValuesOfElement(_dataStore.Outputs);
        if (_dataStore.Memory is not null) _dataStore.Memory = _client.ReadValuesOfElement(_dataStore.Memory);
        if (_dataStore.Timers is not null) _dataStore.Timers = _client.ReadValuesOfElement(_dataStore.Timers);
        if (_dataStore.Counters is not null) _dataStore.Counters = _client.ReadValuesOfElement(_dataStore.Counters);

        // Rebuild the cache with the new values
        _dataStore.BuildCache();
        var newVariables = _dataStore.GetAllVariables();

        // Compare old and new values and raise events
        foreach (var newVarEntry in newVariables)
        {
            if (oldVariables.TryGetValue(newVarEntry.Key, out var oldVar))
            {
                // Compare values. Note: object.Equals handles nulls correctly.
                if (!object.Equals(oldVar.Value, newVarEntry.Value.Value))
                {
                    OnVariableValueChanged(new VariableValueChangedEventArgs(oldVar, newVarEntry.Value));
                }
            }
        }
    }

    /// <summary>
    /// Writes a value to a variable specified by its full symbolic path.
    /// </summary>
    /// <param name="fullPath">The full symbolic path of the variable to write to.</param>
    /// <param name="value">The user-friendly .NET value to write.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
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

    /// <summary>
    /// Updates the S7 data type of a variable in the data store and attempts to reconvert its current raw value.
    /// If the conversion is successful, it raises the <see cref="VariableValueChanged"/> event.
    /// </summary>
    /// <param name="fullPath">The full path of the variable to update.</param>
    /// <param name="newType">The new <see cref="S7DataType"/> to apply.</param>
    /// <returns>True if the variable was found and the type was updated; otherwise, false.</returns>
    public bool UpdateVariableType(string fullPath, S7DataType newType)
    {
        if (!_dataStore.TryGetVariableByPath(fullPath, out var oldVariable) || oldVariable is not S7Variable oldS7Var)
        {
            _logger?.LogWarning("Cannot update type: Path '{Path}' not found or variable is not of type S7Variable.", fullPath);
            return false;
        }

        // Create the new variable with the new type but preserving all other properties
        var newVariable = oldS7Var with { S7Type = newType };

        // Attempt to reconvert the existing raw value with the new type's converter
        if (newVariable.RawOpcValue is not null)
        {
            try
            {
                // Make S7UaClient.GetConverter public or internal
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
            // Get the truly new variable state from the store after update and cache rebuild
            _dataStore.TryGetVariableByPath(fullPath, out var finalNewVariable);

            // If the value changed due to reconversion, fire the event
            if (!object.Equals(oldVariable.Value, finalNewVariable?.Value))
            {
                OnVariableValueChanged(new VariableValueChangedEventArgs(oldVariable, finalNewVariable ?? newVariable));
            }
        }

        return updateSuccess;
    }

    /// <summary>
    /// Retrieves a variable from the data store by its full symbolic path.
    /// </summary>
    /// <param name="fullPath">The full path of the variable (e.g., "DataBlocksGlobal.MyDb.MyVar").</param>
    /// <returns>The <see cref="IS7Variable"/> if found; otherwise, null.</returns>
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