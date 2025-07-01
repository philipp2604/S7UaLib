using Opc.Ua;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace S7UaLib.Json.Converters;

/// <summary>
/// Converts an Opc.Ua.NodeId into a string of type "ns=3;s=MyNode" and vice versa.
/// </summary>
internal class NodeIdJsonConverter : JsonConverter<NodeId>
{
    /// <summary>
    /// Reads a JSON string and converts it to a <see cref="NodeId"/> object.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> instance used to read the JSON data.</param>
    /// <param name="typeToConvert">The type to convert the JSON data to. This parameter is required by the base method but is not used in this
    /// implementation.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> to use during deserialization. This parameter is required by the base
    /// method but is not used in this implementation.</param>
    /// <returns>A <see cref="NodeId"/> object parsed from the JSON string, or <see langword="null"/> if the JSON string is <see
    /// langword="null"/> or empty.</returns>
    public override NodeId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? nodeIdString = reader.GetString();
        return !string.IsNullOrEmpty(nodeIdString) ? NodeId.Parse(nodeIdString) : null;
    }

    /// <summary>
    /// Writes the specified <see cref="NodeId"/> value as a JSON string using the provided <see
    /// cref="Utf8JsonWriter"/>.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> used to write the JSON output. Cannot be <see langword="null"/>.</param>
    /// <param name="value">The <see cref="NodeId"/> to write. If <see langword="null"/>, a JSON null value will not be written.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> that influence serialization behavior. This parameter is not used
    /// directly by this method.</param>
    public override void Write(Utf8JsonWriter writer, NodeId value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.ToString());
    }
}