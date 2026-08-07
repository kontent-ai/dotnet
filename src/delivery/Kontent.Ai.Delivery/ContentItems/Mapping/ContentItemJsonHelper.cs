using System.Text.Json;

namespace Kontent.Ai.Delivery.ContentItems.Mapping;

internal static class ContentItemJsonHelper
{
    /// <summary>
    /// Whether a modular-content entry is a component rather than a content item.
    /// </summary>
    /// <remarks>
    /// A component lives inside its owning item's rich text and has no life of its own. The Delivery API
    /// marks that structurally: <c>workflow</c> and <c>workflow_step</c> are present on every content item
    /// and on no component. The generated codename is not a reliable signal - an authored codename can take
    /// the same shape.
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
