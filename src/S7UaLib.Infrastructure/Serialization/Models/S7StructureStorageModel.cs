using S7UaLib.Core.S7.Structure;

namespace S7UaLib.Infrastructure.Serialization.Models;

/// <summary>
/// Represents a pure data model for storing the discovered S7 structure.
/// This class is used for serialization and deserialization purposes.
/// </summary>
internal record S7StructureStorageModel
{
    public IReadOnlyList<IS7DataBlockGlobal> DataBlocksGlobal { get; init; } = [];
    public IReadOnlyList<IS7DataBlockInstance> DataBlocksInstance { get; init; } = [];
    public IS7Inputs? Inputs { get; init; }
    public IS7Outputs? Outputs { get; init; }
    public IS7Memory? Memory { get; init; }
    public IS7Timers? Timers { get; init; }
    public IS7Counters? Counters { get; init; }
}