namespace Kontent.Ai.Sync;

/// <summary>
/// A builder of <see cref="SyncOptions"/> instances.
/// </summary>
public interface ISyncOptionsBuilder
{
    /// <summary>
    /// Sets the environment ID.
    /// </summary>
    /// <param name="environmentId">The identifier of a Kontent.ai environment.</param>
    ISyncOptionsBuilder WithEnvironmentId(string environmentId);

    /// <summary>
    /// Sets the environment ID.
    /// </summary>
    /// <param name="environmentId">The identifier of a Kontent.ai environment.</param>
    ISyncOptionsBuilder WithEnvironmentId(Guid environmentId);

    /// <summary>
    /// Configures for public Production API (no authentication).
    /// </summary>
    ISyncOptionsBuilder UseProductionApi();

    /// <summary>
    /// Configures for secure Production API with authentication.
    /// </summary>
    /// <param name="secureAccessApiKey">A Delivery API key with secure access enabled.</param>
    ISyncOptionsBuilder UseProductionApi(string secureAccessApiKey);

    /// <summary>
    /// Configures for Preview API with authentication.
    /// </summary>
    /// <param name="apiKey">A Preview API key.</param>
    ISyncOptionsBuilder UsePreviewApi(string apiKey);

    /// <summary>
    /// Configures for secure Production API with authentication.
    /// </summary>
    /// <param name="apiKey">A delivery API key.</param>
    [Obsolete("Use UseProductionApi(secureAccessApiKey) instead, matching the Delivery SDK. Removed in 3.0.")]
    ISyncOptionsBuilder UseSecureApi(string apiKey);

    /// <summary>
    /// Disables retry policy for HTTP requests.
    /// </summary>
    ISyncOptionsBuilder DisableRetryPolicy();

    /// <summary>
    /// Sets the ceiling on one call, covering every retry attempt and the waits between them.
    /// </summary>
    /// <param name="timeout">The ceiling, or <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> for none.</param>
    ISyncOptionsBuilder WithTimeout(TimeSpan timeout);

    /// <summary>
    /// Uses a custom endpoint for the Production or Preview API.
    /// </summary>
    /// <param name="endpoint">A custom endpoint URL.</param>
    ISyncOptionsBuilder WithCustomEndpoint(string endpoint);

    /// <summary>
    /// Uses a custom endpoint for the Production or Preview API.
    /// </summary>
    /// <param name="endpoint">A custom endpoint URI.</param>
    ISyncOptionsBuilder WithCustomEndpoint(Uri endpoint);

    /// <summary>
    /// Returns a new instance of the <see cref="SyncOptions"/> class.
    /// </summary>
    SyncOptions Build();
}
