using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync.Models;

// The API lists a new change type as a non-breaking change, so an unrecognized name must not fail the page it
// arrives in. A number is not a change type at all, and is refused rather than read as an undefined value.
internal sealed class ChangeTypeConverter : JsonConverter<ChangeType>
{
    public override ChangeType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a change type name, found {reader.TokenType}.");
        }

        if (reader.ValueTextEquals("changed"u8))
        {
            return ChangeType.Changed;
        }

        return reader.ValueTextEquals("deleted"u8) ? ChangeType.Deleted : ChangeType.Unknown;
    }

    public override void Write(Utf8JsonWriter writer, ChangeType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            ChangeType.Changed => "changed",
            ChangeType.Deleted => "deleted",
            _ => "unknown",
        });
}
