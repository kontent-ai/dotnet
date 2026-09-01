using System.Text.Json;

namespace Kontent.Ai.Sync.Configuration;

internal static class RefitSettingsProvider
{
    public static RefitSettings CreateDefaultSettings() => new()
    {
        ContentSerializer = new SystemTextJsonContentSerializer(CreateDefaultJsonSerializerOptions()),
    };

    // Every wire model names its own properties and ChangeType names its own converter, so a naming policy
    // would apply to nothing. A required member only guarantees the property was present; an explicit null
    // still lands in it, which is what RespectNullableAnnotations refuses.
    public static JsonSerializerOptions CreateDefaultJsonSerializerOptions() => new()
    {
        RespectNullableAnnotations = true,
    };
}
