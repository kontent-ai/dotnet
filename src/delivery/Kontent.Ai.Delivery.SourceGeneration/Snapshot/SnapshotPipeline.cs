using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Kontent.Ai.Delivery.SourceGeneration.Snapshot;

/// <summary>Whether models are generated: the project names a snapshot and it can be read.</summary>
internal sealed record SnapshotStatus(bool IsActive, ModelDiagnosticInfo? Error);

/// <summary>A model to emit. <see cref="ClassOwner"/> is the codename that won its class name, which another content type can sanitize to as well.</summary>
internal sealed record SnapshotOutput(ModelSpec Model, bool IsActive, string? ClassOwner)
{
    public bool IsEmitted => IsActive && Model.ClassName is not null && ClassOwner == Model.Codename;
}

/// <summary>A generated model, as the type provider needs to list it.</summary>
internal sealed record SnapshotModelEntry(string Codename, string FullyQualifiedName, string ClassName, string FilePath);

/// <summary>
/// Generates a model per content type in the project's content model snapshot, and hands the type provider the
/// list of what it generated. Without a snapshot it produces nothing.
/// </summary>
/// <remarks>
/// Every step carries plain records, so the driver can tell an unchanged step from a changed one. Typing in a C#
/// file changes none of these inputs, so no JSON is read and no model is rewritten. A changed snapshot file
/// re-reads that file only, and a changed snippet rewrites only the models whose output it changes.
/// </remarks>
internal static class SnapshotPipeline
{
    public const string FilesStep = "SnapshotFiles";
    public const string ModelsStep = "SnapshotModels";
    public const string OutputsStep = "SnapshotOutputs";

    private const string SupportedMajorVersion = "1";

