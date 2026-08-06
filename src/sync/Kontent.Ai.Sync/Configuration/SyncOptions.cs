using System.ComponentModel.DataAnnotations;

namespace Kontent.Ai.Sync;

/// <summary>
/// Represents configuration of the <see cref="ISyncClient"/>.
/// </summary>
public sealed class SyncOptions : IValidatableObject
{
    /// <summary>
    /// The configuration section name <c>AddSyncClient</c> binds by default. Design-time tools that
    /// resolve the SDK's configuration from the same sources should probe this section.
    /// </summary>
    public const string DefaultConfigurationSectionName = "SyncOptions";

    /// <summary>
    /// Gets or sets the environment ID.
    /// </summary>
    [Required]
    public string EnvironmentId { get; set; } = Guid.Empty.ToString();

    /// <summary>
    /// Gets or sets a value that determines if the client uses resilience policies.
    /// </summary>
    public bool EnableResilience { get; set; } = true;

    /// <summary>
    /// Gets or sets the format of the Production API endpoint address.
    /// </summary>
    // Fully qualified: Refit is in scope via GlobalUsings.cs and also defines a UrlAttribute,
    // so a bare [Url] is ambiguous. Matches ManagementOptions, which hit the same collision.
    [System.ComponentModel.DataAnnotations.Url]
    public string ProductionEndpoint { get; set; } = "https://deliver.kontent.ai";

    /// <summary>
    /// Gets or sets the format of the Preview API endpoint address.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Url]
    public string PreviewEndpoint { get; set; } = "https://preview-deliver.kontent.ai";

    /// <summary>
    /// Gets or sets the API mode for accessing Kontent.ai content.
    /// </summary>
    public ApiMode ApiMode { get; set; } = ApiMode.Public;

    /// <summary>
    /// Gets or sets the API key for authenticated access (required for Preview and Secure modes).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Validates cross-field constraints for sync options.
    /// Ensures that <see cref="EnvironmentId"/> is not an empty GUID.
    /// Ensures that <see cref="ApiKey"/> is set when <see cref="ApiMode"/> is Preview or Secure.
    /// Uses yield semantics so other attribute-based validations also execute.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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

        if ((ApiMode == ApiMode.Preview || ApiMode == ApiMode.Secure) && string.IsNullOrWhiteSpace(ApiKey))
        {
            yield return new ValidationResult(
                $"ApiKey is required when using {ApiMode} API mode.",
                [nameof(ApiKey), nameof(ApiMode)]);
        }
    }
}
