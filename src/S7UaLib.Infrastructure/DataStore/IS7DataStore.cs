using S7UaLib.Core.S7.Structure;
using System.Diagnostics.CodeAnalysis;

namespace S7UaLib.Infrastructure.DataStore;

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
    public IReadOnlyList<IS7DataBlockGlobal> DataBlocksGlobal { get; }

    /// <summary>
    /// Gets the collection of instance data blocks.
    /// </summary>
    public IReadOnlyList<IS7DataBlockInstance> DataBlocksInstance { get; }

    /// <summary>
    /// Gets the S7 Input memory area.
    /// </summary>
    public IS7Inputs? Inputs { get; }

    /// <summary>
    /// Gets the S7 Output memory area.
    /// </summary>
    public IS7Outputs? Outputs { get; }

    /// <summary>
    /// Gets the S7 Memory/Flag area.
    /// </summary>
    public IS7Memory? Memory { get; }

    /// <summary>
    /// Gets the S7 Timers memory area.
    /// </summary>
    public IS7Timers? Timers { get; }

    /// <summary>
    /// Gets the S7 Counters memory area.
    /// </summary>
    public IS7Counters? Counters { get; }

    /// <summary>
    /// Adds a new variable to the store at the specified full path. This modifies the
    /// immutable hierarchy and rebuilds the cache. The parent element must exist.
    /// </summary>
    /// <param name="newVariable">The new variable instance to add.</param>
    /// <returns>True if the variable was added successfully; otherwise, false.</returns>
    public bool AddVariableToCache(IS7Variable newVariable);

    /// <summary>
    /// Updates the structure of the S7 PLC data by setting the provided data blocks, inputs, outputs, memory, timers,
    /// and counters.
    /// </summary>
    /// <remarks>This method is thread-safe and ensures that the structure is updated atomically. Use this
    /// method to configure or reconfigure the PLC data structure.</remarks>
    /// <param name="globalDbs">A read-only list of global data blocks to be set. Cannot be null.</param>
    /// <param name="instDbs">A read-only list of instance data blocks to be set. Cannot be null.</param>
    /// <param name="inputs">The input configuration for the PLC. Can be null.</param>
    /// <param name="outputs">The output configuration for the PLC. Can be null.</param>
    /// <param name="memory">The memory configuration for the PLC. Can be null.</param>
    /// <param name="timers">The timer configuration for the PLC. Can be null.</param>
    /// <param name="counters">The counter configuration for the PLC. Can be null.</param>
    public void SetStructure(IReadOnlyList<IS7DataBlockGlobal> globalDbs, IReadOnlyList<IS7DataBlockInstance> instDbs, IS7Inputs? inputs, IS7Outputs? outputs, IS7Memory? memory, IS7Timers? timers, IS7Counters? counters);

    /// <summary>
    /// Filters variables in cache based on a predicate.
    /// </summary>
    /// <param name="predicate">A function to test each variable for a condition.</param>
    /// <returns>A list containing the variables that fulfill the condition.</returns>
    public IReadOnlyList<IS7Variable> FindVariablesWhere(Func<IS7Variable, bool> predicate);

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