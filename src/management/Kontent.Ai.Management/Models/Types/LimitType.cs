
namespace Kontent.Ai.Management.Models.Types;

/// <summary>
/// Defines how to apply the limitation.
/// </summary>
public enum LimitType
{
    /// <summary>
    /// At least.
    /// </summary>
    [JsonStringEnumMemberName("at_least")]
    AtLeast,

    /// <summary>
    /// Exactly.
    /// </summary>
    [JsonStringEnumMemberName("exactly")]
    Exactly,

    /// <summary>
    /// At most.
    /// </summary>
    [JsonStringEnumMemberName("at_most")]
    AtMost
}
