using Kontent.Ai.Sync.Abstractions;
using Kontent.Ai.Sync.Api;
using Kontent.Ai.Sync.Extensions;
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
}
