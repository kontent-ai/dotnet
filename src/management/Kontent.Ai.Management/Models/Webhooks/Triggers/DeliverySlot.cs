namespace Kontent.Ai.Management.Models.Webhooks.Triggers;

/// <summary>
/// Represents the delivery slot.
/// </summary>
public enum DeliverySlot
{
    /// <summary>
    /// Published data.
    /// </summary>
    [JsonStringEnumMemberName("published")]
    Published,

    /// <summary>
    /// Preview data.
    /// </summary>
    [JsonStringEnumMemberName("preview")]
    Preview
}