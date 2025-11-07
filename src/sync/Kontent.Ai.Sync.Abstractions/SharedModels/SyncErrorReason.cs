namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Specifies the reason for a sync operation failure.
/// </summary>
public enum SyncErrorReason
{
    /// <summary>
    /// Unknown or unspecified error.
    /// </summary>
    Unknown,

    /// <summary>
    /// The API response was invalid or could not be parsed.
    /// </summary>
    InvalidResponse,

    /// <summary>
    /// The requested resource was not found (404).
    /// </summary>
    NotFound,

    /// <summary>
    /// Authentication failed or credentials are invalid (401/403).
    /// </summary>
    Unauthorized,

    /// <summary>
    /// Rate limit exceeded - too many requests (429).
    /// </summary>
    RateLimited,

    /// <summary>
    /// The request timed out.
    /// </summary>
    Timeout,

    /// <summary>
    /// A network error occurred (connection failed, DNS resolution failed, etc.).
    /// </summary>
    NetworkError,

    /// <summary>
    /// The sync token is invalid or expired.
    /// </summary>
    InvalidSyncToken,

    /// <summary>
    /// The SDK configuration is invalid.
    /// </summary>
    InvalidConfiguration,

    /// <summary>
    /// The server returned an internal error (500).
    /// </summary>
    ServerError
}
