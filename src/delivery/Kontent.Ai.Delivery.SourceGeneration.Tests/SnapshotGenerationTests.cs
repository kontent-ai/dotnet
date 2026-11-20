using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Kontent.Ai.Delivery.SourceGeneration.Tests;

public class SnapshotGenerationTests
{
    private const string SnapshotKindMetadata = "build_metadata.AdditionalFiles.KontentSnapshotKind";
    private const string ArticleModel = "KontentModels.Article.g.cs";

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    [Fact]
    public void Snapshot_GeneratesAModelPerContentType_AndTheTypeProviderListsIt()
    {
        var run = RunGenerator(Snapshot(types: [ContentType("article", Element("title", "text"), Element("body_copy", "rich_text"), Element("hero", "asset"))]));

        run.Diagnostics.Should().BeEmpty();
        run.Source(ArticleModel).Should()
            .Contain("namespace KontentAiModels;")
            .And.Contain("[global::Kontent.Ai.Delivery.Attributes.ContentTypeCodename(\"article\")]")
            .And.Contain("public partial record Article")
            .And.Contain("public const string BodyCopyCodename = \"body_copy\";")
            .And.Contain("public const string ContentTypeCodename = \"article\";")
            .And.Contain("[global::System.Text.Json.Serialization.JsonPropertyName(\"body_copy\")]")
            .And.Contain("public global::Kontent.Ai.Delivery.ContentItems.RichText.RichTextContent? BodyCopy { get; init; }")
            .And.Contain("public global::System.Collections.Generic.IEnumerable<global::Kontent.Ai.Delivery.ContentItems.Asset>? Hero { get; init; }");
        run.Provider.Should().Contain("{ \"article\", typeof(global::KontentAiModels.Article) }");
    }

    [Fact]
    public void Snapshot_InlinesSnippetElements_AndDropsGuidelines()
    {
        var run = RunGenerator(Snapshot(
            types: [ContentType("page", Element("headline", "text"), Element("tips", "guidelines"), SnippetReference("metadata", "snippet-id"))],
            snippets: [Snippet("snippet-id", "metadata", Element("metadata__title", "text"), Element("metadata__tips", "guidelines"))]));

        run.Diagnostics.Should().BeEmpty();
        var model = run.Source("KontentModels.Page.g.cs");
        model.Should().Contain("[global::System.Text.Json.Serialization.JsonPropertyName(\"metadata__title\")]")
            .And.Contain("string? MetadataTitle { get; init; }")
            .And.Contain("string? Headline { get; init; }")
            .And.NotContain("Tips");
    }

    [Fact]
    public void Snapshot_SubpagesElement_IsALinkedItemsProperty_AsTheDeliveryApiReportsIt()
    {
        var run = RunGenerator(Snapshot(types: [ContentType("page", Element("children", "subpages"))]));

        run.Source("KontentModels.Page.g.cs").Should()
            .Contain("public global::System.Collections.Generic.IEnumerable<global::Kontent.Ai.Delivery.Abstractions.IEmbeddedContent>? Children { get; init; }");
    }

    [Fact]
    public void Snapshot_UnknownElementType_IsSkippedWithAWarningOnItsFile()
    {
        var run = RunGenerator(Snapshot(types: [ContentType("article", Element("title", "text"), Element("rating", "star_rating"))]));

        var warning = run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG006").Which;
        warning.Severity.Should().Be(DiagnosticSeverity.Warning);
        warning.GetMessage().Should().Contain("'star_rating'").And.Contain("'rating'");
        warning.Location.GetLineSpan().Path.Should().Be("model-snapshot/types/article.json");
        run.Source(ArticleModel).Should().Contain("Title { get; init; }").And.NotContain("Rating");
    }

    [Fact]
    public void Snapshot_ElementNamedLikeItsContentType_IsSkipped_SoTheModelCompiles()
    {
        // A member may not share its record's name (CS0542).
        var run = RunGenerator(Snapshot(types: [ContentType("title", Element("title", "text"), Element("subtitle", "text"))]));

        run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG006")
            .Which.GetMessage().Should().Contain("the name of the record itself");
        run.Source("KontentModels.Title.g.cs").Should().Contain("Subtitle { get; init; }");
    }

