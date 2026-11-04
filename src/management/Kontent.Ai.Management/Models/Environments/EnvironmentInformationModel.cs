namespace Kontent.Ai.Management.Models.Environments;

/// <summary>
/// Project and environment identity — returned by <c>GetEnvironmentInformationAsync</c> and carried on a validation report.
/// </summary>
public sealed record EnvironmentInformationModel
{
    /// <summary>
    /// Environment ID.
    /// </summary>
    [JsonPropertyName("id")]
    public required Guid Id { get; init; }

    /// <summary>
    /// Project name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string ProjectName { get; init; }

    /// <summary>
    /// Environment name.
    /// </summary>
    [JsonPropertyName("environment")]
    public required string EnvironmentName { get; init; }

    /// <summary>
    /// Whether the environment is marked as production. <c>null</c> on a validation report, which does not carry it.
    /// </summary>
    [JsonPropertyName("is_production")]
    public bool? IsProduction { get; init; }
}
