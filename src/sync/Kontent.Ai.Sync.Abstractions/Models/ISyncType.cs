namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents a delta update for a content type.
/// </summary>
public interface ISyncType
{
    /// <summary>
    /// Gets the type of change that occurred.
    /// </summary>
    ChangeType ChangeType { get; }

    /// <summary>
    /// Gets the content type data.
    /// This property contains the full content type definition when ChangeType is Created or Updated.
    /// When ChangeType is Deleted, this may be null or contain minimal identifying information.
    /// </summary>
    object? Data { get; }
}
