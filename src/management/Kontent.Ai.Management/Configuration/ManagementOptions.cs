using System.ComponentModel.DataAnnotations;

namespace Kontent.Ai.Management.Configuration;

/// <summary>
/// Configuration for <see cref="ManagementClient"/>. Bind from <c>IConfiguration</c> or construct directly.
/// </summary>
public sealed class ManagementOptions : IValidatableObject
{
    /// <summary>
    /// The configuration section name <c>AddManagementClient</c> binds by default. Design-time tools that resolve
    /// the SDK's configuration from the same sources (e.g. a model-pull CLI) should probe this section.
    /// </summary>
    public const string DefaultConfigurationSectionName = "ManagementOptions";

    /// <summary>
    /// Gets or sets the base address of the Management API. Optional; defaults to <c>https://manage.kontent.ai</c>.
    /// The SDK appends the versioned, scoped path (<c>/v2/projects/{id}</c> or <c>/v2/subscriptions/{id}</c>).
    /// </summary>
    // Fully qualified: Refit is in scope via GlobalUsings.cs and also defines a UrlAttribute,
    // so a bare [Url] is ambiguous.
    [System.ComponentModel.DataAnnotations.Url]
    public string Endpoint { get; set; } = "https://manage.kontent.ai";

    /// <summary>
    /// Gets or sets the environment identifier (GUID).
    /// </summary>
    public string? EnvironmentId { get; set; }

    /// <summary>
    /// Gets or sets the subscription identifier. Required only for subscription-scoped endpoints.
    /// </summary>
    public string? SubscriptionId { get; set; }

    /// <summary>
    /// Gets or sets the Management API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets whether the default resilience pipeline is active. Defaults to <c>true</c>. Set to <c>false</c>
    /// to bypass all retry/backoff behaviour without uninstalling the handler.
    /// </summary>
    public bool EnableResilience { get; set; } = true;

    /// <summary>
    /// Gets or sets the ceiling on one call, covering every retry attempt and the waits between them.
    /// Defaults to 30 minutes. Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to leave the call
    /// bounded only by the caller's <see cref="CancellationToken"/>.
    /// </summary>
    /// <remarks>
    /// This SDK sets no per-attempt timeout: an asset upload takes as long as the file is large and the link
    /// is slow, and cutting one off mid-transfer helps nobody. This ceiling is therefore the only bound, and
    /// it covers the whole call - every attempt plus the backoff between them - so it has to clear the
    /// slowest legitimate upload rather than the slowest single attempt. The default is sized against the
    /// documented 2 GB asset limit: 30 minutes carries that much over a link of roughly 10 Mbps.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Copies every option onto <paramref name="destination"/>. Reflected rather than listed property by
    /// property, so an option added later cannot be silently left behind.
    /// </summary>
    public void CopyTo(ManagementOptions destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Kontent.Ai.Common.OptionsCopier<ManagementOptions>.Copy(this, destination);
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult(
                "ApiKey cannot be empty.",
                [nameof(ApiKey)]);
        }

        // A client scoped to neither can call nothing, so that is a configuration mistake worth surfacing
        // at registration. Configuring one of the two is legitimate: subscription endpoints do not belong
        // to an environment, and an environment-only client never needs a subscription.
        if (!this.HasEnvironmentId() && !this.HasSubscriptionId())
        {
            yield return new ValidationResult(
                "Set EnvironmentId for environment endpoints, SubscriptionId for subscription endpoints, or both.",
                [nameof(EnvironmentId), nameof(SubscriptionId)]);
        }

        if (this.HasEnvironmentId())
        {
            if (!Guid.TryParse(EnvironmentId, out var environmentGuid))
            {
                yield return new ValidationResult(
                    $"Provided string is not a valid environment identifier ({EnvironmentId}). Haven't you accidentally passed the API key instead of the environment identifier?",
                    [nameof(EnvironmentId)]);
            }
            else if (environmentGuid == Guid.Empty)
            {
                yield return new ValidationResult(
                    "EnvironmentId cannot be an empty GUID.",
                    [nameof(EnvironmentId)]);
            }
        }

        if (!string.IsNullOrWhiteSpace(SubscriptionId)
            && (!Guid.TryParse(SubscriptionId, out var subscriptionGuid) || subscriptionGuid == Guid.Empty))
        {
            yield return new ValidationResult(
                $"Provided string is not a valid subscription identifier ({SubscriptionId}).",
                [nameof(SubscriptionId)]);
        }

        if (Timeout <= TimeSpan.Zero && Timeout != System.Threading.Timeout.InfiniteTimeSpan)
        {
            yield return new ValidationResult(
                "Timeout must be positive, or Timeout.InfiniteTimeSpan for no ceiling.",
                [nameof(Timeout)]);
        }
    }
}
