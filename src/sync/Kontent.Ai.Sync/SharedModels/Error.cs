using System.Text.Json.Serialization;
using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.SharedModels;

/// <summary>
/// Represents an error response from Kontent.ai Sync API.
/// </summary>
internal sealed record Error : IError
{
    /// <inheritdoc/>
    [JsonPropertyName("message")]
    public string Message { get; init; } = "Unknown error";

    /// <inheritdoc/>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <inheritdoc/>
    [JsonPropertyName("error_code")]
    public int? ErrorCode { get; init; }

    /// <inheritdoc/>
    [JsonPropertyName("specific_code")]
    public int? SpecificCode { get; init; }

    /// <inheritdoc/>
    [JsonIgnore]
    public Exception? Exception { get; init; }
}
