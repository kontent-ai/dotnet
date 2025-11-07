namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Specifies the API mode for accessing Kontent.ai content.
/// </summary>
public enum ApiMode
{
    /// <summary>
    /// Public production API - no authentication required.
    /// Tracks published content only.
    /// </summary>
    Public,

    /// <summary>
    /// Preview API - requires API key authentication.
    /// Tracks all content including unpublished items and workflow changes.
    /// </summary>
    Preview,

    /// <summary>
    /// Secure production API - requires delivery API key authentication.
    /// Tracks published content only, but with authenticated access.
    /// </summary>
    Secure
}
