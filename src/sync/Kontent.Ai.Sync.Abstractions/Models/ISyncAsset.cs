namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents a delta update for an asset.
/// </summary>
public interface ISyncAsset
{
    /// <summary>
    /// Gets the type of change that occurred.
    /// </summary>
    ChangeType ChangeType { get; }

    /// <summary>
    /// Gets the asset data.
    /// This property contains the full asset when ChangeType is Created or Updated.
    /// When ChangeType is Deleted, this may be null or contain minimal identifying information.
    /// </summary>
    object? Data { get; }
}
