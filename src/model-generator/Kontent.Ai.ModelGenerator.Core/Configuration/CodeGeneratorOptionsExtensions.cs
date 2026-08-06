namespace Kontent.Ai.ModelGenerator.Core.Configuration;

/// <summary>
/// Extension methods for <see cref="CodeGeneratorOptions"/>.
/// </summary>
public static class CodeGeneratorOptionsExtensions
{
    /// <summary>
    /// Gets the environment ID the run is targeting, whichever mode populated it, or <c>null</c> when
    /// neither is configured.
    /// </summary>
    public static string? GetEnvironmentId(this CodeGeneratorOptions options) =>
        options.DeliveryOptions?.EnvironmentId ?? options.ManagementOptions?.EnvironmentId;
}
