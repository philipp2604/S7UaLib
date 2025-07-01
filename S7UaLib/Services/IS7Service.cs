using S7UaLib.Events;
using S7UaLib.S7.Structure.Contracts;
using S7UaLib.S7.Types;

namespace S7UaLib.Services;

/// <summary>
/// Defines the contract for interacting with an S7 service, including operations for discovering structure,  reading
/// and writing variables, and managing variable types.
/// </summary>
/// <remarks>This interface provides methods and events for working with S7 PLCs, including reading and writing
/// variable values,  discovering the server structure, and updating variable types. Implementations of this interface
/// are expected to  handle communication with the PLC and manage the internal data store.</remarks>
internal interface IS7Service
{
    /// <summary>
    /// Occurs when a variable's value changes after a read operation.
    /// </summary>
    public event EventHandler<VariableValueChangedEventArgs>? VariableValueChanged;

    /// <summary>
    /// Discovers the entire structure of the OPC UA server and populates the internal data store.
    /// This includes all data blocks, I/O areas, and their variables.
    /// </summary>
    public void DiscoverStructure();

    /// <summary>
    /// Reads the values of all discovered variables from the PLC.
    /// Raises the VariableValueChanged event for any variable whose value has changed.
    /// </summary>
    public void ReadAllVariables();

    /// <summary>
    /// Writes a value to a variable specified by its full symbolic path.
    /// </summary>
    /// <param name="fullPath">The full symbolic path of the variable to write to.</param>
    /// <param name="value">The user-friendly .NET value to write.</param>
    /// <returns>A task that returns true if the write was successful; otherwise, false.</returns>
    public Task<bool> WriteVariableAsync(string fullPath, object value);

    /// <summary>
    /// Updates the S7 data type of a variable in the data store and attempts to reconvert its current raw value.
    /// If the conversion is successful, it raises the <see cref="VariableValueChanged"/> event.
    /// </summary>
    /// <param name="fullPath">The full path of the variable to update.</param>
    /// <param name="newType">The new <see cref="S7DataType"/> to apply.</param>
    /// <returns>True if the variable was found and the type was updated; otherwise, false.</returns>
    public bool UpdateVariableType(string fullPath, S7DataType newType);

    /// <summary>
    /// Retrieves a variable from the data store by its full symbolic path.
    /// </summary>
    /// <param name="fullPath">The full path of the variable (e.g., "DataBlocksGlobal.MyDb.MyVar").</param>
    /// <returns>The <see cref="IS7Variable"/> if found; otherwise, null.</returns>
    public IS7Variable? GetVariable(string fullPath);

    /// <summary>
    /// Saves the current entire PLC structure from the data store to a JSON file.
    /// This includes all discovered elements and their assigned data types.
    /// </summary>
    /// <param name="filePath">The path to the file where the structure will be saved.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveStructureAsync(string filePath);

    /// <summary>
    /// Loads the PLC structure from a JSON file into the data store, bypassing the need for discovery.
    /// After loading, the internal cache is automatically rebuilt.
    /// </summary>
    /// <param name="filePath">The path to the file from which the structure will be loaded.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task LoadStructureAsync(string filePath);
}