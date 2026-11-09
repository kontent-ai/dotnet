namespace Kontent.Ai.AspNetCore.Webhooks;

/// <summary>
/// Options for validating Kontent.ai webhooks.
/// </summary>
public sealed class WebhookOptions
{
    /// <summary>
    /// Webhook secret used for webhook signature validation.
    /// </summary>
    public string? Secret { get; set; }
}
