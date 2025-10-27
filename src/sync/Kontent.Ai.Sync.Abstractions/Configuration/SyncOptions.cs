using System.ComponentModel.DataAnnotations;

namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents configuration of the <see cref="ISyncClient"/>.
/// </summary>
public sealed class SyncOptions : IValidatableObject
{
    /// <summary>
    /// Gets or sets the environment ID.
    /// </summary>
    [Required]
    [RegularExpression(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", ErrorMessage = "The environment ID must be a valid GUID.")]
    public string EnvironmentId { get; set; } = Guid.Empty.ToString();

    /// <summary>
    /// Gets or sets a value that determines if the client uses resilience policies.
    /// </summary>
    public bool EnableResilience { get; set; } = true;

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
    public string? PreviewApiKey { get; set; }

    /// <summary>
    /// Gets or sets a value that determines if the Preview API is used to retrieve content.
    /// If the Preview API is used the <see cref="PreviewApiKey"/> must be set.
    /// </summary>
    public bool UsePreviewApi { get; set; } = false;

    /// <summary>
    /// Validates cross-field constraints for sync options.
    /// Ensures that <see cref="EnvironmentId"/> is not an empty GUID.
    /// Ensures that <see cref="PreviewApiKey"/> is set when <see cref="UsePreviewApi"/> is true.
    /// Uses yield semantics so other attribute-based validations also execute.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Guid.TryParse(EnvironmentId, out var environmentGuid) && environmentGuid == Guid.Empty)
        {
            yield return new ValidationResult(
                "EnvironmentId cannot be an empty GUID.",
                [nameof(EnvironmentId)]);
        }

        if (UsePreviewApi && string.IsNullOrWhiteSpace(PreviewApiKey))
        {
            yield return new ValidationResult(
                "PreviewApiKey is required when using the Preview API.",
                [nameof(PreviewApiKey), nameof(UsePreviewApi)]);
        }
    }
}
