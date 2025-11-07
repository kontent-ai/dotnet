using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.SharedModels;

/// <summary>
/// Implementation of <see cref="ISyncAllDeltaResult"/> for automatic pagination results.
/// </summary>
internal sealed class SyncAllDeltaResult : ISyncAllDeltaResult
{
    /// <inheritdoc/>
    public IReadOnlyList<ISyncDeltaResponse> Responses { get; }

    /// <inheritdoc/>
    public string FinalSyncToken { get; }

    /// <inheritdoc/>
    public bool IsSuccess { get; }

    /// <inheritdoc/>
    public IError? Error { get; }

    /// <inheritdoc/>
    public int PagesFetched { get; }

    /// <inheritdoc/>
    public bool WasLimitedByMaxPages { get; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    internal SyncAllDeltaResult(
        IReadOnlyList<ISyncDeltaResponse> responses,
        string finalSyncToken,
        int pagesFetched,
        bool wasLimitedByMaxPages)
    {
        Responses = responses;
        FinalSyncToken = finalSyncToken;
        IsSuccess = true;
        PagesFetched = pagesFetched;
        WasLimitedByMaxPages = wasLimitedByMaxPages;
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    internal SyncAllDeltaResult(
        IReadOnlyList<ISyncDeltaResponse> responses,
        string finalSyncToken,
        int pagesFetched,
        IError error)
    {
        Responses = responses;
        FinalSyncToken = finalSyncToken;
        IsSuccess = false;
        Error = error;
        PagesFetched = pagesFetched;
        WasLimitedByMaxPages = false;
    }
}
