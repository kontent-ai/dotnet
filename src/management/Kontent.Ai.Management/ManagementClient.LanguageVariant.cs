using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.LanguageVariants;
using Kontent.Ai.Management.Models.LanguageVariants.Elements;
using Kontent.Ai.Management.Models.Workflow;

namespace Kontent.Ai.Management;

public partial class ManagementClient
{
    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel>>> ListLanguageVariantsByItemAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsByItemInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel<T>>>> ListLanguageVariantsByItemAsync<T>(Reference identifier, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsByItemInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync(ToTypedVariants<T>);
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel>>> ListLanguageVariantsByTypeAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var typeSegment = identifier.ToUrlSegment();
        return PageEnumerator.CollectAsync<LanguageVariantsListingResponseServerModel, LanguageVariantModel>(
            (token, ct) => ManagementApi.ListLanguageVariantsByTypeInternalAsync(typeSegment, token, ct),
            page => page.Variants,
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel<T>>>> ListLanguageVariantsByTypeAsync<T>(Reference identifier, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var typeSegment = identifier.ToUrlSegment();
        return PageEnumerator.CollectAsync<LanguageVariantsListingResponseServerModel, LanguageVariantModel<T>>(
            (token, ct) => ManagementApi.ListLanguageVariantsByTypeInternalAsync(typeSegment, token, ct),
            page => ToTypedVariants<T>(page.Variants),
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<LanguageVariantModel>>> ListLanguageVariantsByTypePageAsync(Reference identifier, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsByTypeInternalAsync(identifier.ToUrlSegment(), continuationToken, cancellationToken)
            .ToManagementResultAsync(page => new ListingPage<LanguageVariantModel>
            {
                Items = page.Variants,
                ContinuationToken = page.Pagination?.Token,
            });
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<LanguageVariantModel<T>>>> ListLanguageVariantsByTypePageAsync<T>(Reference identifier, string? continuationToken = null, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsByTypeInternalAsync(identifier.ToUrlSegment(), continuationToken, cancellationToken)
            .ToManagementResultAsync(ToTypedPage<T>);
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel>>> ListLanguageVariantsOfContentTypeWithComponentsAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var typeSegment = identifier.ToUrlSegment();
        return PageEnumerator.CollectAsync<LanguageVariantsListingResponseServerModel, LanguageVariantModel>(
            (token, ct) => ManagementApi.ListLanguageVariantsOfContentTypeWithComponentsInternalAsync(typeSegment, token, ct),
            page => page.Variants,
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel<T>>>> ListLanguageVariantsOfContentTypeWithComponentsAsync<T>(Reference identifier, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var typeSegment = identifier.ToUrlSegment();
        return PageEnumerator.CollectAsync<LanguageVariantsListingResponseServerModel, LanguageVariantModel<T>>(
            (token, ct) => ManagementApi.ListLanguageVariantsOfContentTypeWithComponentsInternalAsync(typeSegment, token, ct),
            page => ToTypedVariants<T>(page.Variants),
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<LanguageVariantModel>>> ListLanguageVariantsOfContentTypeWithComponentsPageAsync(Reference identifier, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsOfContentTypeWithComponentsInternalAsync(identifier.ToUrlSegment(), continuationToken, cancellationToken)
            .ToManagementResultAsync(page => new ListingPage<LanguageVariantModel>
            {
                Items = page.Variants,
                ContinuationToken = page.Pagination?.Token,
            });
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<LanguageVariantModel<T>>>> ListLanguageVariantsOfContentTypeWithComponentsPageAsync<T>(Reference identifier, string? continuationToken = null, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsOfContentTypeWithComponentsInternalAsync(identifier.ToUrlSegment(), continuationToken, cancellationToken)
            .ToManagementResultAsync(ToTypedPage<T>);
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel>>> ListLanguageVariantsByCollectionAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var collectionSegment = identifier.ToUrlSegment();
        return PageEnumerator.CollectAsync<LanguageVariantsListingResponseServerModel, LanguageVariantModel>(
            (token, ct) => ManagementApi.ListLanguageVariantsByCollectionInternalAsync(collectionSegment, token, ct),
            page => page.Variants,
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<LanguageVariantModel>>> ListLanguageVariantsByCollectionPageAsync(Reference identifier, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsByCollectionInternalAsync(identifier.ToUrlSegment(), continuationToken, cancellationToken)
            .ToManagementResultAsync(page => new ListingPage<LanguageVariantModel>
            {
                Items = page.Variants,
                ContinuationToken = page.Pagination?.Token,
            });
    }

    /// <inheritdoc />
    public Task<IManagementResult<IReadOnlyList<LanguageVariantModel>>> ListLanguageVariantsBySpaceAsync(Reference identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        var spaceSegment = identifier.ToUrlSegment();
        return PageEnumerator.CollectAsync<LanguageVariantsListingResponseServerModel, LanguageVariantModel>(
            (token, ct) => ManagementApi.ListLanguageVariantsBySpaceInternalAsync(spaceSegment, token, ct),
            page => page.Variants,
            page => page.Pagination?.Token,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IManagementResult<ListingPage<LanguageVariantModel>>> ListLanguageVariantsBySpacePageAsync(Reference identifier, string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.ListLanguageVariantsBySpaceInternalAsync(identifier.ToUrlSegment(), continuationToken, cancellationToken)
            .ToManagementResultAsync(page => new ListingPage<LanguageVariantModel>
            {
                Items = page.Variants,
                ContinuationToken = page.Pagination?.Token,
            });
    }

    /// <inheritdoc />
    public Task<IManagementResult<LanguageVariantModel>> GetLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<LanguageVariantModel<T>>> GetLanguageVariantAsync<T>(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync(ToTypedVariant<T>);
    }

    /// <inheritdoc />
    public Task<IManagementResult<LanguageVariantModel>> GetPublishedLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetPublishedLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<LanguageVariantModel<T>>> GetPublishedLanguageVariantAsync<T>(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.GetPublishedLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync(ToTypedVariant<T>);
    }

    /// <inheritdoc />
    public Task<IManagementResult<LanguageVariantModel>> UpsertLanguageVariantAsync(LanguageVariantIdentifier identifier, LanguageVariantUpsertModel languageVariantUpsertModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(languageVariantUpsertModel);

        return ManagementApi.UpsertLanguageVariantInternalAsync(identifier.ToUrlSegment(), languageVariantUpsertModel, cancellationToken).ToManagementResultAsync();
    }

    /// <inheritdoc />
    public Task<IManagementResult<LanguageVariantModel<T>>> UpsertLanguageVariantAsync<T>(
        LanguageVariantIdentifier identifier,
        T variant,
        WorkflowStepIdentifier? workflow = null,
        CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(variant);

        var upsertModel = new LanguageVariantUpsertModel
        {
            Elements = _contentConverter.ToElements(variant),
            Workflow = workflow,
        };

        return ManagementApi.UpsertLanguageVariantInternalAsync(identifier.ToUrlSegment(), upsertModel, cancellationToken).ToManagementResultAsync(ToTypedVariant<T>);
    }

    /// <inheritdoc />
    public Task<IManagementResult> DeleteLanguageVariantAsync(LanguageVariantIdentifier identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        return ManagementApi.DeleteLanguageVariantInternalAsync(identifier.ToUrlSegment(), cancellationToken).ToManagementResultAsync();
    }

    // Projects a fetched variant onto the typed wrapper: raw elements become the generated record, the variant
    // metadata (item, language, workflow, …) that the response carries is preserved rather than discarded.
    internal LanguageVariantModel<T> ToTypedVariant<T>(LanguageVariantModel variant) where T : IElementsModel, new()
        => LanguageVariantModel<T>.From(variant, ProjectElements<T>(variant.Elements));

    private IReadOnlyList<LanguageVariantModel<T>> ToTypedVariants<T>(IReadOnlyList<LanguageVariantModel> variants) where T : IElementsModel, new()
        => [.. variants.Select(ToTypedVariant<T>)];

    private ListingPage<LanguageVariantModel<T>> ToTypedPage<T>(LanguageVariantsListingResponseServerModel page) where T : IElementsModel, new()
        => new()
        {
            Items = ToTypedVariants<T>(page.Variants),
            ContinuationToken = page.Pagination?.Token,
        };

    // Projects a variant's element envelopes into a generated record via the content converter.
    private T ProjectElements<T>(IReadOnlyList<BaseElement> elements) where T : IElementsModel, new()
    {
        if (_autoScanContentTypes)
        {
            _contentConverter.Registry.Scan(typeof(T).Assembly);
        }

        return _contentConverter.ReadEnvelopes<T>(elements);
    }
}
