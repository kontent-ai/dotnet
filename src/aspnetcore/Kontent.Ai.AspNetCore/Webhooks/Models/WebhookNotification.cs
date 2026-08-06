using System.Text.Json.Serialization;

namespace Kontent.Ai.AspNetCore.Webhooks.Models;

/// <summary>
/// Root object of a Kontent.ai triggered webhook.
/// See <see href="https://kontent.ai/learn/docs/webhooks/webhooks/net">webhooks reference documentation</see> for details.
/// </summary>
public sealed record WebhookNotification
{
    /// <summary>
    /// A collection of webhook notifications for each modified object.
    /// </summary>
    [JsonPropertyName("notifications"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookModel[]? Notifications { get; init; }
}

/// <summary>
/// The webhook model that contains data and message.
/// </summary>
public sealed record WebhookModel
{
    /// <summary>
    /// Data relevant to the object that triggered the webhook.
    /// </summary>
    [JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookData? Data { get; init; }
    /// <summary>
    /// The Message object contains information about the origin of the notification.
    /// </summary>
    [JsonPropertyName("message"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookMessage? Message { get; init; }
}

/// <summary>
/// Data relevant to the object that triggered the webhook.
/// </summary>
public sealed record WebhookData
{
    /// <summary>
    /// Metadata of the modified object.
    /// </summary>
    [JsonPropertyName("system"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WebhookItem? System { get; init; }
}

/// <summary>
/// The Message object contains information about the origin of the notification.
/// </summary>
public sealed record WebhookMessage
{
    /// <summary>
    /// Identifier of the environment the notification came from.
    /// </summary>
    [JsonPropertyName("environment_id")]
    public Guid EnvironmentId { get; init; }

    /// <summary>
    /// Type of the object that triggered the webhook (content_item_variant, taxonomy, ...)
    /// </summary>
    [JsonPropertyName("object_type")]
    public string? ObjectType { get; init; }

    /// <summary>
    /// Codename of the action that triggered the webhook (published, unpublished, created, deleted, ...).
    /// </summary>
    [JsonPropertyName("action")]
    public string? Action { get; init; }

    /// <summary>
    /// Codename of the delivery slot where the webhook was triggered (preview, published).
    /// </summary>
    [JsonPropertyName("delivery_slot")]
    public string? DeliverySlot { get; init; }
}

/// <summary>
/// Represents metadata of the modified object.
/// </summary>
public sealed record WebhookItem
{
    /// <summary>
    /// The object's ID.
    /// </summary>
    [JsonPropertyName("id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Id { get; init; }

    /// <summary>
    /// The item's name.
    /// </summary>
    [JsonPropertyName("name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>
    /// The item's codename.
    /// </summary>
    [JsonPropertyName("codename"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Codename { get; init; }

    /// <summary>
    /// The item's collection.
    /// </summary>
    [JsonPropertyName("collection"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Collection { get; init; }

    /// <summary>
    /// The item's workflow.
    /// </summary>
    [JsonPropertyName("workflow"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Workflow { get; init; }

    /// <summary>
    /// The item's workflow step.
    /// </summary>
    [JsonPropertyName("workflow_step"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkflowStep { get; init; }

    /// <summary>
    /// Codename of the item's language.
    /// </summary>
    [JsonPropertyName("language"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Language { get; init; }

    /// <summary>
    /// The item's type.
    /// </summary>
    [JsonPropertyName("type"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }

    /// <summary>
    /// Timestamp of when the item was modified.
    /// </summary>
    [JsonPropertyName("last_modified")]
    public DateTime LastModified { get; init; }
}
