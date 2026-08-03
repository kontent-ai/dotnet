namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Helper methods for resolving effective Sync API options.
/// </summary>
public static class SyncOptionsExtensions
{
    /// <summary>
    /// Gets the effective base URL for the configured API mode.
    /// </summary>
    /// <param name="options">Sync options.</param>
    /// <returns>Configured base URL.</returns>
    public static string GetBaseUrl(this SyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ApiMode == ApiMode.Preview
            ? options.PreviewEndpoint
            : options.ProductionEndpoint;
    }

    /// <summary>
    /// Gets the effective API key for the configured API mode.
    /// </summary>
    /// <param name="options">Sync options.</param>
    /// <returns>API key if required by the mode; otherwise null.</returns>
    public static string? GetApiKey(this SyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.ApiMode is ApiMode.Preview or ApiMode.Secure
            ? options.ApiKey
            : null;
    }
}
