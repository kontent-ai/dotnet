namespace Kontent.Ai.Management.Models.Types.Elements;

/// <summary>
/// Determines whether the maximum_text_length applies to characters or words.
/// </summary>
public enum TextLengthLimitType
{
    /// <summary>
    /// Words.
    /// </summary>
    [JsonStringEnumMemberName("words")]
    Words,

    /// <summary>
    /// Characters.
    /// </summary>
    [JsonStringEnumMemberName("characters")]
    Characters
}
