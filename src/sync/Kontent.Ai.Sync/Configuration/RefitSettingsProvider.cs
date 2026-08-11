using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync.Configuration;

/// <summary>
/// Provides default Refit settings for the Sync API.
/// </summary>
internal static class RefitSettingsProvider
{
    /// <summary>
    /// Creates default Refit settings with System.Text.Json serialization.
    /// </summary>
    /// <returns>Configured Refit settings.</returns>
    public static RefitSettings CreateDefaultSettings()
    {
        var jsonSerializerOptions = CreateDefaultJsonSerializerOptions();

        // Only the serializer: ISyncApi sends nothing in a query string - the environment travels in the
        // path and the continuation token in a header - so the collection format and key formatter that
        // the Delivery settings carry would configure something that never happens.
        return new RefitSettings
        {
            ContentSerializer = new SystemTextJsonContentSerializer(jsonSerializerOptions),
        };
    }

    /// <summary>
    /// Creates default JSON serializer options.
    /// </summary>
    /// <returns>Configured JSON serializer options.</returns>
    public static JsonSerializerOptions CreateDefaultJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)
            }
        };
    }
}
