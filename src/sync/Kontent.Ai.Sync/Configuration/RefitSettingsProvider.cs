using System.Text.Json;

namespace Kontent.Ai.Sync.Configuration;

internal static class RefitSettingsProvider
{
    public static RefitSettings CreateDefaultSettings() => new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(CreateDefaultJsonSerializerOptions()),
    };

    // Every wire model names its own properties and ChangeType names its own converter, so a naming policy
    // would apply to nothing. Case-insensitive matching is not in that category and the siblings both keep
    // it: it decides how the wire name is matched against JsonPropertyName rather than being overridden by
    // it. Without it a recased `Items` would bind nothing, and the delta lists carry a `[]` default rather
    // than being required - so a walk would report no changes instead of failing.
    //
    // A required member only guarantees the property was present; an explicit null still lands in it, which
    // is what RespectNullableAnnotations refuses.
    public static JsonSerializerOptions CreateDefaultJsonSerializerOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        RespectNullableAnnotations = true,
    };
}
