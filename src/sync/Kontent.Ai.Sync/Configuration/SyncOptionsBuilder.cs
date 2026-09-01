using Kontent.Ai.Common;

namespace Kontent.Ai.Sync.Configuration;

/// <summary>
/// A builder of <see cref="SyncOptions"/> instances.
/// </summary>
public sealed class SyncOptionsBuilder : ISyncOptionsBuilder
{
    private readonly SyncOptions _options = new();

    private SyncOptionsBuilder() { }

    /// <summary>
    /// Creates a new instance of the <see cref="SyncOptionsBuilder"/> class.
    /// </summary>
    public static ISyncOptionsBuilder CreateInstance() => new SyncOptionsBuilder();

    /// <inheritdoc/>
    public ISyncOptionsBuilder WithEnvironmentId(string environmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        _options.EnvironmentId = environmentId;
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder WithEnvironmentId(Guid environmentId)
    {
        _options.EnvironmentId = environmentId.ToString();
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder UseProductionApi()
    {
        _options.ApiMode = ApiMode.Public;
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder UseProductionApi(string secureAccessApiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secureAccessApiKey);
        _options.ApiMode = ApiMode.Secure;
        _options.ApiKey = secureAccessApiKey;
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder UsePreviewApi(string apiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _options.ApiMode = ApiMode.Preview;
        _options.ApiKey = apiKey;
        return this;
    }

    /// <inheritdoc/>
    [Obsolete("Use UseProductionApi(secureAccessApiKey) instead, matching the Delivery SDK. Removed in 3.0.")]
    public ISyncOptionsBuilder UseSecureApi(string apiKey) => UseProductionApi(apiKey);

    /// <inheritdoc/>
    public ISyncOptionsBuilder DisableRetryPolicy()
    {
        _options.EnableResilience = false;
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder WithCustomEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        SetCustomEndpoint(endpoint);
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder WithCustomEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        SetCustomEndpoint(endpoint.AbsoluteUri);
        return this;
    }

    private void SetCustomEndpoint(string endpoint)
    {
        // Apply to both endpoints so behavior is deterministic regardless of call order.
        _options.PreviewEndpoint = endpoint;
        _options.ProductionEndpoint = endpoint;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Reflected rather than listed property by property: a hand-written list keeps compiling when a new
    /// option is added and silently stops carrying it, so a value the caller set never reaches the client.
    /// </remarks>
    public SyncOptions Build()
    {
        var built = new SyncOptions();
        OptionsCopier<SyncOptions>.Copy(_options, built);

        return built;
    }
}
