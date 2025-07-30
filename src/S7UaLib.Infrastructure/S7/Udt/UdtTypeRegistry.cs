using S7UaLib.Core.S7.Converters;
using S7UaLib.Core.S7.Udt;
using System.Collections.Concurrent;

namespace S7UaLib.Infrastructure.S7.Udt;

/// <summary>
/// Thread-safe implementation of the UDT type registry.
/// </summary>
internal class UdtTypeRegistry : IUdtTypeRegistry
{
    private readonly ConcurrentDictionary<string, UdtDefinition> _udtDefinitions = new();
    private readonly ConcurrentDictionary<string, IS7TypeConverter> _customConverters = new();

    #region UDT Definition Management

    public void RegisterDiscoveredUdt(UdtDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("UDT definition must have a valid name.", nameof(definition));

        _udtDefinitions.AddOrUpdate(definition.Name, definition, (_, _) => definition);
    }

    public UdtDefinition? GetUdtDefinition(string udtName)
    {
        if (string.IsNullOrWhiteSpace(udtName))
            return null;

        return _udtDefinitions.TryGetValue(udtName, out var definition) ? definition : null;
    }

    public IReadOnlyDictionary<string, UdtDefinition> GetAllUdtDefinitions()
    {
        return _udtDefinitions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public bool RemoveUdtDefinition(string udtName)
    {
        if (string.IsNullOrWhiteSpace(udtName))
            return false;

        return _udtDefinitions.TryRemove(udtName, out _);
    }

    public void ClearUdtDefinitions()
    {
        _udtDefinitions.Clear();
    }

    #endregion

    #region Custom Converter Management

    public void RegisterCustomConverter(string udtName, IS7TypeConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        if (string.IsNullOrWhiteSpace(udtName))
            throw new ArgumentException("UDT name cannot be null or whitespace.", nameof(udtName));

        _customConverters.AddOrUpdate(udtName, converter, (_, _) => converter);
    }

    public IS7TypeConverter? GetCustomConverter(string udtName)
    {
        if (string.IsNullOrWhiteSpace(udtName))
            return null;

        return _customConverters.TryGetValue(udtName, out var converter) ? converter : null;
    }

    public IReadOnlyDictionary<string, IS7TypeConverter> GetAllCustomConverters()
    {
        return _customConverters.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public bool RemoveCustomConverter(string udtName)
    {
        if (string.IsNullOrWhiteSpace(udtName))
            return false;

        return _customConverters.TryRemove(udtName, out _);
    }

    public void ClearCustomConverters()
    {
        _customConverters.Clear();
    }

    #endregion

    #region Utility Methods

    public bool HasUdtDefinition(string udtName)
    {
        return !string.IsNullOrWhiteSpace(udtName) && _udtDefinitions.ContainsKey(udtName);
    }

    public bool HasCustomConverter(string udtName)
    {
        return !string.IsNullOrWhiteSpace(udtName) && _customConverters.ContainsKey(udtName);
    }

    #endregion
}