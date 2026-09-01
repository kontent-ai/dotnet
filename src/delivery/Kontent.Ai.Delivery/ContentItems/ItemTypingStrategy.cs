using System.Collections.Concurrent;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.ContentItems;

/// <summary>
/// Maps a content type codename to the model type to deserialize it as, falling back to
/// <see cref="DynamicElements"/> when the <see cref="ITypeProvider"/> has no mapping for it.
/// </summary>
/// <param name="typeProvider">The type provider to use for resolving model types.</param>
/// <param name="logger">Optional logger for diagnostic output.</param>
internal sealed class ItemTypingStrategy(ITypeProvider typeProvider, ILogger<ItemTypingStrategy>? logger = null)
{
    private readonly ConcurrentDictionary<string, Type> _cache = new();

    /// <summary>
    /// Resolves the model type for the given content type codename.
    /// Uses cached results for repeated lookups.
    /// </summary>
    /// <param name="contentTypeCodename">The content type codename.</param>
    /// <returns>The resolved model type, or <see cref="DynamicElements"/> if no mapping exists.</returns>
    public Type ResolveModelType(string contentTypeCodename)
    {
        if (string.IsNullOrEmpty(contentTypeCodename))
        {
            if (logger is not null)
                LoggerMessages.ContentTypeFallbackToDynamic(logger, contentTypeCodename ?? "(null)");
            return typeof(DynamicElements);
        }

        return _cache.GetOrAdd(contentTypeCodename, codename =>
        {
            var modelType = typeProvider.GetType(codename);
            if (modelType is null && logger is not null)
            {
                LoggerMessages.ContentTypeFallbackToDynamic(logger, codename);
            }
            return modelType ?? typeof(DynamicElements);
        });
    }
}
