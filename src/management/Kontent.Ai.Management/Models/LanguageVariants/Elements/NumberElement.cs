namespace Kontent.Ai.Management.Models.LanguageVariants.Elements;

/// <summary>Value of a number element.</summary>
public sealed record NumberElement : BaseElement
{
    /// <summary>The numeric value. <c>null</c> clears the element.</summary>
    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public decimal? Value { get; init; }
}
