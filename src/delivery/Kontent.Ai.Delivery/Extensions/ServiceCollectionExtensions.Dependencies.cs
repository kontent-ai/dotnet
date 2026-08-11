using System.Text.Json;
using AngleSharp.Html.Parser;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Kontent.Ai.Delivery.ContentItems.Processing;
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
        services.TryAddSingleton<IItemTypingStrategy, DefaultItemTypingStrategy>();
        services.TryAddSingleton<IContentDeserializer>(sp =>
            new ContentDeserializer(sp.GetRequiredService<DeliveryJsonOptions>().Value));
        services.TryAddSingleton(sp => new ElementValueMapper(
            sp.GetRequiredService<IContentDependencyExtractor>(),
            sp.GetRequiredService<DeliveryJsonOptions>().Value,
            sp.GetRequiredService<IHtmlParser>(),
            sp.GetService<ILogger<ElementValueMapper>>()));
        services.TryAddSingleton<LinkedItemResolver>();
        services.TryAddSingleton<ContentItemMapper>();
        services.TryAddSingleton<IHtmlParser, HtmlParser>();

        // Dependency extraction is used for cache invalidation and for optional dependency metadata on results.
        services.TryAddSingleton<IContentDependencyExtractor, ContentDependencyExtractor>();
    }
}
