
namespace Kontent.Ai.Management.Models.Types.Elements;

/// <summary>
/// Enum of all possible element types in content types.
/// </summary>
public enum ElementMetadataType
{
    /// <summary>
    /// Represents the text element.
    /// </summary>
    [JsonStringEnumMemberName("text")]
    Text = 1,
    /// <summary>
    /// Represents the rich text element.
    /// </summary>
    [JsonStringEnumMemberName("rich_text")]
    RichText = 2,

    /// <summary>
    /// Represents the number element.
    /// </summary>
    [JsonStringEnumMemberName("number")]
    Number = 3,

    /// <summary>
    /// Represents the multiple-choice element.
    /// </summary>
    [JsonStringEnumMemberName("multiple_choice")]
    MultipleChoice = 4,

    /// <summary>
    /// Represents the date and time element.
    /// </summary>
    [JsonStringEnumMemberName("date_time")]
    DateTime = 5,

    /// <summary>
    /// Represents the asset element.
    /// </summary>
    [JsonStringEnumMemberName("asset")]
    Asset = 6,

    /// <summary>
    /// Represents the linked items element.
    /// </summary>
    [JsonStringEnumMemberName("modular_content")]
    LinkedItems = 7,

    /// <summary>
    /// Represents the guidelines element.
    /// </summary>
    [JsonStringEnumMemberName("guidelines")]
    Guidelines = 8,

    /// <summary>
    /// Represents the taxonomy element.
    /// </summary>
    [JsonStringEnumMemberName("taxonomy")]
    Taxonomy = 9,

    /// <summary>
    /// Represents the url slug element.
    /// </summary>
    [JsonStringEnumMemberName("url_slug")]
    UrlSlug = 10,

    /// <summary>
    /// Represents the content type snippet element.
    /// </summary>
    [JsonStringEnumMemberName("snippet")]
    ContentTypeSnippet = 11,

    /// <summary>
    /// Represents the custom element.
    /// </summary>
    [JsonStringEnumMemberName("custom")]
    Custom = 12,

    /// <summary>
    /// Represents the subpages element.
    /// </summary>
    [JsonStringEnumMemberName("subpages")]
    Subpages = 13
}
