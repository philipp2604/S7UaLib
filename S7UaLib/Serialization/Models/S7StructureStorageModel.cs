using S7UaLib.S7.Structure;

namespace S7UaLib.Serialization.Models;

/// <summary>
/// Represents a pure data model for storing the discovered S7 structure.
/// This class is used for serialization and deserialization purposes.
/// </summary>
internal record S7StructureModel
{
    public IReadOnlyList<S7DataBlockGlobal> DataBlocksGlobal { get; init; } = [];
    public IReadOnlyList<S7DataBlockInstance> DataBlocksInstance { get; init; } = [];
    public S7Inputs? Inputs { get; init; }
    public S7Outputs? Outputs { get; init; }
    public S7Memory? Memory { get; init; }
    public S7Timers? Timers { get; init; }
    public S7Counters? Counters { get; init; }
}