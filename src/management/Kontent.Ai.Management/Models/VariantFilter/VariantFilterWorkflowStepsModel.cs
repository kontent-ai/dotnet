namespace Kontent.Ai.Management.Models.VariantFilter;

/// <summary>
/// Workflow-based filter: restrict results to variants currently in specific steps of a workflow.
/// </summary>
public sealed record VariantFilterWorkflowStepsModel
{
    /// <summary>
    /// Reference to the workflow.
    /// </summary>
    [JsonPropertyName("workflow_identifier")]
    public required Reference Workflow { get; init; }

    /// <summary>
    /// Steps within the workflow to match. The API requires at least one; omitting them, or sending none, is rejected.
    /// </summary>
    [JsonPropertyName("step_identifiers")]
    public IReadOnlyList<Reference>? Steps { get; init; }
}