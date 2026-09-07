using System.Text.Json;
using Kontent.Ai.AspNetCore.Webhooks.Models;

namespace Kontent.Ai.AspNetCore.Tests;

/// <summary>
/// The fixtures under <c>Data/</c> are the example payloads from the Kontent.ai webhook documentation,
/// one per documented shape (published and unpublished items share one).
/// </summary>
public class WebhookNotificationTests
{
    private static readonly Guid EnvironmentId = Guid.Parse("195a50c1-f1b8-0066-9eb4-83f7246d5459");

    [Theory]
    [InlineData("ContentItemPublished.json", WebhookObjectTypes.ContentItem, WebhookActions.Published, WebhookDeliverySlots.Published, "this_changes_everything")]
    [InlineData("ContentItemWorkflowStepChanged.json", WebhookObjectTypes.ContentItem, WebhookActions.WorkflowStepChanged, WebhookDeliverySlots.Preview, "solutions_imaging")]
    [InlineData("AssetMetadataChanged.json", WebhookObjectTypes.Asset, WebhookActions.MetadataChanged, WebhookDeliverySlots.Preview, "sofia_patel_jpg")]
    [InlineData("ContentTypeChanged.json", WebhookObjectTypes.ContentType, WebhookActions.Changed, WebhookDeliverySlots.Preview, "page")]
    [InlineData("LanguageDeleted.json", WebhookObjectTypes.Language, WebhookActions.Deleted, WebhookDeliverySlots.Published, "es-ES")]
    [InlineData("TaxonomyTermCreated.json", WebhookObjectTypes.Taxonomy, WebhookActions.TermCreated, WebhookDeliverySlots.Published, "handheld")]
    public void EveryDocumentedPayload_Deserializes(string file, string objectType, string action, string slot, string codename)
    {
        var notification = Load(file);

        var model = Assert.Single(notification.Notifications);
        Assert.Equal(EnvironmentId, model.Message.EnvironmentId);
        Assert.Equal(objectType, model.Message.ObjectType);
        Assert.Equal(action, model.Message.Action);
        Assert.Equal(slot, model.Message.DeliverySlot);
        Assert.Equal(codename, model.Data.System.Codename);
        Assert.NotEqual(Guid.Empty, model.Data.System.Id);
        Assert.NotEmpty(model.Data.System.Name);
        Assert.Equal(DateTimeKind.Utc, model.Data.System.LastModified.Kind);
    }

    [Fact]
    public void ContentItem_CarriesTheItemOnlyFields()
    {
        var system = Load("ContentItemPublished.json").Notifications[0].Data.System;

        Assert.Equal("marketing", system.Collection);
        Assert.Equal("default", system.Workflow);
        Assert.Equal("published", system.WorkflowStep);
        Assert.Equal("english", system.Language);
        Assert.Equal("product_update", system.Type);
        Assert.Null(system.TaxonomyGroup);
    }

    [Fact]
    public void WorkflowStepChanged_CarriesThePreviousState()
    {
        var message = Load("ContentItemWorkflowStepChanged.json").Notifications[0].Message;

        Assert.Equal("default", message.ActionContext!.PreviousWorkflow);
        Assert.Equal("published", message.ActionContext.PreviousWorkflowStep);
    }

    [Fact]
    public void OtherActions_HaveNoActionContext()
    {
        Assert.Null(Load("ContentItemPublished.json").Notifications[0].Message.ActionContext);
    }

    // For a term event the codename is the term's; the group is what a cache key needs.
    [Fact]
    public void TaxonomyTerm_CarriesItsGroup()
    {
        var system = Load("TaxonomyTermCreated.json").Notifications[0].Data.System;

        Assert.Equal("handheld", system.Codename);
        Assert.Equal("product_category", system.TaxonomyGroup);
    }

    // A member the documentation lists for every event is required: a payload without it fails to bind
    // rather than arriving with a null the handler has to check for.
    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"notifications":[{"data":{"system":{}},"message":{}}]}""")]
    [InlineData("""{"notifications":[{"message":{"environment_id":"195a50c1-f1b8-0066-9eb4-83f7246d5459","object_type":"asset","action":"created","delivery_slot":"published"}}]}""")]
    public void PayloadMissingADocumentedMember_FailsToBind(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WebhookNotification>(json));
    }

    private static WebhookNotification Load(string file)
    {
        var json = File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Data", file));
        return JsonSerializer.Deserialize<WebhookNotification>(json)!;
    }
}
