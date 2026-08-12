namespace Kontent.Ai.Management.Models.EnvironmentValidation;

/// <summary>
/// The result of the async validation task.
/// </summary>
public enum AsyncValidationTaskResult
{
    /// <summary>
    /// The async validation task is not finished yet.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>
    /// Environment doesn't contain any issues.
    /// </summary>
    [JsonStringEnumMemberName("no_issues")]
    NoIssues,

    /// <summary>
    /// Environment contains validation issues.
    /// </summary>
    [JsonStringEnumMemberName("issues_found")]
    IssuesFound,
}
