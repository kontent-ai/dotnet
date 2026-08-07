using System.Text.Json;

namespace Kontent.Ai.Delivery.ContentItems.Mapping;

internal static class ContentItemJsonHelper
{
    /// <summary>
    /// Whether a modular-content entry is a component rather than a content item.
    /// </summary>
    /// <remarks>
    /// A component lives inside its owning item's rich text and has no life of its own: no workflow, and a
    /// generated codename. The Delivery API says so structurally - <c>workflow</c> and <c>workflow_step</c>
    /// are present on every content item and on no component - which is a contract, unlike the shape of the
    /// generated codename, which an authored codename can coincide with.
    /// </remarks>
    public static bool IsComponent(JsonElement itemElement) =>
        itemElement.TryGetProperty("system", out var system)
        && !system.TryGetProperty("workflow", out _)
        && !system.TryGetProperty("workflow_step", out _);

    public static string ExtractContentType(JsonElement itemElement) =>
        itemElement.TryGetProperty("system", out var system) &&
        system.TryGetProperty("type", out var type)
            ? type.GetString() ?? string.Empty
            : string.Empty;
}
