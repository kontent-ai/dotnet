namespace Kontent.Ai.Sync.Abstractions;

/// <summary>
/// Represents a delta update for a language.
/// </summary>
public interface ISyncLanguage
{
    /// <summary>
    /// Gets the type of change that occurred.
    /// </summary>
    ChangeType ChangeType { get; }

    /// <summary>
    /// Gets the language data.
    /// This property contains the full language when ChangeType is Created or Updated.
    /// When ChangeType is Deleted, this may be null or contain minimal identifying information.
    /// </summary>
    object? Data { get; }
}
