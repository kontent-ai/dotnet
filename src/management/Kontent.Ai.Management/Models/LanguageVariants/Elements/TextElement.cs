namespace Kontent.Ai.Management.Models.LanguageVariants.Elements;

/// <summary>Value of a text element.</summary>
public sealed record TextElement : BaseElement
{
    /// <summary>The plain-text value. <c>null</c> clears the element.</summary>
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Value { get; init; }
}
