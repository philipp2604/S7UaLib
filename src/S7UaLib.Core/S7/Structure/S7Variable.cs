﻿using S7UaLib.Core.Enums;
using S7UaLib.Core.S7.Udt;
using System.Text.Json.Serialization;

namespace S7UaLib.Core.S7.Structure;

/// <summary>
/// Represents a single S7 variable, holding its metadata, value, and status.
/// This record can represent a simple type (like INT or REAL) or a complex struct (UDT),
/// in which case it will contain a list of its members.
/// </summary>
public record S7Variable : IS7Variable
{
    #region Public Properties

    /// <inheritdoc cref="IUaNode.NodeId" />
    public string? NodeId { get; init; }

    /// <inheritdoc cref="IUaNode.DisplayName" />
    public string? DisplayName { get; init; }

    /// <inheritdoc/>
    public string? FullPath { get; init; }

    /// <inheritdoc cref="IS7Variable.Variables" />
    [JsonIgnore]
    public object? Value { get; init; }

    /// <inheritdoc cref="IS7Variable.RawOpcValue" />
    [JsonIgnore]
    public object? RawOpcValue { get; init; }

    /// <inheritdoc cref="IS7Variable.S7Type" />
    public S7DataType S7Type { get; init; }

    /// <inheritdoc cref="IS7Variable.UdtTypeName" />
    public string? UdtTypeName { get; init; }

    /// <inheritdoc cref="IS7Variable.SystemType" />
    public Type? SystemType { get; init; }

    /// <inheritdoc cref="IS7Variable.StatusCode" />
    [JsonIgnore]
    public StatusCode StatusCode { get; init; }

    /// <inheritdoc cref="IS7Variable.StructMembers" />
    public IReadOnlyList<IS7Variable> StructMembers { get; init; } = [];

    /// <inheritdoc cref="IS7Variable.Variables" />
    public bool IsSubscribed { get; init; } = false;

    /// <inheritdoc cref="IS7Variable.SamplingInterval" />
    public uint SamplingInterval { get; init; } = 0;

    /// <inheritdoc cref="IS7Variable.UdtDefinition" />
    public UdtDefinition? UdtDefinition { get; init; }

    #endregion Public Properties
}