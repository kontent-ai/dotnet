using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Extensions;
using Kontent.Ai.Sync.SharedModels;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Sync;

/// <summary>
/// Executes requests against the Kontent.ai Sync API.
/// </summary>
/// <param name="syncApi">The Refit-generated API client.</param>
/// <param name="syncOptions">The settings of the Kontent.ai environment.</param>
internal sealed class SyncClient(
    ISyncApi syncApi,
    IOptionsMonitor<SyncOptions> syncOptions) : ISyncClient
{
    private readonly ISyncApi _syncApi = syncApi ?? throw new ArgumentNullException(nameof(syncApi));
    private readonly IOptionsMonitor<SyncOptions> _syncOptions = syncOptions ?? throw new ArgumentNullException(nameof(syncOptions));

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncInitResponse>> InitializeSyncAsync(SyncInitOptions? options = null, CancellationToken cancellationToken = default)
    {
        var environmentId = _syncOptions.CurrentValue.EnvironmentId;

        // Extract filter parameters from options
        var contentTypes = options?.ContentTypes;
        var collections = options?.Collections;
        var systemLanguage = options?.Language;
        var language = (options?.IgnoreLanguageFallbacks == true && !string.IsNullOrWhiteSpace(options.Language))
            ? options.Language
            : null;

        var rawResponse = await _syncApi.InitializeSyncAsync(
            environmentId,
            contentTypes,
            collections,
            systemLanguage,
            language,
            cancellationToken)
            .ConfigureAwait(false);

        return await rawResponse.ToSyncResultAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken, nameof(syncToken));

        var environmentId = _syncOptions.CurrentValue.EnvironmentId;
        var rawResponse = await _syncApi.GetDeltaAsync(environmentId, syncToken, cancellationToken)
            .ConfigureAwait(false);

        return await rawResponse.ToSyncResultAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ISyncAllDeltaResult> GetAllDeltaAsync(string syncToken, int? maxPages = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken, nameof(syncToken));

        if (maxPages.HasValue && maxPages.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages), "Maximum pages must be greater than zero.");
        }

        var responses = new List<ISyncDeltaResponse>();
        var currentToken = syncToken;
        var pagesFetched = 0;
        var wasLimitedByMaxPages = false;

        while (true)
        {
            // Check cancellation
            cancellationToken.ThrowIfCancellationRequested();

            // Fetch next page
            var result = await GetDeltaAsync(currentToken, cancellationToken).ConfigureAwait(false);

            // Handle errors
            if (!result.IsSuccess)
            {
                return new SyncAllDeltaResult(
                    responses.AsReadOnly(),
                    currentToken,
                    pagesFetched,
                    result.Error!);
            }

            // Add response and increment counter
            responses.Add(result.Value);
            pagesFetched++;
            currentToken = result.SyncToken ?? currentToken;

            // Check if more pages available
            if (!result.HasMoreChanges)
            {
                break;
            }

            // Check max pages limit
            if (maxPages.HasValue && pagesFetched >= maxPages.Value)
            {
                wasLimitedByMaxPages = true;
                break;
            }
        }

        return new SyncAllDeltaResult(
            responses.AsReadOnly(),
            currentToken,
            pagesFetched,
            wasLimitedByMaxPages);
    }
}
