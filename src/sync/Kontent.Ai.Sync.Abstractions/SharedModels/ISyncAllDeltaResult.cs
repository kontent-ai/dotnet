namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents the result of fetching all delta updates with automatic pagination.
/// </summary>
public interface ISyncAllDeltaResult
{
    /// <summary>
    /// Gets all delta responses retrieved during pagination.
    /// </summary>
    IReadOnlyList<ISyncDeltaResponse> Responses { get; }

    /// <summary>
    /// Gets the final synchronization token after all pages have been retrieved.
    /// Use this token for the next sync operation.
    /// </summary>
    string FinalSyncToken { get; }

    /// <summary>
    /// Gets a value indicating whether all operations were successful.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// Gets the error that occurred during pagination, if any.
    /// </summary>
    IError? Error { get; }

    /// <summary>
    /// Gets the number of pages fetched during the operation.
    /// </summary>
    int PagesFetched { get; }

    /// <summary>
    /// Gets a value indicating whether pagination was stopped due to reaching the maximum page limit.
    /// </summary>
    bool WasLimitedByMaxPages { get; }
}
