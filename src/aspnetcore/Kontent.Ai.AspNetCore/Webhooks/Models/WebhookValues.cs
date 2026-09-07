namespace Kontent.Ai.AspNetCore.Webhooks.Models;

// The event vocabulary is kept as string constants rather than enums: a value Kontent.ai adds later must
// deserialize rather than fail, because a 400 makes the sender retry the notification for three days.

/// <summary>
/// Values of <see cref="WebhookMessage.ObjectType"/>.
/// </summary>
public static class WebhookObjectTypes
{
    /// <summary>An asset.</summary>
    public const string Asset = "asset";

    /// <summary>A content item, or one of its language variants.</summary>
    public const string ContentItem = "content_item";

    /// <summary>A content type.</summary>
    public const string ContentType = "content_type";

    /// <summary>A language.</summary>
    public const string Language = "language";

    /// <summary>A taxonomy group or one of its terms; see <see cref="WebhookItem.TaxonomyGroup"/>.</summary>
    public const string Taxonomy = "taxonomy";
}

/// <summary>
/// Values of <see cref="WebhookMessage.Action"/>.
/// </summary>
public static class WebhookActions
{
    /// <summary>The object was created.</summary>
    public const string Created = "created";

    /// <summary>The object's content or configuration changed.</summary>
    public const string Changed = "changed";

    /// <summary>The object was deleted.</summary>
    public const string Deleted = "deleted";

    /// <summary>The object's name, codename or collection changed.</summary>
    public const string MetadataChanged = "metadata_changed";

    /// <summary>A content item variant was published.</summary>
    public const string Published = "published";

    /// <summary>A content item variant was unpublished.</summary>
    public const string Unpublished = "unpublished";

    /// <summary>A content item variant moved to another workflow step; see <see cref="WebhookMessage.ActionContext"/>.</summary>
    public const string WorkflowStepChanged = "workflow_step_changed";

    /// <summary>A taxonomy term was created.</summary>
    public const string TermCreated = "term_created";

    /// <summary>A taxonomy term was renamed or its codename changed.</summary>
    public const string TermChanged = "term_changed";

    /// <summary>A taxonomy term was deleted.</summary>
    public const string TermDeleted = "term_deleted";

    /// <summary>Taxonomy terms were reordered within their group.</summary>
    public const string TermsMoved = "terms_moved";
}

/// <summary>
/// Values of <see cref="WebhookMessage.DeliverySlot"/>.
/// </summary>
public static class WebhookDeliverySlots
{
    /// <summary>The change concerns preview data.</summary>
    public const string Preview = "preview";

    /// <summary>The change concerns published data.</summary>
    public const string Published = "published";
}
