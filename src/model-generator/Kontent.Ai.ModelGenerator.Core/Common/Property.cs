using System.Collections.Immutable;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Helpers;

namespace Kontent.Ai.ModelGenerator.Core.Common;

public class Property(string codename, string typeName, string? id = null, string? initializer = null)
{
    private const string RichTextElementType = "rich_text";
    private const string DateTimeElementType = "date_time";
    private const string ModularContentElementType = "modular_content";

    public string? IdentifierOverride { get; init; }

    public string Identifier => IdentifierOverride ?? TextHelpers.GetValidPascalCaseIdentifierName(Codename);

    public string Codename { get; } = codename;

    public string? Id { get; } = id;

    /// <summary>
    /// Returns return type of the property in a string format (e.g.: "string").
    /// </summary>
    public string TypeName { get; } = typeName;

    /// <summary>
    /// Optional initializer expression (e.g. <c>string.Empty</c>, <c>[]</c>, <c>RichTextContent.Empty</c>).
    /// When set, the generator emits <c>= {initializer};</c> on the property.
    /// </summary>
    public string? Initializer { get; } = initializer;

    private sealed record DeliveryElementMapping(string StrictTypeName, string SemanticTypeName, string? SemanticInitializer);

    private static readonly IImmutableDictionary<string, DeliveryElementMapping> DeliverElementTypesDictionary =
        new Dictionary<string, DeliveryElementMapping>
        {
            { "text", new("string?", "string", "string.Empty") },
            { RichTextElementType, new("RichTextContent?", "RichTextContent", "RichTextContent.Empty") },
            { "number", new("double?", "double?", null) },
            { DateTimeElementType, new("DateTimeContent?", "DateTimeContent?", null) },
            { "multiple_choice", new("IEnumerable<MultipleChoiceOption>?", "IEnumerable<MultipleChoiceOption>", "[]") },
            { "asset", new("IEnumerable<Asset>?", "IEnumerable<Asset>", "[]") },
            { ModularContentElementType, new("IEnumerable<IEmbeddedContent>?", "IEnumerable<IEmbeddedContent>", "[]") },
            { "taxonomy", new("IEnumerable<TaxonomyTerm>?", "IEnumerable<TaxonomyTerm>", "[]") },
            { "url_slug", new("string?", "string", "string.Empty") },
            { "custom", new("string?", "string?", null) }
        }.ToImmutableDictionary();

    public static Property FromContentTypeElement(string codename, string elementType) =>
        FromContentTypeElement(codename, elementType, NullabilityMode.Strict);

    public static Property FromContentTypeElement(string codename, string elementType, NullabilityMode nullabilityMode)
    {
        if (!IsContentTypeSupported(elementType))
        {
            throw new ArgumentException($"Unknown Content Type {elementType}", nameof(elementType));
        }

        var mapping = DeliverElementTypesDictionary[elementType];
        return nullabilityMode == NullabilityMode.Semantic
            ? new Property(codename, mapping.SemanticTypeName, id: null, initializer: mapping.SemanticInitializer)
            : new Property(codename, mapping.StrictTypeName);
    }

    private static bool IsContentTypeSupported(string elementType) => DeliverElementTypesDictionary.ContainsKey(elementType);
}