    [Fact]
    public void Snapshot_ElementsProducingOneIdentifier_KeepTheFirst()
    {
        var run = RunGenerator(Snapshot(types: [ContentType("article", Element("my_element", "text"), Element("my__element", "number"))]));

        run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG006")
            .Which.GetMessage().Should().Contain("'my__element' and 'my_element' both produce the identifier 'MyElement'");
        run.Source(ArticleModel).Should().Contain("string? MyElement { get; init; }").And.NotContain("double?");
    }

    [Fact]
    public void Snapshot_ContentTypesProducingOneClassName_TheFirstCodenameInOrdinalOrderKeepsIt()
    {
        var run = RunGenerator(Snapshot(types: [ContentType("my_type", Element("a", "text")), ContentType("my__type", Element("b", "text"))]));

        run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG007")
            .Which.GetMessage().Should().Contain("'my_type'").And.Contain("'my__type'");
        run.Source("KontentModels.MyType.g.cs").Should().Contain("ContentTypeCodename(\"my__type\")");
        run.Provider.Should().NotContain("\"my_type\"");
    }

    [Fact]
    public void Snapshot_AndADeclaredModelForOneContentType_IsADuplicateCodename()
    {
        var declared = """
            using Kontent.Ai.Delivery.Attributes;

            namespace App.Handwritten;

            [ContentTypeCodename("article")]
            public partial record Article;
            """;

        var run = RunGenerator(Snapshot(types: [ContentType("article", Element("title", "text"))]), declared);

        run.Diagnostics.Where(d => d.Id == "KDSG001").Should().HaveCount(2)
            .And.AllSatisfy(d => d.GetMessage().Should().Contain("Article, Article (snapshot article.json)"));
        run.Provider.Should().NotContain("\"article\"");
    }

    [Fact]
    public void Snapshot_AndDeclaredModelsForDifferentContentTypes_ShareOneTypeProvider()
    {
        var declared = """
            using Kontent.Ai.Delivery.Attributes;

            namespace App.Handwritten;

            [ContentTypeCodename("author")]
            public sealed class Author;
            """;

        var run = RunGenerator(Snapshot(types: [ContentType("article", Element("title", "text"))]), declared);

        run.Diagnostics.Should().BeEmpty();
        run.Provider.Should()
            .Contain("{ \"article\", typeof(global::KontentAiModels.Article) }")
            .And.Contain("{ \"author\", typeof(global::App.Handwritten.Author) }");
    }

    [Fact]
    public void Snapshot_ProjectSettings_SetTheNamespaceAndNullability()
    {
        var run = RunGenerator(
            Snapshot(types: [ContentType("article", Element("title", "text"), Element("tags", "taxonomy"), Element("rank", "number"))]),
            settings: new Dictionary<string, string>
            {
                ["build_property.KontentModelNamespace"] = "MyApp.Models",
                ["build_property.KontentModelNullability"] = "Semantic",
            });

        run.Diagnostics.Should().BeEmpty();
        run.Source(ArticleModel).Should()
            .Contain("namespace MyApp.Models;")
            .And.Contain("public string Title { get; init; } = string.Empty;")
            .And.Contain("public global::System.Collections.Generic.IEnumerable<global::Kontent.Ai.Delivery.SharedModels.TaxonomyTerm> Tags { get; init; } = [];")
            .And.Contain("public double? Rank { get; init; }")
            .And.NotContain("Rank { get; init; } =");
        run.Provider.Should().Contain("typeof(global::MyApp.Models.Article)");
    }

    [Theory]
    [InlineData("build_property.KontentModelNamespace", "My App", "isn't a valid C# namespace")]
    [InlineData("build_property.KontentModelNamespace", "App.class", "isn't a valid C# namespace")]
    [InlineData("build_property.KontentModelNullability", "Loose", "use Strict or Semantic")]
    public void Snapshot_InvalidProjectSetting_IsAnError_AndGeneratesNothing(string key, string value, string message)
    {
        var run = RunGenerator(
            Snapshot(types: [ContentType("article", Element("title", "text"))]),
            settings: new Dictionary<string, string> { [key] = value });

        run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG005")
            .Which.GetMessage().Should().Contain(message);
        run.HintNames.Should().NotContain(ArticleModel);
    }

