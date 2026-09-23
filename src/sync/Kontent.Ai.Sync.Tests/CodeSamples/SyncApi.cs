using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Sync.Tests.CodeSamples;

/// <summary>
/// Source of the samples in https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/sync-api-v2
/// </summary>
public sealed class SyncApi : IDisposable
{
    private const string NextToken = "next-sync-token";

    private readonly MockHttpMessageHandler _http = new();

    public SyncApi() => _http.Fallback.Respond(_ =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"items":[],"types":[],"languages":[],"taxonomies":[]}""", Encoding.UTF8, "application/json"),
        };
        response.Headers.TryAddWithoutValidation("X-Continuation", NextToken);
        return response;
    });

    public void Dispose() => _http.Dispose();

    private SyncClient CreateClient() => SyncClient.Create(
        new SyncOptions { EnvironmentId = Guid.NewGuid().ToString() },
        sync => sync.HttpClient.ConfigurePrimaryHttpMessageHandler(() => _http));

    [Fact]
    public async Task InitializeSync()
    {
        using var client = CreateClient();

        // DocSection: sync_api_v2_initialize_sync
        // Initializes Sync API v2 and gets the initial sync token.
        var result = await client.InitializeSyncAsync();
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error?.Message ?? "Sync init failed.");
        }

        string syncToken = result.SyncToken;
        // EndDocSection

        Assert.Equal(NextToken, syncToken);
    }

    [Fact]
    public async Task SynchronizeChanges()
    {
        using var client = CreateClient();

        // DocSection: sync_api_v2_synchronize_changes
        // Gets a page of changes since the last stored sync token.
        var result = await client.GetDeltaAsync("your-sync-token");
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(result.Error?.Message ?? "Sync failed.");
        }

        var delta = result.Value;
        var syncItems = delta.Items;
        var syncTypes = delta.Types;
        var syncTaxonomies = delta.Taxonomies;
        var syncLanguages = delta.Languages;

        // Persist this token and use it in the next synchronization call.
        string nextSyncToken = result.SyncToken;
        // EndDocSection

        Assert.Equal(NextToken, nextSyncToken);
        Assert.All(new object[] { syncItems, syncTypes, syncTaxonomies, syncLanguages }, Assert.NotNull);
    }
}
