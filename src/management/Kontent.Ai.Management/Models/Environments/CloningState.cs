namespace Kontent.Ai.Management.Models.Environments;

/// <summary>
/// Represents the state on environment cloning.
/// </summary>
public enum CloningState
{
    /// <summary>
    /// Environment cloning is in progress.
    /// </summary>
    [JsonStringEnumMemberName("in_progress")]
    InProgress,

    /// <summary>
    /// Environment cloning failed.
    /// </summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>
    /// Environment cloning is successfully done.
    /// </summary>
    [JsonStringEnumMemberName("done")]
    Done
}
