using Microsoft.Extensions.Logging;
using S7UaLib.DataStore.Contracts;
using S7UaLib.S7.Structure;
using S7UaLib.S7.Structure.Contracts;
using S7UaLib.S7.Types;
using S7UaLib.UA;
using System.Diagnostics.CodeAnalysis;

namespace S7UaLib.DataStore;

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
    }

    private readonly Dictionary<string, IS7Variable> _variableCacheByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger? _logger;

    /// <inheritdoc cref="IS7DataStore.DataBlocksGlobal"/>
    public IReadOnlyList<S7DataBlockGlobal> DataBlocksGlobal { get; set; } = [];

    /// <inheritdoc cref="IS7DataStore.DataBlocksInstance"/>
    public IReadOnlyList<S7DataBlockInstance> DataBlocksInstance { get; set; } = [];

    /// <inheritdoc cref="IS7DataStore.Inputs"/>
    public S7Inputs? Inputs { get; set; }

    /// ´<inheritdoc cref="IS7DataStore.Outputs"/>
    public S7Outputs? Outputs { get; set; }

    /// <inheritdoc cref="IS7DataStore.Memory"/>
    public S7Memory? Memory { get; set; }

    /// <inheritdoc cref="IS7DataStore.Timers"/>
    public S7Timers? Timers { get; set; }

    /// <inheritdoc cref="IS7DataStore.Counters"/>
    public S7Counters? Counters { get; set; }

    /// <inheritdoc cref="IS7DataStore.TryGetVariableByPath(string, out IS7Variable)"/>
    public bool TryGetVariableByPath(string fullPath, [MaybeNullWhen(false)] out IS7Variable variable)
    {
        return _variableCacheByPath.TryGetValue(fullPath, out variable);
    }

    /// <inheritdoc cref="IS7DataStore.GetAllVariables"/>
    public IReadOnlyDictionary<string, IS7Variable> GetAllVariables() =>
        new Dictionary<string, IS7Variable>(_variableCacheByPath, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc cref="IS7DataStore.BuildCache"/>
    public void BuildCache()
    {
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
    }

    /// <inheritdoc cref="IS7DataStore.UpdateVariable(string, IS7Variable)"/>
    public bool UpdateVariable(string fullPath, IS7Variable newVariable)
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
            BuildCache();
        }

        return replaced;
    }

    private (IReadOnlyList<T> List, bool Replaced) ReplaceVariableInList<T>(IReadOnlyList<T> list, string path, IS7Variable newVariable) where T : class, IUaElement
    {
        var mutableList = list.ToList();
        for (int i = 0; i < mutableList.Count; i++)
        {
            var currentElement = mutableList[i];
            if (currentElement.DisplayName is not null && path.StartsWith(currentElement.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                string remainingPath = (path.Length == currentElement.DisplayName.Length)
                    ? ""
                    : path.Substring(currentElement.DisplayName.Length).TrimStart('.');

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

    private IUaElement ReplaceVariableInElement(IUaElement element, string path, IS7Variable newVariable)
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

    private void AddVariablesToCacheRecursively(IUaElement element, string? currentPath)
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
}