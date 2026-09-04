using Kontent.Ai.Management.Conversion;
using Kontent.Ai.Management.Models.Assets;
using Kontent.Ai.Management.Models.Items;
using Kontent.Ai.Management.Models.LanguageVariants;
using Kontent.Ai.Management.Models.Workflow;

namespace Kontent.Ai.Management.Extensions;

/// <summary>
/// Extra simplifying methods available for <see cref="IManagementClient"/>.
/// </summary>
public static class ManagementClientExtensions
{
    /// <summary>
    /// Projects a fetched variant onto the generated content-type record <typeparamref name="T"/>: the element
    /// envelopes become the record, and the variant metadata is carried across. For a listing whose variants span
    /// several content types - by collection or by space - this is how each is typed once its type is known; the
    /// listings whose variants share one type have typed overloads of their own.
    /// </summary>
    /// <remarks>
    /// Typed read is environment-bound — see <see cref="IManagementClient.GetLanguageVariantAsync{T}(LanguageVariantIdentifier, CancellationToken)"/>.
    /// The projection resolves rich-text component types through the client's content-type registry; on an
    /// <see cref="IManagementClient"/> that is not the SDK's, it scans <typeparamref name="T"/>'s assembly itself.
    /// </remarks>
    /// <typeparam name="T">The generated content-type record (implements <see cref="IElementsModel"/>).</typeparam>
    /// <param name="client">The client whose content-type registry resolves component types.</param>
    /// <param name="variant">The variant to project, as returned by any untyped variant call.</param>
    /// <returns>The variant with its elements as <typeparamref name="T"/>.</returns>
    /// <exception cref="InvalidOperationException">No element of <paramref name="variant"/> matches <typeparamref name="T"/>, or a component's type is not registered.</exception>
    public static LanguageVariantModel<T> ToTyped<T>(this IManagementClient client, LanguageVariantModel variant)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(variant);

        if (client is ManagementClient managementClient)
        {
            return managementClient.ToTypedVariant<T>(variant);
        }

