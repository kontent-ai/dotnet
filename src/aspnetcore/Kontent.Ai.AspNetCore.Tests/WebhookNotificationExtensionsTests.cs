using System.Net;
using System.Text.Json;
using Kontent.Ai.AspNetCore.Webhooks;
using Kontent.Ai.AspNetCore.Webhooks.Models;
using Kontent.Ai.Delivery;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.AspNetCore.Tests;

public class WebhookNotificationExtensionsTests
{
    private const string EnvironmentId = "195a50c1-f1b8-0066-9eb4-83f7246d5459";
    private const string BaseUrl = $"https://deliver.kontent.ai/{EnvironmentId}";
    private static readonly Guid AssetId = Guid.Parse("f24e4721-f081-50ef-9a47-2ebd45b0c915");

    [Theory]
    [InlineData("ContentItemPublished.json", "item_this_changes_everything", DeliveryCacheDependencies.ItemsListScope)]
    [InlineData("ContentItemWorkflowStepChanged.json", "item_solutions_imaging", DeliveryCacheDependencies.ItemsListScope)]
    [InlineData("ContentTypeChanged.json", "type_page", DeliveryCacheDependencies.TypesListScope)]
    [InlineData("TaxonomyTermCreated.json", "taxonomy_product_category", DeliveryCacheDependencies.TaxonomiesListScope)]
    [InlineData("AssetMetadataChanged.json", "asset_f24e4721-f081-50ef-9a47-2ebd45b0c915")]
    [InlineData("LanguageDeleted.json")]
    public void EveryDocumentedPayload_MapsToTheSdkKeys(string file, params string[] expected)
    {
        var keys = Load(file).GetCacheDependencyKeys();

        Assert.Equal(expected, keys);
    }

    // The documented term payload carries the term's codename and its group separately; the group is the key.
    // A group-level event carries only the group's codename, so that is the fallback.
    [Fact]
    public void Taxonomy_KeysOnTheGroup_WhetherCarriedSeparatelyOrAsTheCodename()
    {
        var term = Notification(WebhookObjectTypes.Taxonomy, WebhookActions.TermChanged, "handheld", taxonomyGroup: "product_category");
        var group = Notification(WebhookObjectTypes.Taxonomy, WebhookActions.MetadataChanged, "product_category");

        Assert.Equal("taxonomy_product_category", new[] { term }.GetCacheDependencyKeys()[0]);
        Assert.Equal("taxonomy_product_category", new[] { group }.GetCacheDependencyKeys()[0]);
    }

