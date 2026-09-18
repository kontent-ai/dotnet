using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.Logging;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Api.Filtering;

internal static class SystemFilterHelpers
{
    internal static void AddSystemLanguageFilter(ICollection<KeyValuePair<string, string>> filters, string languageCodename)
    {
        ArgumentNullException.ThrowIfNull(filters);
        filters.Add(new KeyValuePair<string, string>(
            FilterPath.System("language") + FilterSuffix.Eq,
            FilterValueSerializer.Serialize(languageCodename)));
    }

    internal static void AddGenericTypeFilter<TModel>(
        ICollection<KeyValuePair<string, string>> filters,
        ITypeProvider typeProvider,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(typeProvider);

        if (ModelTypeHelper.IsDynamic<TModel>())
            return;

        var codename = typeProvider.GetCodename(typeof(TModel));

        if (string.IsNullOrWhiteSpace(codename))
        {
            var modelName = typeof(TModel).FullName ?? typeof(TModel).Name;
            if (logger is not null)
            {
                LoggerMessages.GenericQueryTypeCodenameNotFound(logger, modelName);
            }

            throw new InvalidOperationException(
                $"Cannot resolve a content type codename for model '{modelName}'. " +
                "Ensure the model has [ContentTypeCodename] and its project references Kontent.Ai.Delivery.SourceGeneration, " +
                "or register an ITypeProvider that maps this model. To query without a model, use the non-generic overload. " +
                "See https://github.com/kontent-ai/dotnet/blob/main/src/delivery/docs/models.md#typed-query-validation.");
        }

        var typeFilterKeyPrefix = FilterPath.System("type") + "[";
        var hasTypeFilter = filters.Any(kvp =>
            kvp.Key.StartsWith(typeFilterKeyPrefix, StringComparison.OrdinalIgnoreCase));
        if (hasTypeFilter && logger is not null)
        {
            LoggerMessages.GenericQueryTypeFilterConflict(logger, typeof(TModel).Name, codename);
        }

        var typeFilterKey = FilterPath.System("type") + FilterSuffix.Eq;
        var typeFilterValue = FilterValueSerializer.Serialize(codename);
        var hasSameAutoFilter = filters.Any(kvp =>
            kvp.Key.Equals(typeFilterKey, StringComparison.OrdinalIgnoreCase) &&
            kvp.Value.Equals(typeFilterValue, StringComparison.Ordinal));
        if (hasSameAutoFilter)
            return;

        filters.Add(new KeyValuePair<string, string>(typeFilterKey, typeFilterValue));
    }
}
