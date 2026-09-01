using System.Text.Json;
using AngleSharp.Html.Parser;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery;

public static partial class ServiceCollectionExtensions
{
    private static void RegisterDependencies(IServiceCollection services, JsonSerializerOptions sharedJsonOptions)
    {
        // JSON serialization (shared instance — same one used by Refit). Held under an SDK-private type so
        // the application's own JsonSerializerOptions registration stays the application's.
        services.TryAddSingleton(new DeliveryJsonOptions(sharedJsonOptions));

        // Core services
        services.TryAddSingleton<ITypeProvider, TypeProvider>();
        services.TryAddSingleton<ItemTypingStrategy>();
        services.TryAddSingleton(sp =>
            new ContentDeserializer(sp.GetRequiredService<DeliveryJsonOptions>().Value));
        services.TryAddSingleton(sp => new ElementValueMapper(
            sp.GetRequiredService<DeliveryJsonOptions>().Value,
            sp.GetRequiredService<IHtmlParser>(),
            sp.GetService<ILogger<ElementValueMapper>>()));
        services.TryAddSingleton<LinkedItemResolver>();
        services.TryAddSingleton<ContentItemMapper>();
        services.TryAddSingleton<IHtmlParser, HtmlParser>();
    }
}
