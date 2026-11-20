namespace Kontent.Ai.Delivery.SourceGeneration.Snapshot;

internal sealed record ModelProperty(string Codename, string Identifier, string ConstantIdentifier, string TypeName, string? Initializer);

internal enum ModelDiagnostic
{
    Unusable,
    ElementSkipped,
    ContentTypeSkipped,
}

/// <summary>A diagnostic to report later, kept as plain values so it can sit in the incremental cache.</summary>
internal sealed record ModelDiagnosticInfo(ModelDiagnostic Kind, string FilePath, string Message);

/// <summary>A content type as a model. <see cref="ClassName"/> is null when it can't become one, and <see cref="Diagnostics"/> says why.</summary>
internal sealed record ModelSpec(
    string Codename,
    string? ClassName,
    string Namespace,
    string FilePath,
    EquatableArray<ModelProperty> Properties,
    EquatableArray<ModelDiagnosticInfo> Diagnostics)
{
    public string FullyQualifiedName => $"global::{Namespace}.{ClassName}";
}

/// <summary>
/// Turns a snapshot content type into a Delivery model. The rules are the model generator tool's Delivery mode
/// (<c>Property</c>, <c>ClassDefinition</c>, <c>SnippetExpander</c>), so the same content model produces the same
/// members whichever one generates it; the messages are the tool's too.
/// </summary>
internal static class ModelBuilder
{
    private const string ContentTypeCodenameIdentifier = "ContentTypeCodename";
    private const string RichTextContent = "global::Kontent.Ai.Delivery.ContentItems.RichText.RichTextContent";

    private sealed record ElementMapping(string StrictTypeName, string SemanticTypeName, string? SemanticInitializer);

    private static readonly Dictionary<string, ElementMapping> ElementMappings = new(StringComparer.Ordinal)
    {
        ["text"] = new("string?", "string", "string.Empty"),
        ["rich_text"] = new($"{RichTextContent}?", RichTextContent, $"{RichTextContent}.Empty"),
        ["number"] = new("double?", "double?", null),
        ["date_time"] = new("global::Kontent.Ai.Delivery.SharedModels.DateTimeContent?", "global::Kontent.Ai.Delivery.SharedModels.DateTimeContent?", null),
        ["multiple_choice"] = Collection("global::Kontent.Ai.Delivery.SharedModels.MultipleChoiceOption"),
        ["asset"] = Collection("global::Kontent.Ai.Delivery.ContentItems.Asset"),
        ["modular_content"] = Collection("global::Kontent.Ai.Delivery.Abstractions.IEmbeddedContent"),
        // The Delivery API reports a subpages element as modular_content, so its model property is the same.
        ["subpages"] = Collection("global::Kontent.Ai.Delivery.Abstractions.IEmbeddedContent"),
        ["taxonomy"] = Collection("global::Kontent.Ai.Delivery.SharedModels.TaxonomyTerm"),
        ["url_slug"] = new("string?", "string", "string.Empty"),
        ["custom"] = new("string?", "string?", null),
    };

