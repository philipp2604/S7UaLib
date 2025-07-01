using S7UaLib.S7.Structure;
using S7UaLib.S7.Structure.Contracts;
using System.Diagnostics.CodeAnalysis;

namespace S7UaLib.DataStore.Contracts;

/// <summary>
/// Represents a data store for managing and accessing S7 PLC memory areas and variables.
/// </summary>
/// <remarks>This interface provides access to various S7 memory areas, including global and instance data blocks,
/// inputs, outputs, memory/flags, timers, and counters. It also includes methods for retrieving, updating, and caching
/// variables by their symbolic paths. Implementations of this interface are expected to handle the internal structure
/// and caching of variables to ensure efficient access and consistency.</remarks>
internal interface IS7DataStore
{
    /// <summary>
    /// Gets the collection of global data blocks.
    /// </summary>
    public IReadOnlyList<S7DataBlockGlobal> DataBlocksGlobal { get; set; }

    /// <summary>
    /// Gets the collection of instance data blocks.
    /// </summary>
    public IReadOnlyList<S7DataBlockInstance> DataBlocksInstance { get; set; }

    /// <summary>
    /// Gets the S7 Input memory area.
    /// </summary>
    public S7Inputs? Inputs { get; set; }

    /// <summary>
    /// Gets the S7 Output memory area.
    /// </summary>
    public S7Outputs? Outputs { get; set; }

    /// <summary>
    /// Gets the S7 Memory/Flag area.
    /// </summary>
    public S7Memory? Memory { get; set; }

    /// <summary>
    /// Gets the S7 Timers memory area.
    /// </summary>
    public S7Timers? Timers { get; set; }

    /// <summary>
    /// Gets the S7 Counters memory area.
    /// </summary>
    public S7Counters? Counters { get; set; }

    /// <summary>
    /// Tries to retrieve a variable by its full symbolic path.
    /// </summary>
    /// <param name="fullPath">The full path of the variable (e.g., "DataBlocksGlobal.MyDb.MyVar").</param>
    /// <param name="variable">The found variable, or null if not found.</param>
    /// <returns>True if the variable was found; otherwise, false.</returns>
    public bool TryGetVariableByPath(string fullPath, [MaybeNullWhen(false)] out IS7Variable variable);

    /// <summary>
    /// Gets a dictionary of all variables keyed by their full path.
    /// </summary>
    /// <returns>A new dictionary containing all cached variables.</returns>
    public IReadOnlyDictionary<string, IS7Variable> GetAllVariables();

    /// <summary>
    /// Clears and rebuilds the internal variable cache from the stored structure elements.
    /// This should be called after the structure is discovered or modified.
    /// </summary>
    public void BuildCache();

    /// <summary>
    /// Updates a variable in the store by its full path. This replaces the variable in the
    /// immutable hierarchy and rebuilds the cache to ensure data consistency.
    /// </summary>
    /// <param name="fullPath">The full path of the variable to replace.</param>
    /// <param name="newVariable">The new variable instance.</param>
    /// <returns>True if the variable was found and replaced; otherwise, false.</returns>
    public bool UpdateVariable(string fullPath, IS7Variable newVariable);
}