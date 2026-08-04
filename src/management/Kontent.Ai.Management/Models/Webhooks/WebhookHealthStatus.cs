
namespace Kontent.Ai.Management.Models.Webhooks;

/// <summary>
/// Webhook health status.
/// </summary>
public enum WebhookHealthStatus
{
    /// <summary>
    /// Appears for newly created webhooks before any notification is sent.
    /// </summary>
    [JsonStringEnumMemberName("unknown")]
    Unknown = 0,

    /// <summary>
    /// Appears for webhooks that have successfully delivered notifications.
    /// </summary>
    [JsonStringEnumMemberName("working")]
    Working = 1,

    /// <summary>
    /// Appears for webhooks that have not been successful in delivering notifications. 
    /// </summary>
    [JsonStringEnumMemberName("failing")]
    Failing = 2,

    /// <summary>
    /// Appears for webhooks where notification delivery has repeatedly failed for 7 days. 
    /// </summary>
    [JsonStringEnumMemberName("dead")]
    Dead = 3
}