    [Theory]
    [InlineData("""{ "format_version": "2.0", "kinds": ["types", "snippets"] }""", "format_version 2.0 isn't supported")]
    [InlineData("""{ "format_version": "1.0", "kinds": ["types"] }""", "doesn't contain snippets")]
    [InlineData("""{ "format_version": "1.0" }""", "'kinds' is missing")]
    [InlineData("""{ "name": "my-web-app" }""", "'format_version' is missing")]
    [InlineData("""{ "format_version": """, "invalid JSON")]
    public void Snapshot_UnusableManifest_IsAnError_AndGeneratesNothing(string manifest, string message)
    {
        var run = RunGenerator(Snapshot(manifest: manifest, types: [ContentType("article", Element("title", "text"))]));

        var error = run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG005").Which;
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().Contain(message);
        error.Location.GetLineSpan().Path.Should().Be("model-snapshot/manifest.json");
        run.HintNames.Should().NotContain(ArticleModel).And.NotContain("GeneratedTypeProvider.g.cs");
    }

    [Fact]
    public void Snapshot_MissingManifest_SaysWhereTheSnapshotShouldBe()
    {
        // The package's targets list manifest.json even when it doesn't exist, so a wrong path reads as a missing file.
        var run = RunGenerator([new SnapshotText("model-snapshot/manifest.json", "manifest", null)]);

        run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG005")
            .Which.GetMessage().Should().Contain("missing or can't be read").And.Contain("kontent model snapshot");
    }

    [Fact]
    public void Snapshot_MalformedContentTypeFile_IsAnErrorNamingTheFile_AndTheOtherModelsStillGenerate()
    {
        var run = RunGenerator(Snapshot(types:
        [
            ContentType("article", Element("title", "text")),
            new SnapshotText("model-snapshot/types/broken.json", "types", """{ "codename": "broken", """),
        ]));

        run.Diagnostics.Should().ContainSingle(d => d.Id == "KDSG005")
            .Which.GetMessage().Should().Contain("model-snapshot/types/broken.json").And.Contain("invalid JSON");
        run.HintNames.Should().Contain(ArticleModel);
    }

    [Fact]
    public void Snapshot_AcceptsWindowsLineEndings()
    {
        // Git on Windows may convert line endings on checkout.
        var json = ContentType("article", Element("title", "text")).Content!.Replace("\n", "\r\n");

        var run = RunGenerator(Snapshot(types: [new SnapshotText("model-snapshot/types/article.json", "types", json)]));

        run.Diagnostics.Should().BeEmpty();
        run.Source(ArticleModel).Should().Contain("Title { get; init; }");
    }

    [Fact]
    public void ContentModelIds_DoNotChangeTheGeneratedCode()
    {
        // An export/import gives a content model new ids, and a sync gives them to what it adds. Delivery models are
        // keyed by codename, so a snapshot of either environment has to generate the same code.
        var snapshot = SampleSnapshot();
        var sameModelOtherIds = snapshot
            .Select(file => new SnapshotText(file.Path, file.Kind, file.Content!
                .Replace("-id\"", "-id-in-another-environment\"")
                .Replace("000000000001", "000000000002")
                .Replace("2026-01-01T00:00:00Z", "2026-06-30T12:00:00Z")))
            .ToImmutableArray();
        sameModelOtherIds.Select(file => file.Content).Should().NotIntersectWith(snapshot.Select(file => file.Content));

        var original = RunGenerator(snapshot);
        var other = RunGenerator(sameModelOtherIds);

        other.Diagnostics.Should().BeEmpty();
        GeneratedCode(other).Should().Equal(GeneratedCode(original));
    }