    [Fact]
    public void Batch_UnionsTheKeysWithoutDuplicates()
    {
        var batch = new WebhookNotification
        {
            Notifications =
            [
                Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, "hero"),
                Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, "about"),
                Notification(WebhookObjectTypes.ContentItem, WebhookActions.Unpublished, "hero"),
            ]
        };

        Assert.Equal(["item_hero", DeliveryCacheDependencies.ItemsListScope, "item_about"], batch.GetCacheDependencyKeys());
    }

    // A value Kontent.ai adds later deserializes (the vocabulary is strings, not an enum) and maps to nothing.
    [Fact]
    public void UnknownObjectType_MapsToNothing()
    {
        Assert.Empty(new[] { Notification("something_new", WebhookActions.Created, "x") }.GetCacheDependencyKeys());
    }

    [Fact]
    public void ASubsetOfTheBatch_CanBeMapped()
    {
        var models = new[]
        {
            Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, "hero"),
            Notification(WebhookObjectTypes.Language, WebhookActions.Changed, "en"),
        };

        Assert.Equal(["item_hero", DeliveryCacheDependencies.ItemsListScope], models.Where(n => n.Message.ObjectType != WebhookObjectTypes.Language).GetCacheDependencyKeys());
    }

    // An asset element carries no asset id, so the items holding the asset are only reachable through the
    // used-in lookup; that is the SDK's InvalidateAssetAsync, and it needs the client.
    [Fact]
    public async Task InvalidateAsync_InvalidatesTheKeys_AndTheItemsUsingAnAsset()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{BaseUrl}/assets/sofia_patel_jpg/used-in").Respond("application/json", UsagePage("about"));
        var manager = new RecordingCacheManager();
        var batch = new WebhookNotification
        {
            Notifications = [Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, "hero"), .. Load("AssetMetadataChanged.json").Notifications]
        };

        var invalidated = await manager.InvalidateAsync(batch, CreateClient(mock));

        Assert.True(invalidated);
        Assert.Equal(2, manager.Invalidations.Count);
        Assert.Equal(["item_hero", DeliveryCacheDependencies.ItemsListScope], manager.Invalidations[0]);
        Assert.Equal([$"asset_{AssetId}", "item_about", DeliveryCacheDependencies.ItemsListScope], manager.Invalidations[1]);
    }

    [Fact]
    public async Task InvalidateAsync_LooksUpAnAssetOnce_HoweverManyNotificationsCarryIt()
    {
        var mock = new MockHttpMessageHandler();
        var usedIn = mock.When($"{BaseUrl}/assets/sofia_patel_jpg/used-in").Respond("application/json", UsagePage("about"));
        var manager = new RecordingCacheManager();
        var asset = Load("AssetMetadataChanged.json").Notifications[0];
        var batch = new[] { asset, asset with { Message = asset.Message with { Action = WebhookActions.Changed } } };

        Assert.True(await manager.InvalidateAsync(batch, CreateClient(mock)));

        Assert.Single(manager.Invalidations);
        Assert.Equal(1, mock.GetMatchCount(usedIn));
    }

    [Fact]
    public async Task InvalidateAsync_WithoutAssets_DoesNotCallTheApi()
    {
        var mock = new MockHttpMessageHandler();
        mock.Fallback.Throw(new InvalidOperationException("The API was called."));
        var manager = new RecordingCacheManager();
        var batch = new[]
        {
            Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, "hero"),
            Notification(WebhookObjectTypes.Language, WebhookActions.Changed, "en"),
        };

        Assert.True(await manager.InvalidateAsync(batch, CreateClient(mock)));

        Assert.Equal(["item_hero", DeliveryCacheDependencies.ItemsListScope], Assert.Single(manager.Invalidations));
    }

    [Fact]
    public async Task InvalidateAsync_WithNothingToInvalidate_TouchesNothing()
    {
        var manager = new RecordingCacheManager();
        var batch = new[] { Notification(WebhookObjectTypes.Language, WebhookActions.Changed, "en") };

        Assert.True(await manager.InvalidateAsync(batch, CreateClient(new MockHttpMessageHandler())));

        Assert.Empty(manager.Invalidations);
    }

    [Fact]
    public async Task InvalidateAsync_ReturnsFalse_WhenAnyInvalidationDidNotComplete()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{BaseUrl}/assets/sofia_patel_jpg/used-in").Respond("application/json", UsagePage("about"));
        var manager = new RecordingCacheManager { Outcomes = new([true, false]) };
        var batch = new WebhookNotification
        {
            Notifications = [Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, "hero"), .. Load("AssetMetadataChanged.json").Notifications]
        };

        Assert.False(await manager.InvalidateAsync(batch, CreateClient(mock)));

        Assert.Equal(2, manager.Invalidations.Count);
    }

    [Fact]
    public async Task InvalidateAsync_Throws_WhenAnAssetLookupFails()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{BaseUrl}/assets/sofia_patel_jpg/used-in").Respond(HttpStatusCode.InternalServerError);
        var manager = new RecordingCacheManager();
        var batch = new WebhookNotification
        {
            Notifications = [Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, "hero"), .. Load("AssetMetadataChanged.json").Notifications]
        };

        await Assert.ThrowsAsync<DeliveryRequestException>(() => manager.InvalidateAsync(batch, CreateClient(mock)));

        Assert.Equal(["item_hero", DeliveryCacheDependencies.ItemsListScope], Assert.Single(manager.Invalidations));
    }

    [Fact]
    public async Task InvalidateAsync_RejectsNulls()
    {
        var manager = new RecordingCacheManager();
        var client = CreateClient(new MockHttpMessageHandler());
        var notification = Load("ContentItemPublished.json");

        await Assert.ThrowsAsync<ArgumentNullException>(() => ((IDeliveryCacheManager)null!).InvalidateAsync(notification, client));
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.InvalidateAsync((WebhookNotification)null!, client));
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.InvalidateAsync((IEnumerable<WebhookModel>)null!, client));
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.InvalidateAsync(notification, null!));
    }

    private static WebhookModel Notification(string objectType, string action, string codename, string? taxonomyGroup = null) => new()
    {
        Data = new WebhookData
        {
            System = new WebhookItem
            {
                Id = Guid.NewGuid(),
                Name = codename,
                Codename = codename,
                TaxonomyGroup = taxonomyGroup,
                LastModified = DateTime.UtcNow,
            }
        },
        Message = new WebhookMessage
        {
            EnvironmentId = Guid.Parse(EnvironmentId),
            ObjectType = objectType,
            Action = action,
            DeliverySlot = WebhookDeliverySlots.Published,
        }
    };

    private static WebhookNotification Load(string file)
    {
        var json = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Data", file));
        return JsonSerializer.Deserialize<WebhookNotification>(json)!;
    }

    private static string UsagePage(string codename) => $$"""
        { "items": [{ "system": { "id": "22222222-2222-2222-2222-222222222222", "name": "{{codename}}", "codename": "{{codename}}", "language": "default", "type": "article", "collection": "default", "workflow": "default", "workflow_step": "published", "last_modified": "2026-09-08T00:00:00Z" } }] }
        """;

    private static IDeliveryClient CreateClient(MockHttpMessageHandler mock)
    {
        mock.When($"{BaseUrl}/languages").Respond("application/json", """
            {
              "languages": [{ "system": { "id": "11111111-1111-1111-1111-111111111111", "name": "Default", "codename": "default" } }],
              "pagination": { "skip": 0, "limit": 1, "count": 1, "next_page": "" }
            }
            """);
        var services = new ServiceCollection();
        services.AddDeliveryClient(
            new DeliveryOptions { EnvironmentId = EnvironmentId, EnableResilience = false },
            delivery => delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock));
        return services.BuildServiceProvider().GetRequiredService<IDeliveryClient>();
    }

    private sealed class RecordingCacheManager : IDeliveryCacheManager
    {
        public List<string[]> Invalidations { get; } = [];
        public Queue<bool> Outcomes { get; init; } = new();

        public CacheStorageMode StorageMode => CacheStorageMode.HydratedObject;

        public Task<CacheResult<T>?> GetOrSetAsync<T>(string cacheKey, Func<CancellationToken, Task<CacheEntry<T>?>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
            where T : class
            => throw new NotSupportedException();

        public Task<bool> InvalidateAsync(string[] dependencyKeys, CancellationToken cancellationToken = default)
        {
            Invalidations.Add(dependencyKeys);
            return Task.FromResult(!Outcomes.TryDequeue(out var outcome) || outcome);
        }
    }
}