    public static IncrementalValueProvider<EquatableArray<SnapshotModelEntry>> Register(IncrementalGeneratorInitializationContext context)
    {
        var files = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, ct) => SnapshotReader.Read(pair.Left, pair.Right.GetOptions(pair.Left), ct))
            .Where(static file => file is not null)
            .Select(static (file, _) => file!)
            .WithTrackingName(FilesStep);

        var settings = context.AnalyzerConfigOptionsProvider
            .Select(static (options, _) => SnapshotSettings.From(options.GlobalOptions));

        var status = files.Collect()
            .Combine(settings)
            .Select(static (input, _) => Check(input.Left, input.Right));

        var snippets = files
            .Where(static file => file is { Kind: SnapshotKind.Snippet, Entity: not null })
            .Select(static (file, _) => file.Entity!)
            .Collect()
            .Select(static (all, _) => all.ToEquatableArray());

        var models = files
            .Where(static file => file.Kind == SnapshotKind.Type)
            .Combine(snippets)
            .Combine(settings)
            .Select(static (input, _) => ModelBuilder.Build(input.Left.Left, input.Left.Right, input.Right))
            .WithTrackingName(ModelsStep);

        var classOwners = models.Collect().Select(static (all, _) => ClaimClassNames(all));

        var outputs = models
            .Combine(classOwners)
            .Combine(status)
            .Select(static (input, _) => new SnapshotOutput(
                input.Left.Left,
                input.Right.IsActive,
                OwnerOf(input.Left.Right, input.Left.Left.ClassName)))
            .WithTrackingName(OutputsStep);

        context.RegisterSourceOutput(status, static (spc, snapshot) =>
        {
            if (snapshot.Error is { } error)
                spc.ReportDiagnostic(ToDiagnostic(error));
        });

        context.RegisterSourceOutput(outputs, static (spc, output) => Emit(spc, output));

        return outputs
            .Where(static output => output.IsEmitted)
            .Select(static (output, _) => new SnapshotModelEntry(
                output.Model.Codename, output.Model.FullyQualifiedName, output.Model.ClassName!, output.Model.FilePath))
            .Collect()
            .Select(static (all, _) => all.ToEquatableArray());
    }

    private static SnapshotStatus Check(ImmutableArray<SnapshotFile> files, SnapshotSettings settings)
    {
        if (files.IsEmpty)
            return new SnapshotStatus(false, null);

        if (settings.Error is { } settingsError)
            return Unusable(string.Empty, settingsError);

        var manifests = files.Where(static file => file.Kind == SnapshotKind.Manifest).ToList();
        if (manifests.Count != 1)
            return Unusable(string.Empty, "KontentModelSnapshot must point at one folder made by 'kontent model snapshot'");

        var manifest = manifests[0];
        if (manifest.Manifest is not { } content)
            return Unusable(manifest.Path, $"{manifest.Path}: {manifest.Error}. KontentModelSnapshot must point at a folder made by 'kontent model snapshot'");

        if (content.FormatVersion.Split('.')[0] != SupportedMajorVersion)
        {
            return Unusable(manifest.Path,
                $"{manifest.Path}: format_version {content.FormatVersion} isn't supported. This version reads format {SupportedMajorVersion}.x; update Kontent.Ai.Delivery.SourceGeneration");
        }

        var missing = new[] { "types", "snippets" }.Where(kind => !content.Kinds.Contains(kind)).ToList();
        return missing.Count == 0
            ? new SnapshotStatus(true, null)
            : Unusable(manifest.Path, $"{manifest.Path}: the snapshot doesn't contain {string.Join(" or ", missing)}");
    }

    private static SnapshotStatus Unusable(string path, string message) =>
        new(false, new ModelDiagnosticInfo(ModelDiagnostic.Unusable, path, message));

    /// <summary>
    /// Two content types whose codenames sanitize to one class name would both define it. The first codename in
    /// ordinal order keeps it, so the outcome doesn't depend on the order the build lists the files in.
    /// </summary>
    private static EquatableArray<ClassClaim> ClaimClassNames(ImmutableArray<ModelSpec> models) =>
        models
            .Where(static model => model.ClassName is not null)
            .GroupBy(static model => model.ClassName!, StringComparer.Ordinal)
            .Select(static group => new ClassClaim(group.Key, group.Select(static m => m.Codename).OrderBy(static c => c, StringComparer.Ordinal).First()))
            .OrderBy(static claim => claim.ClassName, StringComparer.Ordinal)
            .ToEquatableArray();

    private static string? OwnerOf(EquatableArray<ClassClaim> claims, string? className) =>
        claims.FirstOrDefault(claim => claim.ClassName == className)?.Codename;

    private static void Emit(SourceProductionContext context, SnapshotOutput output)
    {
        if (!output.IsActive)
            return;

        var model = output.Model;
        foreach (var diagnostic in model.Diagnostics)
        {
            context.ReportDiagnostic(ToDiagnostic(diagnostic));
        }

        if (model.ClassName is null)
            return;

        if (!output.IsEmitted)
        {
            context.ReportDiagnostic(ToDiagnostic(new ModelDiagnosticInfo(ModelDiagnostic.ContentTypeSkipped, model.FilePath,
                $"Skipping content type '{model.Codename}': it generates the same model as '{output.ClassOwner}' ('{model.ClassName}'). Rename one of them in Kontent.ai.")));
            return;
        }

        context.AddSource(ModelWriter.HintName(model), SourceText.From(ModelWriter.Write(model), Encoding.UTF8));
    }

    private static Diagnostic ToDiagnostic(ModelDiagnosticInfo info)
    {
        var descriptor = info.Kind switch
        {
            ModelDiagnostic.ElementSkipped => DiagnosticDescriptors.SnapshotElementSkipped,
            ModelDiagnostic.ContentTypeSkipped => DiagnosticDescriptors.SnapshotContentTypeSkipped,
            _ => DiagnosticDescriptors.SnapshotUnusable,
        };

        var location = info.FilePath.Length == 0
            ? Location.None
            : Location.Create(info.FilePath, default, default);

        return Diagnostic.Create(descriptor, location, info.Message);
    }

    internal sealed record ClassClaim(string ClassName, string Codename);
}
