using Microsoft.Extensions.Logging;
using S7UaLib.Core.Enums;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using System.Diagnostics.CodeAnalysis;

namespace S7UaLib.Infrastructure.DataStore;

/// <summary>
/// Represents an in-memory storage for the entire S7 PLC structure.
/// It holds the discovered elements and provides a fast, path-based lookup cache for variables.
/// </summary>
internal class S7DataStore : IS7DataStore
{
    public S7DataStore(ILoggerFactory? loggerFactory = null)
    {
        if (loggerFactory != null)
        {
            _logger = loggerFactory.CreateLogger<S7DataStore>();
        }

        Inputs = new S7Inputs { DisplayName = "Inputs", FullPath = "Inputs", NodeId = S7StructureConstants._s7InputsNamespaceIdentifier };
        Outputs = new S7Outputs { DisplayName = "Outputs", FullPath = "Outputs", NodeId = S7StructureConstants._s7OutputsNamespaceIdentifier };
        Memory = new S7Memory { DisplayName = "Memory", FullPath = "Memory", NodeId = S7StructureConstants._s7MemoryNamespaceIdentifier };
        Timers = new S7Timers { DisplayName = "Timers", FullPath = "Timers", NodeId = S7StructureConstants._s7TimersNamespaceIdentifier };
        Counters = new S7Counters { DisplayName = "Counters", FullPath = "Counters", NodeId = S7StructureConstants._s7CountersNamespaceIdentifier };
    }

    private readonly Dictionary<string, IS7Variable> _variableCacheByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;

#if NET8_0
    private readonly object _lock = new();
#elif NET9_0_OR_GREATER
    private readonly System.Threading.Lock _lock = new();
#endif

    /// <inheritdoc cref="IS7DataStore.DataBlocksGlobal"/>
    public IReadOnlyList<IS7DataBlockGlobal> DataBlocksGlobal { get; private set; } = [];

    /// <inheritdoc cref="IS7DataStore.DataBlocksInstance"/>
    public IReadOnlyList<IS7DataBlockInstance> DataBlocksInstance { get; private set; } = [];

    /// <inheritdoc cref="IS7DataStore.Inputs"/>
    public IS7Inputs Inputs { get; private set; }

    /// ´<inheritdoc cref="IS7DataStore.Outputs"/>
    public IS7Outputs Outputs { get; private set; }

    /// <inheritdoc cref="IS7DataStore.Memory"/>
    public IS7Memory Memory { get; private set; }

    /// <inheritdoc cref="IS7DataStore.Timers"/>
    public IS7Timers Timers { get; private set; }

    /// <inheritdoc cref="IS7DataStore.Counters"/>
    public IS7Counters Counters { get; private set; }