    [Fact]
    public void WithoutSnapshot_OtherAdditionalFilesChangeNothing()
    {
        var declared = """
            using Kontent.Ai.Delivery.Attributes;

            namespace App;

            [ContentTypeCodename("article")]
            public sealed class Article;
            """;

        var withoutFiles = RunGenerator([], declared);
        var withUnrelatedFile = RunGenerator([new SnapshotText("stylecop.json", string.Empty, "{}")], declared);

        withUnrelatedFile.Diagnostics.Should().BeEmpty();
        withUnrelatedFile.HintNames.Should().BeEquivalentTo("ContentTypeCodenameAttribute.g.cs", "GeneratedTypeProvider.g.cs");
        withUnrelatedFile.Provider.Should().Be(withoutFiles.Provider);
    }

    [Fact]
    public void EditingCSharp_NeitherRereadsTheSnapshotNorRewritesAModel()
    {
        var run = RunGenerator(SampleSnapshot(), "namespace App; public class Service { }");

        var edited = run.Input.ReplaceSyntaxTree(
            run.Input.SyntaxTrees.Single(),
            CSharpSyntaxTree.ParseText("namespace App; public class Service { public int Count { get; set; } }", ParseOptions));
        var rerun = run.Driver.RunGenerators(edited).GetRunResult().Results.Single();

        Reasons(rerun, "SnapshotFiles").Should().NotBeEmpty().And.OnlyContain(reason => reason == IncrementalStepRunReason.Cached);
        Reasons(rerun, "SnapshotModels").Should().NotBeEmpty().And.OnlyContain(reason => reason == IncrementalStepRunReason.Cached);
        Reasons(rerun, "SnapshotOutputs").Should().NotBeEmpty().And.OnlyContain(reason => reason == IncrementalStepRunReason.Cached);
    }

    [Fact]
    public void EditingOneContentTypeFile_RebuildsOnlyThatModel()
    {
        var files = SampleSnapshot();
        var run = RunGenerator(files);

        var article = files.Single(file => file.Path.EndsWith("article.json", StringComparison.Ordinal));
        var driver = run.Driver.ReplaceAdditionalText(article, ContentType("article", Element("title", "text"), Element("summary", "text")));
        var rerun = driver.RunGenerators(run.Input).GetRunResult().Results.Single();

        Reasons(rerun, "SnapshotFiles").Count(reason => reason == IncrementalStepRunReason.Modified).Should().Be(1);
        Reasons(rerun, "SnapshotModels").Count(reason => reason == IncrementalStepRunReason.Modified).Should().Be(1);
        Reasons(rerun, "SnapshotOutputs").Count(reason => reason == IncrementalStepRunReason.Modified).Should().Be(1);
        rerun.GeneratedSources.Single(source => source.HintName == ArticleModel).SourceText.ToString().Should().Contain("Summary");
    }

    [Fact]
    public void EditingASnippet_RewritesOnlyTheModelsThatUseIt()
    {
        var files = SampleSnapshot();
        var run = RunGenerator(files);

        var snippet = files.Single(file => file.Kind == "snippets");
        var driver = run.Driver.ReplaceAdditionalText(snippet, Snippet("snippet-id", "metadata", Element("metadata__title", "text"), Element("metadata__image", "asset")));
        var rerun = driver.RunGenerators(run.Input).GetRunResult().Results.Single();

        Reasons(rerun, "SnapshotOutputs").Count(reason => reason == IncrementalStepRunReason.Modified).Should().Be(1);
        rerun.GeneratedSources.Single(source => source.HintName == "KontentModels.Page.g.cs").SourceText.ToString().Should().Contain("MetadataImage");
    }

    private static ImmutableArray<SnapshotText> SampleSnapshot() => Snapshot(
        types:
        [
            ContentType("article", Element("title", "text")),
            ContentType("author", Element("name", "text")),
            ContentType("page", Element("headline", "text"), SnippetReference("metadata", "snippet-id")),
        ],
        snippets: [Snippet("snippet-id", "metadata", Element("metadata__title", "text"))]);

    private static ImmutableArray<SnapshotText> Snapshot(
        IEnumerable<SnapshotText>? types = null,
        IEnumerable<SnapshotText>? snippets = null,
        string manifest = """{ "format_version": "1.0", "environment_id": "00000000-0000-0000-0000-000000000001", "kinds": ["types", "snippets"] }""") =>
        [new SnapshotText("model-snapshot/manifest.json", "manifest", manifest), .. types ?? [], .. snippets ?? []];

