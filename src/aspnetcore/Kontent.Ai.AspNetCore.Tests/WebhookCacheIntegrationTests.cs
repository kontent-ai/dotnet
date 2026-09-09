using System.Text.Json;
using Kontent.Ai.AspNetCore.Webhooks;
using Kontent.Ai.AspNetCore.Webhooks.Models;
using Kontent.Ai.Delivery;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.AspNetCore.Tests;

public class WebhookCacheIntegrationTests
{
    private const string EnvironmentId = "195a50c1-f1b8-0066-9eb4-83f7246d5459";
    private const string BaseUrl = $"https://deliver.kontent.ai/{EnvironmentId}";

    [Theory]
    [InlineData(WebhookObjectTypes.ContentType, false)]
    [InlineData(WebhookObjectTypes.ContentType, true)]
    [InlineData(WebhookObjectTypes.Taxonomy, false)]
    [InlineData(WebhookObjectTypes.Taxonomy, true)]
    public async Task TypeOrTaxonomyChange_InvalidatesFilteredItemListings(string objectType, bool initiallyPopulated)
    {
        using var mock = new MockHttpMessageHandler();
        mock.Expect($"{BaseUrl}/items").Respond("application/json", Listing(initiallyPopulated ? ["first"] : []));
        using var provider = CreateProvider(mock);
        var client = provider.GetRequiredService<IDeliveryClient>();
        var cache = provider.GetRequiredService<IDeliveryCacheManager>();
        var query = client.GetItems<IDynamicElements>()
            .WithElements("title")
            .Where(f => objectType == WebhookObjectTypes.ContentType
                ? f.System("type").IsEqualTo("article")
                : f.Element("category").ContainsAny("handheld"));

        var before = await query.ExecuteAsync();
        Assert.True(before.IsSuccess);
        Assert.Equal(initiallyPopulated ? 1 : 0, before.Value.Items.Count);
        Assert.True((await query.ExecuteAsync()).IsCacheHit);
        if (objectType == WebhookObjectTypes.Taxonomy)
            Assert.DoesNotContain(DeliveryCacheDependencies.ForTaxonomy("categories"), before.DependencyKeys!);
        else if (!initiallyPopulated)
            Assert.DoesNotContain(DeliveryCacheDependencies.ForType("article"), before.DependencyKeys!);

        mock.Expect($"{BaseUrl}/items").Respond("application/json", Listing(["first", "second"]));
        Assert.True(await cache.InvalidateAsync([Notification(objectType)], client));

        var after = await query.ExecuteAsync();
        Assert.True(after.IsSuccess);
        Assert.False(after.IsCacheHit);
        Assert.Equal(2, after.Value.Items.Count);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task TaxonomyTermChange_InvalidatesSingleItemByGroupKey()
    {
        using var mock = new MockHttpMessageHandler();
        mock.Expect($"{BaseUrl}/items/article").Respond("application/json", Item("Old name"));
        using var provider = CreateProvider(mock);
        var client = provider.GetRequiredService<IDeliveryClient>();
        var cache = provider.GetRequiredService<IDeliveryCacheManager>();
        var query = client.GetItem<IDynamicElements>("article");

        var before = await query.ExecuteAsync();
        Assert.True(before.IsSuccess);
        Assert.Contains(DeliveryCacheDependencies.ForTaxonomy("categories"), before.DependencyKeys!);
        Assert.DoesNotContain(DeliveryCacheDependencies.ItemsListScope, before.DependencyKeys!);
        Assert.True((await query.ExecuteAsync()).IsCacheHit);

        mock.Expect($"{BaseUrl}/items/article").Respond("application/json", Item("New name"));
        Assert.True(await cache.InvalidateAsync([Notification(WebhookObjectTypes.Taxonomy)], client));

        var after = await query.ExecuteAsync();
        Assert.True(after.IsSuccess);
        Assert.False(after.IsCacheHit);
        mock.VerifyNoOutstandingExpectation();
    }

    private static ServiceProvider CreateProvider(MockHttpMessageHandler mock)
    {
        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = EnvironmentId, EnableResilience = false }, delivery =>
        {
            delivery.UseMemoryCache();
            delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock);
        });
        return services.BuildServiceProvider();
    }

    private static WebhookModel Notification(string objectType) => new()
    {
        Data = new WebhookData { System = new WebhookItem
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Changed",
            Codename = objectType == WebhookObjectTypes.Taxonomy ? "handheld" : "article",
            TaxonomyGroup = objectType == WebhookObjectTypes.Taxonomy ? "categories" : null,
            LastModified = DateTime.UnixEpoch,
        } },
        Message = new WebhookMessage
        {
            EnvironmentId = Guid.Parse(EnvironmentId), ObjectType = objectType,
            Action = objectType == WebhookObjectTypes.Taxonomy ? WebhookActions.TermChanged : WebhookActions.Changed,
            DeliverySlot = WebhookDeliverySlots.Published,
        }
    };

    private static object SystemAttributes(string codename) => new
    {
        id = "22222222-2222-2222-2222-222222222222", name = codename, codename, type = "article",
        language = "default", collection = "default", last_modified = DateTime.UnixEpoch,
    };

    private static string Listing(string[] codenames) => JsonSerializer.Serialize(new
    {
        items = codenames.Select(codename => new
        {
            system = SystemAttributes(codename),
            elements = new { title = new { type = "text", name = "Title", value = codename } },
        }),
        modular_content = new { },
        pagination = new { skip = 0, limit = 10, count = codenames.Length, next_page = "" },
    });

    private static string Item(string termName) => JsonSerializer.Serialize(new
    {
        item = new
        {
            system = SystemAttributes("article"),
            elements = new { category = new
            {
                type = "taxonomy", name = "Category", taxonomy_group = "categories",
                value = new[] { new { name = termName, codename = "handheld" } },
            } },
        },
        modular_content = new { },
    });
}
