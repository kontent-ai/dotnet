using System.Text.Json.Serialization;

namespace Kontent.Ai.AspNetCore.Webhooks.Models;

/// <summary>
/// Root object of a Kontent.ai webhook request: a batch of notifications, one per changed object.
/// See the <see href="https://kontent.ai/learn/docs/webhooks/webhooks/net">webhooks reference</see>.
/// </summary>
public sealed record WebhookNotification
{
    /// <summary>
    /// One notification per modified object. Notifications may be batched, so a single request can carry several.
    /// </summary>
    [JsonPropertyName("notifications"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WebhookModel>? Notifications { get; init; }
}

/// <summary>
/// One notification: which object changed (<see cref="Data"/>) and due to which event (<see cref="Message"/>).
/// </summary>
public sealed record WebhookModel
{
    /// <summary>
    /// Metadata identifying the object that changed.
    /// </summary>
    [JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookData? Data { get; init; }

    /// <summary>
    /// Where the change occurred and due to which event.
    /// </summary>
    [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookMessage? Message { get; init; }
}

/// <summary>
/// Metadata identifying the object that changed.
/// </summary>
public sealed record WebhookData
{
    /// <summary>
    /// System properties of the changed object.
    /// </summary>
    [JsonPropertyName("system"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookItem? System { get; init; }
}

/// <summary>
/// Context of the change: the environment, the kind of object, the event, and which delivery slot it concerns.
/// </summary>
public sealed record WebhookMessage
{
    /// <summary>
    /// Identifier of the environment the notification came from.
    /// </summary>
    [JsonPropertyName("environment_id")]
    public Guid EnvironmentId { get; init; }

    /// <summary>
    /// Kind of object that changed; one of the <see cref="WebhookObjectTypes"/> values.
    /// </summary>
    [JsonPropertyName("object_type")]
    public string? ObjectType { get; init; }

    /// <summary>
    /// What happened to the object; one of the <see cref="WebhookActions"/> values.
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    /// <summary>
    /// Present only when <see cref="Action"/> is <see cref="WebhookActions.WorkflowStepChanged"/>: the workflow state the item left.
    /// </summary>
    [JsonPropertyName("action_context"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookActionContext? ActionContext { get; init; }

    /// <summary>
    /// Whether the change concerns preview or published data; one of the <see cref="WebhookDeliverySlots"/> values.
    /// Assets, content types, languages and taxonomies are shared between the two, so for those the slot only
    /// says which slot the webhook was configured for.
    /// </summary>
    [JsonPropertyName("delivery_slot")]
    public string? DeliverySlot { get; init; }
}

/// <summary>
/// The workflow state a content item left in a <see cref="WebhookActions.WorkflowStepChanged"/> event.
/// </summary>
public sealed record WebhookActionContext
{
    /// <summary>
    /// Codename of the item's previous workflow.
    /// </summary>
    [JsonPropertyName("previous_workflow")]
    public string? PreviousWorkflow { get; init; }

    /// <summary>
    /// Codename of the item's previous workflow step.
    /// </summary>
    [JsonPropertyName("previous_workflow_step")]
    public string? PreviousWorkflowStep { get; init; }
}

/// <summary>
/// System properties of the changed object. <see cref="Id"/>, <see cref="Name"/>, <see cref="Codename"/> and
/// <see cref="LastModified"/> are sent for every kind of object; the rest depend on the kind.
/// </summary>
public sealed record WebhookItem
{
    /// <summary>
    /// The object's internal ID.
    /// </summary>
    [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Id { get; init; }

    /// <summary>
    /// The object's display name.
    /// </summary>
    [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>
    /// The object's codename. For a taxonomy term event this is the term's codename and
    /// <see cref="TaxonomyGroup"/> names the group.
    /// </summary>
    [JsonPropertyName("codename"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Codename { get; init; }

    /// <summary>
    /// Codename of the collection; content items and assets.
    /// </summary>
    [JsonPropertyName("collection"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Collection { get; init; }

    /// <summary>
    /// Codename of the content item's workflow; content items only.
    /// </summary>
    [JsonPropertyName("workflow"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Workflow { get; init; }

    /// <summary>
    /// Codename of the content item's workflow step; content items only.
    /// </summary>
    [JsonPropertyName("workflow_step"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkflowStep { get; init; }

    /// <summary>
    /// Codename of the content item's language; content items only.
    /// </summary>
    [JsonPropertyName("language"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; init; }

    /// <summary>
    /// Codename of the content item's content type; content items only.
    /// </summary>
    [JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }

    /// <summary>
    /// Codename of the taxonomy group a term belongs to; taxonomy term events only.
    /// </summary>
    [JsonPropertyName("taxonomy_group"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TaxonomyGroup { get; init; }

    /// <summary>
    /// When the object was last modified, in UTC.
    /// </summary>
    [JsonPropertyName("last_modified")]
    public DateTime LastModified { get; init; }
}
