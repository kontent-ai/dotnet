using Kontent.Ai.Sync.Abstractions;

namespace Kontent.Ai.Sync.Configuration;

/// <summary>
/// A builder of <see cref="SyncOptions"/> instances.
/// </summary>
public class SyncOptionsBuilder : ISyncOptionsBuilder
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
    public ISyncOptionsBuilder UsePreviewApi(string apiKey)
    {
        _options.ApiMode = ApiMode.Preview;
        _options.ApiKey = apiKey;
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder UseSecureApi(string apiKey)
    {
        _options.ApiMode = ApiMode.Secure;
        _options.ApiKey = apiKey;
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder DisableRetryPolicy()
    {
        _options.EnableResilience = false;
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder WithCustomEndpoint(string endpoint)
    {
        SetCustomEndpoint(endpoint);
        return this;
    }

    /// <inheritdoc/>
    public ISyncOptionsBuilder WithCustomEndpoint(Uri endpoint)
    {
        SetCustomEndpoint(endpoint.AbsoluteUri);
        return this;
    }

    private void SetCustomEndpoint(string endpoint)
    {
        if (_options.ApiMode == ApiMode.Preview)
        {
            _options.PreviewEndpoint = endpoint;
        }
        else
        {
            _options.ProductionEndpoint = endpoint;
        }
    }

    /// <inheritdoc/>
    public SyncOptions Build() => new()
    {
        EnvironmentId = _options.EnvironmentId,
        EnableResilience = _options.EnableResilience,
        ProductionEndpoint = _options.ProductionEndpoint,
        PreviewEndpoint = _options.PreviewEndpoint,
        ApiKey = _options.ApiKey,
        ApiMode = _options.ApiMode
    };
}
