using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Extensions;
using Kontent.Ai.Sync.SharedModels;

namespace Kontent.Ai.Sync;

/// <summary>
/// Executes requests against the Kontent.ai Sync API.
/// </summary>
/// <param name="syncApi">The Refit-generated API client.</param>
/// <param name="environmentId">The environment identifier.</param>
internal sealed class SyncClient(
    ISyncApi syncApi,
    string environmentId) : ISyncClient
{
    private readonly ISyncApi _syncApi = syncApi ?? throw new ArgumentNullException(nameof(syncApi));
    private readonly string _environmentId = !string.IsNullOrWhiteSpace(environmentId)
        ? environmentId
        : throw new ArgumentException("Environment ID cannot be null or empty.", nameof(environmentId));

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncInitResponse>> InitializeSyncAsync(CancellationToken cancellationToken = default)
    {
        var rawResponse = await _syncApi.InitializeSyncAsync(_environmentId, cancellationToken)
            .ConfigureAwait(false);

        return await rawResponse.ToSyncResultAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ISyncResult<ISyncDeltaResponse>> GetDeltaAsync(string syncToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken);

        var rawResponse = await _syncApi.GetDeltaAsync(_environmentId, syncToken, cancellationToken)
            .ConfigureAwait(false);

        return await rawResponse.ToSyncResultAsync()
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ISyncAllDeltaResult> GetAllDeltaAsync(string syncToken, int? maxPages = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncToken);

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
