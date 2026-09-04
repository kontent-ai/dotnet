using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Conversion;
using System.Text.Json;

namespace Kontent.Ai.Management.Tests.Conversion;

// String-based conveniences over the converter's element/reader primitives — fixture-driven tests read better with
// JSON strings, but production only ever needs the element paths. WriteEnvelopes serializes the elements the way the
// upsert request does, so what the tests parse is what goes on the wire.
internal static class EnvelopeConverterTestExtensions
{
    private static readonly JsonSerializerOptions WireOptions = RefitSettingsProvider.CreateDefaultJsonSerializerOptions();

    public static string WriteEnvelopes<T>(this ContentItemEnvelopeConverter converter, T item) where T : IElementsModel
        => JsonSerializer.Serialize(converter.ToElements(item), WireOptions);

    public static T ReadEnvelopes<T>(this ContentItemEnvelopeConverter converter, string envelopesJson) where T : IElementsModel
    {
        using var doc = JsonDocument.Parse(envelopesJson);
        return (T)converter.ReadEnvelopes(doc.RootElement, typeof(T));
    }
}
