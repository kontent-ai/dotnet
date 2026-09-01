using System.Text.Json;
using AwesomeAssertions;
using Kontent.Ai.Sync.Configuration;
using Kontent.Ai.Sync.Models;

namespace Kontent.Ai.Sync.Tests.Configuration;

public class RefitSettingsProviderTests
{
    [Fact]
    public void DefaultJsonSerializerOptions_HasSyncShape()
    {
        var options = RefitSettingsProvider.CreateDefaultJsonSerializerOptions();

        options.PropertyNameCaseInsensitive.Should().BeTrue();
        options.RespectNullableAnnotations.Should().BeTrue();
        options.PropertyNamingPolicy.Should().BeNull();
    }

    [Fact]
    public void Deserialize_DeltaWithRecasedPropertyNames_StillBinds()
    {
        // The delta lists default to [] rather than being required, so a casing change the SDK could not
        // match would report an empty delta instead of throwing - a sync that silently sees no changes.
        const string json = """
        {
          "Items": [
            {
              "change_type": "changed",
              "timestamp": "2026-01-01T00:00:00Z",
              "data": { "system": {
                  "id": "00000000-0000-0000-0000-000000000001",
                  "collection": "default",
                  "name": "Article",
                  "codename": "article",
                  "language": "en-US",
                  "type": "article",
                  "last_modified": "2026-01-01T00:00:00Z" } }
            }
          ]
        }
        """;

        var delta = JsonSerializer.Deserialize<SyncDeltaResponse>(
            json, RefitSettingsProvider.CreateDefaultJsonSerializerOptions());

        delta.Should().NotBeNull();
        delta.Items.Should().ContainSingle();
        delta.Items[0].Data.System.Codename.Should().Be("article");
    }
}