        // A client that is not the SDK's has no registry to lend; one of this method's own, scanning the record's
        // assembly the way a default client does, stands in.
        var converter = FallbackConverter.Value;
        converter.Registry.Scan(typeof(T).Assembly);
        return LanguageVariantModel<T>.From(variant, converter.ReadEnvelopes<T>(variant.Elements));
    }

    private static readonly Lazy<ContentItemEnvelopeConverter> FallbackConverter = new(() => new ContentItemEnvelopeConverter());

    /// <summary>
    /// Creates or updates the content item from a fetched <see cref="ContentItemModel"/> — the server-owned
    /// metadata is dropped. Addressing by external id creates the item when it does not exist yet.
    /// </summary>
    /// <param name="client">Content management client instance.</param>
    /// <param name="identifier">Identifies which content item will be created or updated.</param>
    /// <param name="contentItem">The fetched (and possibly modified) content item to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A result wrapping the created or updated <see cref="ContentItemModel"/> on success, or the failure detail.</returns>
    public static async Task<IManagementResult<ContentItemModel>> UpsertContentItemAsync(this IManagementClient client, Reference identifier, ContentItemModel contentItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(contentItem);

        var contentItemUpdateModel = new ContentItemUpsertModel
        {
            Name = contentItem.Name,
            Codename = contentItem.Codename,
            Collection = contentItem.Collection,
            SitemapLocations = contentItem.SitemapLocations,
            Type = contentItem.Type
        };

        return await client.UpsertContentItemAsync(identifier, contentItemUpdateModel, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates or updates the language variant from a fetched <see cref="LanguageVariantModel"/> — its elements,
    /// workflow, due date, note, and contributors feed the upsert; the server-owned metadata is dropped.
    /// </summary>
    /// <param name="client">Content management client instance.</param>
    /// <param name="identifier">The identifier of the language variant.</param>
    /// <param name="languageVariant">The fetched (and possibly modified) variant to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A result wrapping the inserted or updated <see cref="LanguageVariantModel"/> on success, or the failure detail.</returns>
    public static async Task<IManagementResult<LanguageVariantModel>> UpsertLanguageVariantAsync(this IManagementClient client, LanguageVariantIdentifier identifier, LanguageVariantModel languageVariant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(languageVariant);

        var upsertModel = new LanguageVariantUpsertModel
        {
            Elements = languageVariant.Elements,
            Workflow = languageVariant.Workflow,
            DueDate = languageVariant.DueDate,
            Note = languageVariant.Note,
            Contributors = languageVariant.Contributors,
        };

        return await client.UpsertLanguageVariantAsync(identifier, upsertModel, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads the file and creates an asset that references it.
    /// </summary>
    /// <param name="client">Content management client instance.</param>
    /// <param name="fileContent">Represents the content of the file.</param>
    /// <param name="createModel">Builds the asset to create from the reference of the uploaded file.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A result wrapping the created asset, or the failure detail of the file upload or the create.</returns>
    public static async Task<IManagementResult<AssetModel>> CreateAssetAsync(this IManagementClient client, FileContentSource fileContent, Func<FileReference, AssetCreateModel> createModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fileContent);
        ArgumentNullException.ThrowIfNull(createModel);

        var fileResult = await client.UploadFileAsync(fileContent, cancellationToken).ConfigureAwait(false);
        if (!fileResult.IsSuccess)
        {
            return fileResult.AsFailure<AssetModel>();
        }

        return await client.CreateAssetAsync(createModel(fileResult.Value), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Uploads the file and creates or updates the asset that references it.
    /// </summary>
    /// <param name="client">Content management client instance.</param>
    /// <param name="identifier">The identifier of the asset.</param>
    /// <param name="fileContent">Represents the content of the file.</param>
    /// <param name="upsertModel">Values for the upserted asset; its <see cref="AssetUpsertModel.FileReference"/> is set from the uploaded file.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A result wrapping the upserted asset, or the failure detail of the file upload or the upsert.</returns>
    public static async Task<IManagementResult<AssetModel>> UpsertAssetAsync(this IManagementClient client, Reference identifier, FileContentSource fileContent, AssetUpsertModel upsertModel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(fileContent);
        ArgumentNullException.ThrowIfNull(upsertModel);

        var fileResult = await client.UploadFileAsync(fileContent, cancellationToken).ConfigureAwait(false);
        if (!fileResult.IsSuccess)
        {
            return fileResult.AsFailure<AssetModel>();
        }

        return await client.UpsertAssetAsync(identifier, upsertModel with { FileReference = fileResult.Value }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a content item and upserts one of its language variants in a single call.
    /// </summary>
    /// <remarks>
    /// On a partial failure — the item is created but the variant upsert fails — the created item is left in place
    /// (no rollback) and the returned failure carries the variant call's detail. Set
    /// <see cref="ContentItemCreateModel.ExternalId"/> so a retry reuses the same item instead of creating a duplicate.
    /// </remarks>
    /// <param name="client">Content management client instance.</param>
    /// <param name="item">The content item to create.</param>
    /// <param name="language">The language of the variant to upsert on the created item.</param>
    /// <param name="variant">The variant data to upsert.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A result wrapping the upserted variant, or the failure detail of the item creation or the variant upsert.</returns>
    public static async Task<IManagementResult<LanguageVariantModel>> CreateContentItemWithVariantAsync(this IManagementClient client, ContentItemCreateModel item, Reference language, LanguageVariantUpsertModel variant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(variant);

        var itemResult = await client.CreateContentItemAsync(item, cancellationToken).ConfigureAwait(false);
        if (!itemResult.IsSuccess)
        {
            return itemResult.AsFailure<LanguageVariantModel>();
        }

        var identifier = new LanguageVariantIdentifier(Reference.ById(itemResult.Value.Id), language);
        return await client.UpsertLanguageVariantAsync(identifier, variant, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a content item and upserts one of its language variants from the generated content-type record
    /// <typeparamref name="T"/> in a single call.
    /// </summary>
    /// <remarks>
    /// On a partial failure — the item is created but the variant upsert fails — the created item is left in place
    /// (no rollback) and the returned failure carries the variant call's detail. Set
    /// <see cref="ContentItemCreateModel.ExternalId"/> so a retry reuses the same item instead of creating a duplicate.
    /// </remarks>
    /// <typeparam name="T">The generated content-type record (implements <see cref="IElementsModel"/>).</typeparam>
    /// <param name="client">Content management client instance.</param>
    /// <param name="item">The content item to create.</param>
    /// <param name="language">The language of the variant to upsert on the created item.</param>
    /// <param name="variant">The content-type record carrying the elements to set.</param>
    /// <param name="workflow">Optional workflow step to set on the variant.</param>
    /// <param name="cancellationToken">Token to cancel the request.</param>
    /// <returns>A result wrapping the upserted variant — its element values as <typeparamref name="T"/> plus the item,
    /// language, workflow and other variant metadata — or the failure detail of the item creation or the variant upsert.</returns>
    public static async Task<IManagementResult<LanguageVariantModel<T>>> CreateContentItemWithVariantAsync<T>(this IManagementClient client, ContentItemCreateModel item, Reference language, T variant, WorkflowStepIdentifier? workflow = null, CancellationToken cancellationToken = default)
        where T : IElementsModel, new()
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(variant);

        var itemResult = await client.CreateContentItemAsync(item, cancellationToken).ConfigureAwait(false);
        if (!itemResult.IsSuccess)
        {
            return itemResult.AsFailure<LanguageVariantModel<T>>();
        }

        var identifier = new LanguageVariantIdentifier(Reference.ById(itemResult.Value.Id), language);
        return await client.UpsertLanguageVariantAsync(identifier, variant, workflow, cancellationToken).ConfigureAwait(false);
    }

}
