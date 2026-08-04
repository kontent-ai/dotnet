
namespace Kontent.Ai.Management.Models.EnvironmentValidation;

/// <summary>
/// The type of the async validation task issue.
/// </summary>
public enum AsyncValidationTaskIssueType
{
    /// <summary>
    /// Language variant issue.
    /// </summary>
    [JsonStringEnumMemberName("variant_issue")]
    VariantIssue,

    /// <summary>
    /// Content type issue.
    /// </summary>
    [JsonStringEnumMemberName("type_issue")]
    TypeIssue,
}
