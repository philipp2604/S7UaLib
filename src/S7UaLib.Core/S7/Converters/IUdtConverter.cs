using S7UaLib.Core.S7.Structure;

namespace S7UaLib.Core.S7.Converters;

/// <summary>
/// Non-generic base interface for UDT converters to enable runtime operations without generic type parameters.
/// </summary>
public interface IUdtConverterBase : IS7TypeConverter
{
    /// <summary>
    /// Gets the UDT type name that this converter handles.
    /// </summary>
    string UdtTypeName { get; }

    /// <summary>
    /// Gets the C# type that this converter produces.
    /// </summary>
    Type UdtType { get; }

    /// <summary>
    /// Converts UDT structure members from the PLC into a user-defined C# object.
    /// </summary>
    /// <param name="structMembers">The structure members with their values from the PLC.</param>
    /// <returns>An instance of the user-defined C# type.</returns>
    object ConvertFromUdtMembers(IReadOnlyList<IS7Variable> structMembers);

    /// <summary>
    /// Converts a user-defined C# object back into UDT structure members for writing to the PLC.
    /// </summary>
    /// <param name="udtInstance">The user-defined C# object instance.</param>
    /// <param name="structMemberTemplate">The template structure members (for NodeIds and metadata).</param>
    /// <returns>Structure members with updated values ready for writing to the PLC.</returns>
    IReadOnlyList<IS7Variable> ConvertToUdtMembers(object udtInstance, IReadOnlyList<IS7Variable> structMemberTemplate);
}

/// <summary>
/// Interface for custom UDT converters that can convert between PLC UDT data and user-defined C# types.
/// </summary>
/// <typeparam name="T">The user-defined C# type that represents the UDT.</typeparam>
public interface IUdtConverter<T> : IUdtConverterBase where T : class
{
    /// <summary>
    /// Converts UDT structure members from the PLC into a user-defined C# object.
    /// </summary>
    /// <param name="structMembers">The structure members with their values from the PLC.</param>
    /// <returns>An instance of the user-defined C# type.</returns>
    new T ConvertFromUdtMembers(IReadOnlyList<IS7Variable> structMembers);

    /// <summary>
    /// Converts a user-defined C# object back into UDT structure members for writing to the PLC.
    /// </summary>
    /// <param name="udtInstance">The user-defined C# object instance.</param>
    /// <param name="structMemberTemplate">The template structure members (for NodeIds and metadata).</param>
    /// <returns>Structure members with updated values ready for writing to the PLC.</returns>
    IReadOnlyList<IS7Variable> ConvertToUdtMembers(T udtInstance, IReadOnlyList<IS7Variable> structMemberTemplate);
}