    private static SnapshotText ContentType(string codename, params string[] elements) =>
        new($"model-snapshot/types/{codename}.json", "types", Entity($"{codename}-id", codename, elements));

    private static SnapshotText Snippet(string id, string codename, params string[] elements) =>
        new($"model-snapshot/snippets/{codename}.json", "snippets", Entity(id, codename, elements));

    private static string Entity(string id, string codename, string[] elements) => $$"""
        {
          "id": "{{id}}",
          "codename": "{{codename}}",
          "last_modified": "2026-01-01T00:00:00Z",
          "name": "{{codename}}",
          "elements": [{{string.Join(",", elements)}}]
        }
        """;

    private static string Element(string codename, string type) =>
        $$"""{ "id": "{{codename}}-id", "codename": "{{codename}}", "type": "{{type}}", "name": "{{codename}}", "guidelines": null }""";

    private static string SnippetReference(string codename, string snippetId) =>
        $$"""{ "id": "{{codename}}-id", "codename": "{{codename}}", "type": "snippet", "snippet": { "id": "{{snippetId}}" } }""";

    private static IEnumerable<(string HintName, string Text)> GeneratedCode(GeneratorRun run) =>
        run.Result.GeneratedSources
            .Select(source => (source.HintName, source.SourceText.ToString()))
            .OrderBy(source => source.HintName, StringComparer.Ordinal);

    private static IEnumerable<IncrementalStepRunReason> Reasons(GeneratorRunResult result, string step) =>
        result.TrackedSteps[step].SelectMany(run => run.Outputs).Select(output => output.Reason);

    private static GeneratorRun RunGenerator(
        IEnumerable<SnapshotText> files,
        string source = "namespace App;",
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
            .Concat(
            [
                typeof(Abstractions.ITypeProvider).Assembly.Location,
                typeof(ContentItems.Asset).Assembly.Location,
                typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute).Assembly.Location,
            ])
            .Distinct()
            .Select(location => MetadataReference.CreateFromFile(location));

        var input = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, ParseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new ContentTypeGenerator().AsSourceGenerator()],
            additionalTexts: files,
            parseOptions: ParseOptions,
            optionsProvider: new TestOptionsProvider(settings ?? new Dictionary<string, string>()),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        var result = driver.GetRunResult();

        // Whatever the generator emits must compile, warnings included: a model is code in someone else's build.
        output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree is { } tree && result.GeneratedTrees.Contains(tree))
            .Should().BeEmpty();

        return new GeneratorRun(driver, input, diagnostics, result.Results.Single());
    }

    private sealed record GeneratorRun(GeneratorDriver Driver, Compilation Input, ImmutableArray<Diagnostic> Diagnostics, GeneratorRunResult Result)
    {
        public IEnumerable<string> HintNames => Result.GeneratedSources.Select(source => source.HintName);

        public string Provider => Result.GeneratedSources
            .Where(source => source.HintName == "GeneratedTypeProvider.g.cs")
            .Select(source => source.SourceText.ToString())
            .SingleOrDefault() ?? string.Empty;

        public string Source(string hintName) => Result.GeneratedSources.Single(source => source.HintName == hintName).SourceText.ToString();
    }

    /// <summary>A file the package's targets pass as an AdditionalFile, with the snapshot folder it came from.</summary>
    private sealed class SnapshotText(string path, string kind, string? content) : AdditionalText
    {
        public string Kind { get; } = kind;

        public string? Content { get; } = content;

        public override string Path { get; } = path;

        public override SourceText? GetText(CancellationToken cancellationToken = default) =>
            Content is null ? null : SourceText.From(Content, Encoding.UTF8);
    }

    private sealed class TestOptionsProvider(IReadOnlyDictionary<string, string> global) : AnalyzerConfigOptionsProvider
    {
        private static readonly TestOptions None = new(new Dictionary<string, string>());

        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestOptions(global);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => None;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
            textFile is SnapshotText { Kind.Length: > 0 } snapshot
                ? new TestOptions(new Dictionary<string, string> { [SnapshotKindMetadata] = snapshot.Kind })
                : None;
    }

    private sealed class TestOptions(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
