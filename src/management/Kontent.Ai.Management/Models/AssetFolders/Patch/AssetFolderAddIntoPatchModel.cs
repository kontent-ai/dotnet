namespace Kontent.Ai.Management.Models.AssetFolders.Patch;

/// <summary>
/// Represents addInto operation to perform on the folder.
/// </summary>
public sealed record AssetFolderAddIntoPatchModel : AssetFolderOperationBaseModel
{
    /// <summary>
    /// Represents addInto operation.
    /// </summary>
    public override string Op => "addInto";

    /// <summary>
    /// Reference to the parent folder to add into; omit to add at the root.
    /// </summary>
    [JsonPropertyName("reference")]
    public Reference? Reference { get; init; }

    /// <summary>
    /// The folder to add. Required.
    /// </summary>
    [JsonPropertyName("value")]
    public required AssetFolderHierarchy Value { get; init; }

    /// <summary>
    /// Reference to the existing sibling folder before which the new folder should be inserted. Mutually exclusive with <see cref="After"/>.
    /// </summary>
    [JsonPropertyName("before")]
    public Reference? Before { get; init; }

    /// <summary>
    /// Reference to the existing sibling folder after which the new folder should be inserted. Mutually exclusive with <see cref="Before"/>.
    /// </summary>
    [JsonPropertyName("after")]
    public Reference? After { get; init; }
}
