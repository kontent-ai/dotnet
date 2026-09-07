using System.Text.Json;
using Kontent.Ai.AspNetCore.Webhooks;
using Kontent.Ai.AspNetCore.Webhooks.Models;
using Kontent.Ai.Delivery.Abstractions;

namespace Kontent.Ai.AspNetCore.Tests;

public class WebhookNotificationExtensionsTests
{
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
            EnvironmentId = Guid.NewGuid(),
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
}
