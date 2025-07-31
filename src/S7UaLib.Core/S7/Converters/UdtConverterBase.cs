using S7UaLib.Core.S7.Structure;

namespace S7UaLib.Core.S7.Converters;

/// <summary>
/// Base class for custom UDT converters, providing common functionality.
/// </summary>
/// <typeparam name="T">The user-defined C# type that represents the UDT.</typeparam>
public abstract class UdtConverterBase<T> : IUdtConverter<T> where T : class
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UdtConverterBase{T}"/> class.
    /// </summary>
    /// <param name="udtTypeName">The UDT type name as defined in the PLC.</param>
    protected UdtConverterBase(string udtTypeName)
    {
        UdtTypeName = udtTypeName ?? throw new ArgumentNullException(nameof(udtTypeName));
        UdtType = typeof(T);
        TargetType = typeof(T);
    }

    /// <inheritdoc />
    public string UdtTypeName { get; }

    /// <inheritdoc />
    public Type UdtType { get; }

    /// <inheritdoc />
    public Type TargetType { get; }

    /// <inheritdoc />
    public abstract T ConvertFromUdtMembers(IReadOnlyList<IS7Variable> structMembers);

    /// <inheritdoc />
    public abstract IReadOnlyList<IS7Variable> ConvertToUdtMembers(T udtInstance, IReadOnlyList<IS7Variable> structMemberTemplate);

    /// <inheritdoc />
    public virtual object? ConvertFromOpc(object? opcValue)
    {
        // This method is called by the generic converter system
        // For UDT converters, the actual conversion happens in ConvertFromUdtMembers
        // This method should not be called directly for UDT types
        if (opcValue is IReadOnlyList<IS7Variable> structMembers)
        {
            return ConvertFromUdtMembers(structMembers);
        }

        throw new InvalidOperationException($"UDT converter for type '{UdtTypeName}' cannot convert from OPC value of type '{opcValue?.GetType().Name}'. Use ConvertFromUdtMembers instead.");
    }

    /// <inheritdoc />
    public virtual object? ConvertToOpc(object? value)
    {
        // This method is called by the generic converter system for writing
        // For UDT converters, this should not be used directly
        throw new InvalidOperationException($"UDT converter for type '{UdtTypeName}' cannot convert to OPC directly. Use ConvertToUdtMembers instead.");
    }

    /// <inheritdoc />
    object IUdtConverterBase.ConvertFromUdtMembers(IReadOnlyList<IS7Variable> structMembers)
    {
        return ConvertFromUdtMembers(structMembers);
    }

    /// <inheritdoc />
    IReadOnlyList<IS7Variable> IUdtConverterBase.ConvertToUdtMembers(object udtInstance, IReadOnlyList<IS7Variable> structMemberTemplate)
    {
        if (udtInstance is T typedInstance)
        {
            return ConvertToUdtMembers(typedInstance, structMemberTemplate);
        }

        throw new ArgumentException($"Expected instance of type '{typeof(T).Name}' but received '{udtInstance?.GetType().Name}'.", nameof(udtInstance));
    }

    /// <summary>
    /// Helper method to find a structure member by name.
    /// </summary>
    /// <param name="structMembers">The structure members to search.</param>
    /// <param name="memberName">The name of the member to find.</param>
    /// <returns>The structure member if found, otherwise null.</returns>
    protected static IS7Variable? FindMember(IReadOnlyList<IS7Variable> structMembers, string memberName)
    {
        return structMembers.FirstOrDefault(m => m.DisplayName == memberName);
    }

    /// <summary>
    /// Helper method to get a typed value from a structure member.
    /// </summary>
    /// <typeparam name="TValue">The expected type of the value.</typeparam>
    /// <param name="member">The structure member.</param>
    /// <param name="defaultValue">The default value to return if conversion fails.</param>
    /// <returns>The typed value or the default value.</returns>
    protected static TValue GetMemberValue<TValue>(IS7Variable? member, TValue defaultValue = default!)
    {
        if (member?.Value is TValue typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }
}