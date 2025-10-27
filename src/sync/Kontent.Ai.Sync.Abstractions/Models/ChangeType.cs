namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents the type of change that occurred to a synchronized entity.
/// </summary>
public enum ChangeType
{
    /// <summary>
    /// The entity was created.
    /// </summary>
    Created,

    /// <summary>
    /// The entity was updated.
    /// </summary>
    Updated,

    /// <summary>
    /// The entity was deleted.
    /// </summary>
    Deleted
}
