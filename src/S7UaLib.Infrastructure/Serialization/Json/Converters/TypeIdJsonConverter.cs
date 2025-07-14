using System.Text.Json;
using System.Text.Json.Serialization;

namespace S7UaLib.Infrastructure.Serialization.Json.Converters;

internal class TypeJsonConverter : JsonConverter<Type>
{
    /// <summary>
    /// Reads a JSON string representing a type name and converts it to a <see cref="Type"/> object.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> instance used to read the JSON data.</param>
    /// <param name="typeToConvert">The type to convert, which is not used in this implementation.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use during deserialization, which is not used in this implementation.</param>
    /// <returns>A <see cref="Type"/> object corresponding to the type name in the JSON string,  or <see langword="null"/> if the
    /// JSON string is empty or <see langword="null"/>.</returns>
    public override Type? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? typeString = reader.GetString();
        return !string.IsNullOrEmpty(typeString) ? Type.GetType(typeString) : null;
    }

    /// <summary>
    /// Writes the assembly-qualified name of the specified <see cref="Type"/> to the provided <see
    /// cref="Utf8JsonWriter"/>.
    /// </summary>
    /// <remarks>This method writes the assembly-qualified name of the <paramref name="value"/> to the JSON
    /// output as a string. If <paramref name="value"/> is <see langword="null"/>, the method writes a <see
    /// langword="null"/> value to the JSON output.</remarks>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> to which the value will be written. Cannot be <see langword="null"/>.</param>
    /// <param name="value">The <see cref="Type"/> whose assembly-qualified name will be written. If <see langword="null"/>, no value is
    /// written.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use during serialization. This parameter is not used in this
    /// implementation.</param>
    public override void Write(Utf8JsonWriter writer, Type value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.AssemblyQualifiedName);
    }
}