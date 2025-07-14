namespace S7UaLib.Core.S7.Converters;

/// <summary>
/// Defines a contract for type converters responsible for transforming values
/// between raw OPC UA server formats and user-friendly .NET types.
/// </summary>
public interface IS7TypeConverter
{
    #region Public Properties

    /// <summary>
    /// Gets the target .NET type that this converter produces from OPC values
    /// or consumes from user values.
    /// </summary>
    Type TargetType { get; }

    #endregion Public Properties

    #region Public Methods

    /// <summary>
    /// Converts a value received from the OPC server into the target .NET type.
    /// </summary>
    /// <param name="opcValue">The value received from the server (e.g., a byte array).</param>
    /// <returns>The converted, user-friendly .NET object (e.g., a string).</returns>
    object? ConvertFromOpc(object? opcValue);

    /// <summary>
    /// Converts a user-friendly .NET value into the format expected by the OPC server.
    /// </summary>
    /// <param name="userValue">The user-friendly .NET object (e.g., a string).</param>
    /// <returns>The value formatted to be sent to the server (e.g., a byte array).</returns>
    object? ConvertToOpc(object? userValue);

    #endregion Public Methods
}