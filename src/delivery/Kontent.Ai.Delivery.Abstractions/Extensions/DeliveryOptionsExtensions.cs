namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// A class which contains extension methods on <see cref="DeliveryOptions"/>.
/// </summary>
public static class DeliveryOptionsExtensions
{
    /// <summary>
    /// Production API, published content, no authentication.
    /// </summary>
    public static DeliveryOptions UseProductionApi(this DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.UsePreviewApi = false;
        options.UseSecureAccess = false;
        return options;
    }

    /// <summary>
    /// Production API with secure access, authenticated with a Delivery API key that has secure access
    /// enabled.
    /// </summary>
    public static DeliveryOptions UseProductionApi(this DeliveryOptions options, string secureAccessApiKey)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(secureAccessApiKey);
        options.UsePreviewApi = false;
        options.UseSecureAccess = true;
        options.SecureAccessApiKey = secureAccessApiKey;
        return options;
    }

    /// <summary>
    /// Preview API, all content, authenticated with a Preview API key.
    /// </summary>
    public static DeliveryOptions UsePreviewApi(this DeliveryOptions options, string previewApiKey)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(previewApiKey);
        options.UsePreviewApi = true;
        options.UseSecureAccess = false;
        options.PreviewApiKey = previewApiKey;
        return options;
    }

    /// <summary>
    /// Sends every request to <paramref name="endpoint"/>, whichever API is active.
    /// </summary>
    public static DeliveryOptions UseCustomEndpoint(this DeliveryOptions options, string endpoint)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        options.ProductionEndpoint = endpoint;
        options.PreviewEndpoint = endpoint;
        return options;
    }

    /// <summary>
    /// Sends every request to <paramref name="endpoint"/>, whichever API is active.
    /// </summary>
    public static DeliveryOptions UseCustomEndpoint(this DeliveryOptions options, Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return options.UseCustomEndpoint(endpoint.AbsoluteUri);
    }

    /// <summary>
    /// Gets the base URL for the delivery API.
    /// </summary>
    /// <param name="options">The delivery options.</param>
    /// <returns>The base URL for the delivery API.</returns>
    public static string GetBaseUrl(this DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UsePreviewApi ? options.PreviewEndpoint : options.ProductionEndpoint;
    }

    /// <summary>
    /// Gets the API key for the delivery API.
    /// </summary>
    /// <param name="options">The delivery options.</param>
    /// <returns>The API key for the delivery API.</returns>
    public static string? GetApiKey(this DeliveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options switch
        {
            { UseSecureAccess: true, SecureAccessApiKey: var key } => key,
            { UsePreviewApi: true, PreviewApiKey: var key } => key,
            _ => null
        };
    }
}
