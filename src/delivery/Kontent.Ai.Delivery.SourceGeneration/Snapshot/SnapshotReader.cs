using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Kontent.Ai.Delivery.SourceGeneration.Snapshot;

internal enum SnapshotKind
{
    Manifest,
    Type,
    Snippet,
}

/// <summary>A content type or snippet element. <see cref="SnippetId"/> is set for an element of type <c>snippet</c>.</summary>
internal sealed record SnapshotElement(string Codename, string Type, string? SnippetId);

/// <summary>A content type or a snippet: both are a codename and a list of elements.</summary>
internal sealed record SnapshotEntity(string Id, string Codename, EquatableArray<SnapshotElement> Elements);

internal sealed record SnapshotManifest(string FormatVersion, EquatableArray<string> Kinds);

/// <summary>One snapshot file, read. Exactly one of <see cref="Entity"/>, <see cref="Manifest"/> and <see cref="Error"/> is set.</summary>
internal sealed record SnapshotFile(SnapshotKind Kind, string Path, SnapshotEntity? Entity, SnapshotManifest? Manifest, string? Error);

/// <summary>
/// Reads the files the package's targets hand over as <c>AdditionalFiles</c>. They mark each file with the
/// snapshot folder it came from, so every other additional file is ignored and no folder name is guessed here.
/// </summary>
internal static class SnapshotReader
{
    private const string KindMetadata = "build_metadata.AdditionalFiles.KontentSnapshotKind";

    public static SnapshotFile? Read(AdditionalText file, AnalyzerConfigOptions options, CancellationToken cancellationToken)
    {
        if (!options.TryGetValue(KindMetadata, out var kindName))
            return null;

        SnapshotKind? kind = kindName switch
        {
            "manifest" => SnapshotKind.Manifest,
            "types" => SnapshotKind.Type,
            "snippets" => SnapshotKind.Snippet,
            _ => null,
        };

        if (kind is not { } snapshotKind)
            return null;

        var text = file.GetText(cancellationToken);
        if (text is null)
            return new SnapshotFile(snapshotKind, file.Path, null, null, "the file is missing or can't be read");

        try
        {
            using var document = JsonDocument.Parse(text.ToString());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("the file doesn't hold a JSON object");

            return snapshotKind == SnapshotKind.Manifest
                ? new SnapshotFile(snapshotKind, file.Path, null, ReadManifest(root), null)
                : new SnapshotFile(snapshotKind, file.Path, ReadEntity(root), null, null);
        }
        catch (JsonException e)
        {
            return new SnapshotFile(snapshotKind, file.Path, null, null, $"invalid JSON: {e.Message}");
        }
        catch (FormatException e)
        {
            return new SnapshotFile(snapshotKind, file.Path, null, null, e.Message);
        }
    }

    private static SnapshotManifest ReadManifest(JsonElement root) => new(
        RequiredString(root, "format_version"),
        (root.TryGetProperty("kinds", out var kinds) && kinds.ValueKind == JsonValueKind.Array
            ? kinds.EnumerateArray().Where(static kind => kind.ValueKind == JsonValueKind.String).Select(static kind => kind.GetString()!)
            : throw new FormatException("'kinds' is missing or isn't an array")).ToEquatableArray());

    private static SnapshotEntity ReadEntity(JsonElement root)
    {
        if (!root.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
            throw new FormatException("'elements' is missing or isn't an array");

        return new SnapshotEntity(
            RequiredString(root, "id"),
            RequiredString(root, "codename"),
            elements.EnumerateArray().Select(ReadElement).ToEquatableArray());
    }

    private static SnapshotElement ReadElement(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new FormatException("an element isn't a JSON object");

        var type = RequiredString(element, "type");
        var snippetId = type == "snippet"
            && element.TryGetProperty("snippet", out var snippet)
            && snippet.ValueKind == JsonValueKind.Object
            && snippet.TryGetProperty("id", out var id)
            && id.ValueKind == JsonValueKind.String
                ? id.GetString()
                : null;

        return new SnapshotElement(RequiredString(element, "codename"), type, snippetId);
    }

    private static string RequiredString(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new FormatException($"'{name}' is missing or isn't a string");
}

/// <summary>The csproj settings for generated models. <see cref="Error"/> says why they can't be used, or is null.</summary>
internal sealed record SnapshotSettings(string Namespace, bool SemanticNullability, string? Error)
{
    public const string DefaultNamespace = "KontentAiModels";

    public static SnapshotSettings From(AnalyzerConfigOptions options)
    {
        var @namespace = Value(options, "build_property.KontentModelNamespace") ?? DefaultNamespace;
        var nullability = Value(options, "build_property.KontentModelNullability") ?? "Strict";

        var semantic = nullability.Equals("Semantic", StringComparison.OrdinalIgnoreCase);
        if (!semantic && !nullability.Equals("Strict", StringComparison.OrdinalIgnoreCase))
            return new(@namespace, false, $"KontentModelNullability is '{nullability}'; use Strict or Semantic");

        return Identifiers.IsValidNamespace(@namespace)
            ? new(@namespace, semantic, null)
            : new(@namespace, semantic, $"KontentModelNamespace '{@namespace}' isn't a valid C# namespace");
    }

    private static string? Value(AnalyzerConfigOptions options, string key) =>
        options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}
