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
/// Converts an Opc.Ua.StatusCode into a JSON object with code and symbol name, and vice versa.
/// </summary>
public class StatusCodeJsonConverter : JsonConverter<StatusCode>
{
    /// <summary>
    /// Reads and converts JSON data into a <see cref="StatusCode"/> object.
    /// </summary>
    /// <remarks>This method expects the JSON data to represent an object with a "Code" property. The "Code"
    /// property is case-insensitive and must contain a valid unsigned integer. If the JSON data does not start with a
    /// <see cref="JsonTokenType.StartObject"/> token or ends unexpectedly, a <see cref="JsonException"/> is
    /// thrown.</remarks>
    /// <param name="reader">The <see cref="Utf8JsonReader"/> instance used to read the JSON data.</param>
    /// <param name="typeToConvert">The type of the object to convert. This parameter is required but not used in this implementation.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> that provide serialization options. This parameter is required but not
    /// used in this implementation.</param>
    /// <returns>A <see cref="StatusCode"/> object deserialized from the JSON data.</returns>
    /// <exception cref="JsonException">Thrown if the JSON data is not in the expected format, such as missing the "Code" property or encountering an
    /// unexpected token.</exception>
    public override StatusCode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token for StatusCode.");
        }

        uint code = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new StatusCode(code);
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string? propertyName = reader.GetString();
                reader.Read(); // Move to property value

                if (string.Equals(propertyName, "Code", StringComparison.OrdinalIgnoreCase))
                {
                    code = reader.GetUInt32();
                }
            }
        }
        throw new JsonException("Unexpected end of JSON while parsing StatusCode.");
    }

    /// <summary>
    /// Writes a JSON representation of the specified <see cref="StatusCode"/> value using the provided <see
    /// cref="Utf8JsonWriter"/>.
    /// </summary>
    /// <remarks>The method writes a JSON object containing two properties:  <list type="bullet">
    /// <item><term>Code</term><description>The numeric code of the <see cref="StatusCode"/>.</description></item>
    /// <item><term>Symbol</term><description>The string representation of the <see
    /// cref="StatusCode"/>.</description></item> </list> Ensure that the <paramref name="writer"/> is properly
    /// initialized and open before calling this method.</remarks>
    /// <param name="writer">The <see cref="Utf8JsonWriter"/> used to write the JSON output. Cannot be <see langword="null"/>.</param>
    /// <param name="value">The <see cref="StatusCode"/> instance to serialize. Must contain valid data.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions"/> that influence the serialization process. Can be <see langword="null"/>.</param>
    public override void Write(Utf8JsonWriter writer, StatusCode value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Code", value.Code);
        writer.WriteString("Symbol", value.ToString()); // z.B. "Good", "BadWaitingForInitialData"
        writer.WriteEndObject();
    }
}