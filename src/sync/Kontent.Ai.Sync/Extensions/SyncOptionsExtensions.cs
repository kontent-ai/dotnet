namespace Kontent.Ai.Sync;

/// <summary>
/// Configures <see cref="SyncOptions"/> the way the API is accessed, and resolves the effective values.
/// </summary>
public static class SyncOptionsExtensions
{
    /// <summary>
    /// Public Production API: published content, no authentication.
    /// </summary>
    public static SyncOptions UseProductionApi(this SyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ApiMode = ApiMode.Public;
        return options;
    }

    /// <summary>
    /// Secure Production API: published content, authenticated with a Delivery API key that has secure
    /// access enabled.
    /// </summary>
    public static SyncOptions UseProductionApi(this SyncOptions options, string secureAccessApiKey)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(secureAccessApiKey);
        options.ApiMode = ApiMode.Secure;
        options.ApiKey = secureAccessApiKey;
        return options;
    }

    /// <summary>
    /// Preview API: all content, authenticated with a Preview API key.
    /// </summary>
    public static SyncOptions UsePreviewApi(this SyncOptions options, string previewApiKey)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewApiKey);
        options.ApiMode = ApiMode.Preview;
        options.ApiKey = previewApiKey;
        return options;
    }

    /// <summary>
    /// Sends every request to <paramref name="endpoint"/>, whichever API mode is active.
    /// </summary>
    public static SyncOptions UseCustomEndpoint(this SyncOptions options, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        options.ProductionEndpoint = endpoint;
        options.PreviewEndpoint = endpoint;
        return options;
    }

    /// <summary>
    /// Sends every request to <paramref name="endpoint"/>, whichever API mode is active.
    /// </summary>
    public static SyncOptions UseCustomEndpoint(this SyncOptions options, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return options.UseCustomEndpoint(endpoint.AbsoluteUri);
    }

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
