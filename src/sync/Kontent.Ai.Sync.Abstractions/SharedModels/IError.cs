namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents an error response from Kontent.ai Sync API.
/// </summary>
public interface IError
{
    /// <summary>
    /// Gets the error message.
    /// </summary>
    string Message { get; }

    /// <summary>
    /// Gets the ID of a request that can be used for troubleshooting.
    /// </summary>
    string? RequestId { get; }

    /// <summary>
    /// Gets Kontent.ai Sync API error code.
    /// </summary>
    int? ErrorCode { get; }

    /// <summary>
    /// Gets specific code of error.
    /// </summary>
    int? SpecificCode { get; }
}
