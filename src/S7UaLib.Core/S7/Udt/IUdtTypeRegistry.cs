using S7UaLib.Core.S7.Converters;

namespace S7UaLib.Core.S7.Udt;

/// <summary>
/// Registry for managing discovered UDT definitions and user-registered custom type converters.
/// </summary>
public interface IUdtTypeRegistry
{
    #region UDT Definition Management

    /// <summary>
    /// Registers a UDT definition that was discovered from the PLC.
    /// </summary>
    /// <param name="definition">The discovered UDT definition.</param>
    void RegisterDiscoveredUdt(UdtDefinition definition);

    /// <summary>
    /// Gets a previously discovered UDT definition by name.
    /// </summary>
    /// <param name="udtName">The name of the UDT.</param>
    /// <returns>The UDT definition if found; otherwise null.</returns>
    UdtDefinition? GetUdtDefinition(string udtName);

    /// <summary>
    /// Gets all currently registered UDT definitions.
    /// </summary>
    /// <returns>A read-only dictionary of UDT definitions keyed by name.</returns>
    IReadOnlyDictionary<string, UdtDefinition> GetAllUdtDefinitions();

    /// <summary>
    /// Removes a UDT definition from the registry.
    /// </summary>
    /// <param name="udtName">The name of the UDT to remove.</param>
    /// <returns>True if the UDT was removed; false if it wasn't found.</returns>
    bool RemoveUdtDefinition(string udtName);

    /// <summary>
    /// Clears all registered UDT definitions.
    /// </summary>
    void ClearUdtDefinitions();

    #endregion

    #region Custom Converter Management

    /// <summary>
    /// Registers a custom converter for a specific UDT type.
    /// This converter will be used instead of the generic UDT converter.
    /// </summary>
    /// <param name="udtName">The name of the UDT.</param>
    /// <param name="converter">The custom converter to use.</param>
    void RegisterCustomConverter(string udtName, IS7TypeConverter converter);

    /// <summary>
    /// Gets a custom converter for a specific UDT type.
    /// </summary>
    /// <param name="udtName">The name of the UDT.</param>
    /// <returns>The custom converter if registered; otherwise null.</returns>
    IS7TypeConverter? GetCustomConverter(string udtName);

    /// <summary>
    /// Gets all currently registered custom converters.
    /// </summary>
    /// <returns>A read-only dictionary of custom converters keyed by UDT name.</returns>
    IReadOnlyDictionary<string, IS7TypeConverter> GetAllCustomConverters();

    /// <summary>
    /// Removes a custom converter for a specific UDT type.
    /// </summary>
    /// <param name="udtName">The name of the UDT.</param>
    /// <returns>True if the converter was removed; false if it wasn't found.</returns>
    bool RemoveCustomConverter(string udtName);

    /// <summary>
    /// Clears all registered custom converters.
    /// </summary>
    void ClearCustomConverters();

    #endregion

    #region Utility Methods

    /// <summary>
    /// Checks if a UDT definition is registered.
    /// </summary>
    /// <param name="udtName">The name of the UDT.</param>
    /// <returns>True if the UDT definition exists; otherwise false.</returns>
    bool HasUdtDefinition(string udtName);

    /// <summary>
    /// Checks if a custom converter is registered for a UDT.
    /// </summary>
    /// <param name="udtName">The name of the UDT.</param>
    /// <returns>True if a custom converter exists; otherwise false.</returns>
    bool HasCustomConverter(string udtName);

    #endregion
}