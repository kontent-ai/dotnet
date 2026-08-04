
namespace Kontent.Ai.Management.Models.EnvironmentValidation;

/// <summary>
/// The status of the async validation task.
/// </summary>
public enum AsyncValidationTaskStatus
{
    /// <summary>
    /// Task is queued.
    /// </summary>
    [JsonStringEnumMemberName("queued")]
    Queued,

    /// <summary>
    /// Task is finished.
    /// </summary>
    [JsonStringEnumMemberName("finished")]
    Finished,

    /// <summary>
    /// Task has failed.
    /// </summary>
    [JsonStringEnumMemberName("failed")]
    Failed,
}
