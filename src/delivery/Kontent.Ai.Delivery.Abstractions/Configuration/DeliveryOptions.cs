using System.ComponentModel.DataAnnotations;
using Kontent.Ai.Common;

namespace Kontent.Ai.Delivery.Abstractions;

/// <summary>
/// Represents configuration of the <see cref="IDeliveryClient"/>.
/// </summary>
public sealed class DeliveryOptions : IValidatableObject
{
    /// <summary>
    /// The configuration section name <c>AddDeliveryClient</c> binds by default. Design-time tools that
    /// resolve the SDK's configuration from the same sources should probe this section.
    /// </summary>
    public const string DefaultConfigurationSectionName = "DeliveryOptions";

    /// <summary>
    /// Gets or sets the environment ID.
    /// </summary>
    [Required]
    public string EnvironmentId { get; set; } = Guid.Empty.ToString();

    /// <summary>
    /// Gets or sets a value that determines if the client uses resilience policies.
    /// This setting is evaluated once when the HTTP pipeline is constructed at startup
    /// and cannot be changed at runtime.
    /// </summary>
    public bool EnableResilience { get; set; } = true;

    /// <summary>
    /// Gets or sets the ceiling on one call, covering every retry attempt and the waits between them.
    /// Leave unset to let the resilience pipeline own timing.
    /// </summary>
    /// <remarks>
    /// Unset, the SDK's own pipeline bounds each attempt and the call runs as long as its retries need;
    /// with resilience disabled or a pipeline of your own, <see cref="HttpClient"/>'s 100-second default
    /// applies, since nothing else is known to bound the request. A value set here always wins, and
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> removes the ceiling outright.
    /// <para>
    /// The ceiling outranks <c>Retry-After</c>: the server's backoff is honoured in full until the budget
    /// runs out, at which point the call is cut short.
    /// </para>
    /// </remarks>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the format of the Production API endpoint address.
    /// </summary>
    [Url]
    public string ProductionEndpoint { get; set; } = "https://deliver.kontent.ai";

    /// <summary>
    /// Gets or sets the format of the Preview API endpoint address.
    /// </summary>
    [Url]
    public string PreviewEndpoint { get; set; } = "https://preview-deliver.kontent.ai";

    /// <summary>
    /// Gets or sets the API key that is used to retrieve content with the Preview API.
    /// </summary>
    [RequiredIf(nameof(UsePreviewApi), true, ErrorMessage = "PreviewApiKey is required when using the Preview API.")]
    [RegularExpression(@"[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+", ErrorMessage = "The Preview API key must be a valid API key.")]
    public string? PreviewApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value that determines if the Preview API is used to retrieve content.
    /// If the Preview API is used the <see cref="PreviewApiKey"/> must be set.
    /// </summary>
    public bool UsePreviewApi { get; set; } = false;

    /// <summary>
    /// Gets or sets a value that determines if the client sends the secure access API key to retrieve content with the Production API.
    /// This key is required to retrieve content when secure access is enabled.
    /// To retrieve content when secure access is enabled the <see cref="SecureAccessApiKey"/> must be set.
    /// </summary>
    public bool UseSecureAccess { get; set; } = false;

    /// <summary>
    /// Gets or sets the API key that is used to retrieve content with the Production API when secure access is enabled.
    /// </summary>
    [RequiredIf(nameof(UseSecureAccess), true, ErrorMessage = "SecureAccessApiKey is required when using the Production API with secure access.")]
    [RegularExpression(@"[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+", ErrorMessage = "The Secure Access API key must be a valid API key.")]
    public string? SecureAccessApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value of codename for the rendition preset to be applied by default to the base asset URL path.
    /// If no value is specified, asset URLs will always point to non-customized variant of the image.
    /// </summary>
    public string? DefaultRenditionPreset { get; set; }

    /// <summary>
    /// Gets or sets a custom domain for asset URLs.
    /// When set, the SDK replaces the host of all asset URLs with this domain,
    /// preserving the original path and query string.
    /// </summary>
    [Url]
    public string? CustomAssetDomain { get; set; }

    /// <summary>
    /// Copies every option onto <paramref name="destination"/>. Reflected rather than listed property by
    /// property, so an option added later cannot be silently left behind.
    /// </summary>
    public void CopyTo(DeliveryOptions destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Reflected rather than assigned property by property: a hand-written list keeps compiling when a
        // new option is added and silently stops carrying it, which is a value the caller set and the
        // client never sees.
        OptionsCopier<DeliveryOptions>.Copy(this, destination);
    }

    /// <summary>
    /// Validates cross-field constraints for delivery options.
    /// Ensures mutual exclusivity of <see cref="UsePreviewApi"/> and <see cref="UseSecureAccess"/>.
    /// Validates that <see cref="EnvironmentId"/> is not an empty GUID.
    /// Uses yield semantics so other attribute-based validations also execute.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Timeout is { } timeout && timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
        {
            yield return new ValidationResult(
                "Timeout must be positive, or Timeout.InfiniteTimeSpan for no ceiling.",
                [nameof(Timeout)]);
        }

        if (UsePreviewApi && UseSecureAccess)
        {
            yield return new ValidationResult(
                "Cannot use both Preview API and Secure Access simultaneously.",
                [nameof(UsePreviewApi), nameof(UseSecureAccess)]);
        }

        if (string.IsNullOrWhiteSpace(EnvironmentId))
        {
            yield break;
        }

        if (!Guid.TryParse(EnvironmentId, out var environmentGuid))
        {
            yield return new ValidationResult(
                "The environment ID must be a valid GUID.",
                [nameof(EnvironmentId)]);
        }
        else if (environmentGuid == Guid.Empty)
        {
            yield return new ValidationResult(
                "EnvironmentId cannot be an empty GUID.",
                [nameof(EnvironmentId)]);
        }

        if (!string.IsNullOrWhiteSpace(CustomAssetDomain))
        {
            if (!Uri.TryCreate(CustomAssetDomain, UriKind.Absolute, out var customDomainUri))
            {
                yield return new ValidationResult(
                    $"CustomAssetDomain '{CustomAssetDomain}' is not a valid absolute URI.",
                    [nameof(CustomAssetDomain)]);
                yield break;
            }

            if (customDomainUri.AbsolutePath is not ("/" or ""))
            {
                yield return new ValidationResult(
                    $"CustomAssetDomain must be a root domain without a path (e.g. 'https://assets.example.com'). " +
                    $"The path '{customDomainUri.AbsolutePath}' would be silently ignored.",
                    [nameof(CustomAssetDomain)]);
            }

            if (!string.IsNullOrEmpty(customDomainUri.Query))
            {
                yield return new ValidationResult(
                    "CustomAssetDomain must not contain a query string.",
                    [nameof(CustomAssetDomain)]);
            }

            if (!string.IsNullOrEmpty(customDomainUri.Fragment))
            {
                yield return new ValidationResult(
                    "CustomAssetDomain must not contain a fragment.",
                    [nameof(CustomAssetDomain)]);
            }
        }
    }
}