    /// <inheritdoc cref="IS7DataStore.AddVariableToCache(IS7Variable)"/>
    public bool AddVariableToCache(IS7Variable newVariable)
    {
        lock (_lock)
        {
            if (_variableCacheByPath.ContainsKey(newVariable.FullPath!))
            {
                _logger?.LogWarning("Cannot add variable: A variable with path '{Path}' already exists.", newVariable.FullPath!);
                return false;
            }

            var pathSegments = newVariable.FullPath!.Split('.');
            if (pathSegments.Length < 2)
            {
                _logger?.LogWarning("Cannot add variable: The path '{Path}' is not valid. It must contain at least a root and a variable name.", newVariable.FullPath!);
                return false;
            }

            var rootName = pathSegments[0];
            var remainingPath = string.Join(".", pathSegments.Skip(1));
            bool added = false;

            if (rootName.Equals("DataBlocksGlobal", StringComparison.OrdinalIgnoreCase))
            {
                (var newList, added) = AddVariableToList(DataBlocksGlobal, remainingPath, newVariable);
                if (added) DataBlocksGlobal = newList;
            }
            else if (rootName.Equals("DataBlocksInstance", StringComparison.OrdinalIgnoreCase))
            {
                (var newList, added) = AddVariableToList(DataBlocksInstance, remainingPath, newVariable);
                if (added) DataBlocksInstance = newList;
            }
            else if (Inputs is not null && rootName.Equals(Inputs.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var (newElement, success) = AddVariableToElement(Inputs, remainingPath, newVariable);
                if (success)
                {
                    Inputs = (S7Inputs)newElement;
                    added = true;
                }
            }
            else if (Outputs is not null && rootName.Equals(Outputs.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var (newElement, success) = AddVariableToElement(Outputs, remainingPath, newVariable);
                if (success)
                {
                    Outputs = (S7Outputs)newElement;
                    added = true;
                }
            }
            else if (Memory is not null && rootName.Equals(Memory.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var (newElement, success) = AddVariableToElement(Memory, remainingPath, newVariable);
                if (success)
                {
                    Memory = (S7Memory)newElement;
                    added = true;
                }
            }
            else if (Timers is not null && rootName.Equals(Timers.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var (newElement, success) = AddVariableToElement(Timers, remainingPath, newVariable);
                if (success)
                {
                    Timers = (S7Timers)newElement;
                    added = true;
                }
            }
            else if (Counters is not null && rootName.Equals(Counters.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var (newElement, success) = AddVariableToElement(Counters, remainingPath, newVariable);
                if (success)
                {
                    Counters = (S7Counters)newElement;
                    added = true;
                }
            }

            if (added)
            {
                _logger?.LogDebug("Variable '{FullPath}' added to data store. Rebuilding cache.", newVariable.FullPath!);
                BuildCache();
            }
            else
            {
                _logger?.LogWarning("Failed to add variable '{FullPath}'. Parent path might not exist.", newVariable.FullPath!);
            }

            return added;
        }
    }

    /// <inheritdoc cref="IS7DataStore.SetStructure(IReadOnlyList{IS7DataBlockGlobal}, IReadOnlyList{IS7DataBlockInstance}, IS7Inputs?, IS7Outputs?, IS7Memory?, IS7Timers?, IS7Counters?)"/>
    public void SetStructure(
        IReadOnlyList<IS7DataBlockGlobal> globalDbs,
        IReadOnlyList<IS7DataBlockInstance> instDbs,
        IS7Inputs? inputs,
        IS7Outputs? outputs,
        IS7Memory? memory,
        IS7Timers? timers,
        IS7Counters? counters)
    {
        lock (_lock)
        {
            DataBlocksGlobal = globalDbs;
            DataBlocksInstance = instDbs;
            Inputs = inputs ?? new S7Inputs() { DisplayName = "Inputs", FullPath = "Inputs" };
            Outputs = outputs ?? new S7Outputs { DisplayName = "Outputs", FullPath = "Outputs" };
            Memory = memory ?? new S7Memory { DisplayName = "Memory", FullPath = "Memory" };
            Timers = timers ?? new S7Timers { DisplayName = "Timers", FullPath = "Timers" };
            Counters = counters ?? new S7Counters { DisplayName = "Counters", FullPath = "Counters" };
        }
    }

    /// <inheritdoc cref="IS7DataStore.FindVariablesWhere(Func{IS7Variable, bool})"/>
    public IReadOnlyList<IS7Variable> FindVariablesWhere(Func<IS7Variable, bool> predicate)
    {
        lock(_lock)
        {
            return [.. _variableCacheByPath.Values.Where(predicate)];
        }
    }

    /// <inheritdoc cref="IS7DataStore.TryGetVariableByPath(string, out IS7Variable)"/>
    public bool TryGetVariableByPath(string fullPath, [MaybeNullWhen(false)] out IS7Variable variable)
    {
        lock (_lock)
        {
            return _variableCacheByPath.TryGetValue(fullPath, out variable);
        }
    }

    /// <inheritdoc cref="IS7DataStore.GetAllVariables"/>
    public IReadOnlyDictionary<string, IS7Variable> GetAllVariables()
    {
        lock (_lock)
        {
            return new Dictionary<string, IS7Variable>(_variableCacheByPath, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc cref="IS7DataStore.BuildCache"/>
    public void BuildCache()
    {
        lock (_lock)
        {
            _logger?.LogDebug("Starting to build variable cache.");
            _variableCacheByPath.Clear();

            foreach (var db in DataBlocksGlobal)
            {
                AddVariablesToCacheRecursively(db, "DataBlocksGlobal");
            }
            foreach (var db in DataBlocksInstance)
            {
                AddVariablesToCacheRecursively(db, "DataBlocksInstance");
            }

            if (Inputs is not null) AddVariablesToCacheRecursively(Inputs, null);
            if (Outputs is not null) AddVariablesToCacheRecursively(Outputs, null);
            if (Memory is not null) AddVariablesToCacheRecursively(Memory, null);
            if (Timers is not null) AddVariablesToCacheRecursively(Timers, null);
            if (Counters is not null) AddVariablesToCacheRecursively(Counters, null);

            _logger?.LogDebug("Variable cache build completed. Cached {Count} variables.", _variableCacheByPath.Count);
        }
    }

    /// <inheritdoc cref="IS7DataStore.UpdateVariable(string, IS7Variable)"/>
    public bool UpdateVariable(string fullPath, IS7Variable newVariable)
    {
        lock (_lock)
        {
            var pathSegments = fullPath.Split('.');
            if (pathSegments.Length < 2) return false;

            var rootName = pathSegments[0];
            var remainingPath = string.Join(".", pathSegments.Skip(1));
            bool replaced = false;

            if (rootName.Equals("DataBlocksGlobal", StringComparison.OrdinalIgnoreCase))
            {
                (var newList, replaced) = ReplaceVariableInList(DataBlocksGlobal, remainingPath, newVariable);
                if (replaced) DataBlocksGlobal = newList;
            }
            else if (rootName.Equals("DataBlocksInstance", StringComparison.OrdinalIgnoreCase))
            {
                (var newList, replaced) = ReplaceVariableInList(DataBlocksInstance, remainingPath, newVariable);
                if (replaced) DataBlocksInstance = newList;
            }
            else if (Inputs is not null && rootName.Equals(Inputs.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var newElement = ReplaceVariableInElement(Inputs, remainingPath, newVariable);
                if (!Object.ReferenceEquals(Inputs, newElement))
                {
                    Inputs = (S7Inputs)newElement;
                    replaced = true;
                }
            }
            else if (Outputs is not null && rootName.Equals(Outputs.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var newElement = ReplaceVariableInElement(Outputs, remainingPath, newVariable);
                if (!Object.ReferenceEquals(Outputs, newElement))
                {
                    Outputs = (S7Outputs)newElement;
                    replaced = true;
                }
            }
            else if (Memory is not null && rootName.Equals(Memory.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var newElement = ReplaceVariableInElement(Memory, remainingPath, newVariable);
                if (!Object.ReferenceEquals(Memory, newElement))
                {
                    Memory = (S7Memory)newElement;
                    replaced = true;
                }
            }
            else if (Timers is not null && rootName.Equals(Timers.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var newElement = ReplaceVariableInElement(Timers, remainingPath, newVariable);
                if (!Object.ReferenceEquals(newElement, Timers))
                {
                    Timers = (S7Timers)newElement;
                    replaced = true;
                }
            }
            else if (Counters is not null && rootName.Equals(Counters.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var newElement = ReplaceVariableInElement(Counters, remainingPath, newVariable);
                if (!Object.ReferenceEquals(newElement, Counters))
                {
                    Counters = (S7Counters)newElement;
                    replaced = true;
                }
            }

            if (replaced)
            {
                _logger?.LogDebug("Variable '{FullPath}' updated in data store. Rebuilding cache.", fullPath);
                BuildCache();
            }
            else
            {
                _logger?.LogDebug("Variable '{FullPath}' not found or no change made during update attempt in data store.", fullPath);
            }

            return replaced;
        }
    }

    private (IReadOnlyList<T> List, bool Replaced) ReplaceVariableInList<T>(IReadOnlyList<T> list, string path, IS7Variable newVariable) where T : class, IUaNode
    {
        var mutableList = list.ToList();
        for (int i = 0; i < mutableList.Count; i++)
        {
            var currentElement = mutableList[i];
            if (currentElement.DisplayName is not null && path.StartsWith(currentElement.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                string remainingPath = (path.Length == currentElement.DisplayName.Length)
                    ? ""
                    : path[currentElement.DisplayName.Length..].TrimStart('.');

                var newElement = ReplaceVariableInElement(currentElement, remainingPath, newVariable);

                if (!Object.ReferenceEquals(currentElement, newElement))
                {
                    mutableList[i] = (T)newElement;
                    return (mutableList.AsReadOnly(), true);
                }
            }
        }
        return (list, false);
    }

    private IUaNode ReplaceVariableInElement(IUaNode element, string path, IS7Variable newVariable)
    {
        if (string.IsNullOrEmpty(path))
        {
            return element is IS7Variable ? newVariable : element;
        }

        var pathSegments = path.Split('.');
        var nextSegment = pathSegments[0];
        var remainingPath = pathSegments.Length > 1 ? string.Join(".", pathSegments.Skip(1)) : "";

        switch (element)
        {
            case S7Variable variable when variable.S7Type == S7DataType.STRUCT:
            {
                (var newMembers, bool replaced) = ReplaceVariableInList(variable.StructMembers, path, newVariable);
                if (replaced) return variable with { StructMembers = newMembers };
                break;
            }

            case S7StructureElement s7Element:
            {
                (var newVars, bool replaced) = ReplaceVariableInList(s7Element.Variables, path, newVariable);
                if (replaced) return s7Element with { Variables = newVars };
                break;
            }

            case S7DataBlockInstance idb:
                if (idb.Inputs?.DisplayName == nextSegment)
                {
                    var newSection = ReplaceVariableInElement(idb.Inputs, remainingPath, newVariable);
                    if (!Object.ReferenceEquals(newSection, idb.Inputs)) return idb with { Inputs = (S7InstanceDbSection)newSection };
                }
                else if (idb.Outputs?.DisplayName == nextSegment)
                {
                    var newSection = ReplaceVariableInElement(idb.Outputs, remainingPath, newVariable);
                    if (!Object.ReferenceEquals(newSection, idb.Outputs)) return idb with { Outputs = (S7InstanceDbSection)newSection };
                }
                else if (idb.Static?.DisplayName == nextSegment)
                {
                    var newSection = ReplaceVariableInElement(idb.Static, remainingPath, newVariable);
                    if (!Object.ReferenceEquals(newSection, idb.Static)) return idb with { Static = (S7InstanceDbSection)newSection };
                }
                break;

            case S7InstanceDbSection section:
            {
                (var newVars, bool varsReplaced) = ReplaceVariableInList(section.Variables, path, newVariable);
                if (varsReplaced) return section with { Variables = newVars };

                (var newNested, bool nestedReplaced) = ReplaceVariableInList(section.NestedInstances, path, newVariable);
                if (nestedReplaced) return section with { NestedInstances = newNested };
                break;
            }
        }
        return element;
    }

    private void AddVariablesToCacheRecursively(IUaNode element, string? currentPath)
    {
        string? elementPath = element switch
        {
            IS7Variable v => v.FullPath,
            IS7DataBlockInstance i => i.FullPath,
            IS7StructureElement s => s.FullPath,
            _ => null
        } ?? (string.IsNullOrEmpty(currentPath) ? element.DisplayName : $"{currentPath}.{element.DisplayName}");

        if (elementPath is null) return;

        switch (element)
        {
            case S7Variable variable:
                _variableCacheByPath[elementPath] = variable;
                foreach (var member in variable.StructMembers)
                {
                    AddVariablesToCacheRecursively(member, elementPath);
                }
                break;

            case S7StructureElement structureElement:
                foreach (var v in structureElement.Variables)
                {
                    AddVariablesToCacheRecursively(v, elementPath);
                }
                break;

            case S7DataBlockInstance instanceDb:
                if (instanceDb.Inputs is not null) AddVariablesToCacheRecursively(instanceDb.Inputs, elementPath);
                if (instanceDb.Outputs is not null) AddVariablesToCacheRecursively(instanceDb.Outputs, elementPath);
                if (instanceDb.InOuts is not null) AddVariablesToCacheRecursively(instanceDb.InOuts, elementPath);
                if (instanceDb.Static is not null) AddVariablesToCacheRecursively(instanceDb.Static, elementPath);
                foreach (var nested in instanceDb.NestedInstances)
                {
                    AddVariablesToCacheRecursively(nested, elementPath);
                }
                break;

            case S7InstanceDbSection section:
                foreach (var v in section.Variables)
                {
                    AddVariablesToCacheRecursively(v, elementPath);
                }
                foreach (var nested in section.NestedInstances)
                {
                    AddVariablesToCacheRecursively(nested, elementPath);
                }
                break;
        }
    }

    private (IReadOnlyList<T> List, bool Added) AddVariableToList<T>(IReadOnlyList<T> list, string path, IS7Variable newVariable) where T : class, IUaNode
    {
        var mutableList = list.ToList();
        for (int i = 0; i < mutableList.Count; i++)
        {
            var currentElement = mutableList[i];
            if (currentElement.DisplayName is not null && path.StartsWith(currentElement.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                string remainingPath = (path.Length == currentElement.DisplayName.Length)
                    ? ""
                    : path[currentElement.DisplayName.Length..].TrimStart('.');

                var (newElement, added) = AddVariableToElement(currentElement, remainingPath, newVariable);

                if (added)
                {
                    mutableList[i] = (T)newElement;
                    return (mutableList.AsReadOnly(), true);
                }
            }
        }
        return (list, false);
    }

    private (IUaNode Element, bool Added) AddVariableToElement(IUaNode element, string path, IS7Variable newVariable)
    {
        var pathSegments = path.Split('.');
        var nextSegment = pathSegments[0];

        // Base case: The path points directly to the new variable, so 'element' is its parent.
        if (pathSegments.Length == 1)
        {
            switch (element)
            {
                case S7Variable parentVar when parentVar.S7Type == S7DataType.STRUCT:
                    var newMembers = parentVar.StructMembers.ToList();
                    newMembers.Add(newVariable);
                    return (parentVar with { StructMembers = newMembers.AsReadOnly() }, true);

                case S7StructureElement parentStruct:
                    var newVars = parentStruct.Variables.ToList();
                    newVars.Add(newVariable);
                    return (parentStruct with { Variables = newVars.AsReadOnly() }, true);

                case S7InstanceDbSection parentSection:
                    var newSectionVars = parentSection.Variables.ToList();
                    newSectionVars.Add(newVariable);
                    return (parentSection with { Variables = newSectionVars.AsReadOnly() }, true);

                default:
                    // This element type cannot contain variables.
                    return (element, false);
            }
        }

        // Recursive step: Traverse deeper into the hierarchy.
        var remainingPath = string.Join(".", pathSegments.Skip(1));

        switch (element)
        {
            case S7Variable variable when variable.S7Type == S7DataType.STRUCT:
            {
                (var newMembers, bool added) = AddVariableToList(variable.StructMembers, path, newVariable);
                return added ? (variable with { StructMembers = newMembers }, true) : (element, false);
            }

            case S7DataBlockInstance idb:
            {
                IUaNode? sectionToSearch = null;
                if (idb.Inputs?.DisplayName == nextSegment) sectionToSearch = idb.Inputs;
                else if (idb.Outputs?.DisplayName == nextSegment) sectionToSearch = idb.Outputs;
                else if (idb.InOuts?.DisplayName == nextSegment) sectionToSearch = idb.InOuts;
                else if (idb.Static?.DisplayName == nextSegment) sectionToSearch = idb.Static;

                if (sectionToSearch is not null)
                {
                    var (newSection, added) = AddVariableToElement(sectionToSearch, remainingPath, newVariable);
                    if (added)
                    {
                        var newIdb = idb;
                        if (idb.Inputs?.DisplayName == nextSegment) newIdb = newIdb with { Inputs = (S7InstanceDbSection)newSection };
                        else if (idb.Outputs?.DisplayName == nextSegment) newIdb = newIdb with { Outputs = (S7InstanceDbSection)newSection };
                        else if (idb.InOuts?.DisplayName == nextSegment) newIdb = newIdb with { InOuts = (S7InstanceDbSection)newSection };
                        else if (idb.Static?.DisplayName == nextSegment) newIdb = newIdb with { Static = (S7InstanceDbSection)newSection };
                        return (newIdb, true);
                    }
                }
                break;
            }

            case S7InstanceDbSection section:
            {
                (var newVars, bool varsAdded) = AddVariableToList(section.Variables, path, newVariable);
                if (varsAdded) return (section with { Variables = newVars }, true);

                (var newNested, bool nestedAdded) = AddVariableToList(section.NestedInstances, path, newVariable);
                if (nestedAdded) return (section with { NestedInstances = newNested }, true);
                break;
            }
        }

        return (element, false);
    }
}