    public static ModelSpec Build(SnapshotFile typeFile, EquatableArray<SnapshotEntity> snippets, SnapshotSettings settings)
    {
        if (typeFile.Entity is not { } contentType)
        {
            return Skipped(typeFile, string.Empty, settings, new ModelDiagnosticInfo(ModelDiagnostic.Unusable, typeFile.Path, $"{typeFile.Path}: {typeFile.Error}"));
        }

        var className = Identifiers.ToPascalCase(contentType.Codename);
        if (className is null)
        {
            return Skipped(typeFile, contentType.Codename, settings, new ModelDiagnosticInfo(ModelDiagnostic.ContentTypeSkipped, typeFile.Path,
                $"Skipping Content Type '{contentType.Codename}'. Can't create valid C# identifier from its name."));
        }

        var diagnostics = new List<ModelDiagnosticInfo>();
        void Warn(string message) => diagnostics.Add(new(ModelDiagnostic.ElementSkipped, typeFile.Path, message));

        // Which codename claimed each member name. A member may not share its record's name (CS0542), so the class
        // name is claimed up front; the tool does not do this and emits a record that fails to compile.
        var owners = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ContentTypeCodenameIdentifier] = contentType.Codename,
            [className] = contentType.Codename,
        };

        var properties = new List<ModelProperty>();
        foreach (var element in ExpandSnippets(contentType.Elements, snippets, Warn))
        {
            if (!ElementMappings.TryGetValue(element.Type, out var mapping))
            {
                Warn($"Skipping unknown Content Element type '{element.Type}'. (Content Type: '{className}', Element Codename: '{element.Codename}').");
                continue;
            }

            var name = Identifiers.ToPascalCase(element.Codename);
            if (name is null)
            {
                Warn($"Can't create valid C# Identifier from '{element.Codename}'. Skipping element.");
                continue;
            }

            var identifier = name == ContentTypeCodenameIdentifier ? "_" + name : name;
            var constant = name + "Codename" == ContentTypeCodenameIdentifier ? "_" + name + "Codename" : name + "Codename";

            var clash = Clash(owners, className, identifier, element.Codename) ?? Clash(owners, className, constant, element.Codename);
            if (clash is not null)
            {
                Warn($"Skipping element '{element.Codename}'. Content type '{contentType.Codename}': {clash}. Rename one of the elements in Kontent.ai.");
                continue;
            }

            owners[identifier] = element.Codename;
            owners[constant] = element.Codename;
            properties.Add(settings.SemanticNullability
                ? new ModelProperty(element.Codename, identifier, constant, mapping.SemanticTypeName, mapping.SemanticInitializer)
                : new ModelProperty(element.Codename, identifier, constant, mapping.StrictTypeName, null));
        }

        return new ModelSpec(contentType.Codename, className, settings.Namespace, typeFile.Path, properties.ToEquatableArray(), diagnostics.ToEquatableArray());
    }

    private static IEnumerable<SnapshotElement> ExpandSnippets(
        EquatableArray<SnapshotElement> elements,
        EquatableArray<SnapshotEntity> snippets,
        Action<string> warn)
    {
        foreach (var element in elements)
        {
            switch (element.Type)
            {
                // The Delivery API never returns guidelines, so dropping them adds nothing to report.
                case "guidelines":
                    continue;
                case "snippet":
                    var snippet = snippets.FirstOrDefault(s => s.Id == element.SnippetId);
                    if (snippet is null)
                    {
                        warn($"Could not resolve snippet reference on snippet element '{element.Codename}'; skipping.");
                        continue;
                    }

                    // Snippet element codenames already carry the snippet prefix, so they pass through unchanged.
                    foreach (var inner in snippet.Elements)
                    {
                        if (inner.Type == "snippet")
                        {
                            warn($"Snippet '{snippet.Codename}' contains a nested snippet element — MAPI should not allow this. Skipping inner element.");
                            continue;
                        }

                        if (inner.Type != "guidelines")
                            yield return inner;
                    }

                    break;
                default:
                    yield return element;
                    break;
            }
        }
    }

    private static string? Clash(Dictionary<string, string> owners, string className, string identifier, string codename)
    {
        if (!owners.TryGetValue(identifier, out var owner))
            return null;

        if (identifier == className)
            return $"'{codename}' produces '{identifier}', the name of the record itself";

        return owner == codename
            ? $"'{codename}' would emit '{identifier}' twice"
            : $"'{codename}' and '{owner}' both produce the identifier '{identifier}'";
    }

    private static ModelSpec Skipped(SnapshotFile file, string codename, SnapshotSettings settings, ModelDiagnosticInfo reason) =>
        new(codename, null, settings.Namespace, file.Path, EquatableArray<ModelProperty>.Empty, new[] { reason }.ToEquatableArray());

    private static ElementMapping Collection(string elementType) =>
        new($"global::System.Collections.Generic.IEnumerable<{elementType}>?", $"global::System.Collections.Generic.IEnumerable<{elementType}>", "[]");
}
