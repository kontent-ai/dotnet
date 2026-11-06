using System.Text.Json.Serialization;

namespace Kontent.Ai.Sync.SharedModels;

/// <summary>
/// Represents an error response from Kontent.ai Sync API.
/// </summary>
internal sealed record Error : IError
{
    /// <inheritdoc/>
    // Required on the wire so that a JSON body which is not the API's envelope - a proxy's or the CDN's - fails
    // to parse and is reported with its status and raw body, rather than as an envelope with nothing in it.
    [JsonRequired]
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
