using System.Text.RegularExpressions;
using Kontent.Ai.Management.Models.Types.Elements;
using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Generators.Class;
using Kontent.Ai.ModelGenerator.Core.Services;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kontent.Ai.ModelGenerator.Core.Tests.Generators.Class;

/// <summary>
/// Pins the emitter against the Management SDK's public-API approval snapshot - the repository's
/// record of what that SDK actually exposes, and the one view of it available in both reference
/// modes. <c>IntegrationTest_EmittedCodeCompilesAgainstSdkStubs</c> proves the emitted code is
/// well-formed, but only against stubs this project writes itself, so the two can agree while both
/// disagree with the SDK. That is how the <c>Models.Shared</c> bug shipped: the stub put
/// <c>Reference</c> where the emitter's using said it was.
/// </summary>
public class ManagementEmittedTypesMatchSdkTests
{
    // Framework types the emitted code names. Everything else must come from the SDK snapshot.
    private static readonly HashSet<string> FrameworkTypes = ["IEnumerable"];

    [Fact]
    public void EveryEmittedSdkType_IsPublicInTheSdk_UnderAnImportedNamespace()
    {
        var code = EmitEveryElementKind();
        var root = CSharpSyntaxTree.ParseText(code).GetCompilationUnitRoot();

        var imported = root.Usings.Select(u => u.Name!.ToString()).ToHashSet();
        var declaredHere = root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Select(t => t.Identifier.Text).ToHashSet();
        var sdk = ReadApprovedSdkSurface();

        var unknown = new List<string>();
        var notImported = new List<string>();

        foreach (var name in ReferencedTypeNames(root).Except(declaredHere).Except(FrameworkTypes))
        {
            if (!sdk.TryGetValue(name, out var namespaces))
            {
                unknown.Add(name);
            }
            else if (!namespaces.Any(imported.Contains))
            {
                notImported.Add($"{name} (in {string.Join(", ", namespaces)})");
            }
        }

        unknown.Should().BeEmpty(
            $"the emitter may only name types the SDK exposes.\nEmitted code:\n{code}");
        notImported.Should().BeEmpty(
            $"the emitted file must import the namespace each type actually lives in.\nEmitted code:\n{code}");
    }

    [Fact]
    public void TheEmittedIdentityAttributes_AreTheOnesTheSdkShips()
    {
        var sdk = ReadApprovedSdkSurface();

        foreach (var attribute in (string[])["ContentTypeAttribute", "ContentElementAttribute", "ContentOptionAttribute"])
        {
            sdk.Should().ContainKey(attribute);
            sdk[attribute].Should().Contain("Kontent.Ai.Management.Annotations");
        }
    }

    private static string EmitEveryElementKind()
    {
        ManagementElementInput[] inputs =
        [
            new TextElementInput("title", "11111111-1111-1111-1111-111111111111"),
            new NumberElementInput("priority", "22222222-2222-2222-2222-222222222222"),
            new DateTimeElementInput("published_at", "33333333-3333-3333-3333-333333333333"),
            new CustomElementInput("widget", "44444444-4444-4444-4444-444444444444"),
            new UrlSlugElementInput("slug", "55555555-5555-5555-5555-555555555555"),
            new LinkedItemsElementInput("related", "66666666-6666-6666-6666-666666666666"),
            new SubpagesElementInput("children", "77777777-7777-7777-7777-777777777777"),
            new TaxonomyElementInput("categories", "88888888-8888-8888-8888-888888888888"),
            new RichTextElementInput("body", "99999999-9999-9999-9999-999999999999"),
            new AssetElementInput("featured_image", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            new MultipleChoiceElementInput("tone", "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "ArticleTone",
                [new MultipleChoiceOptionInput("warning", "cccccccc-cccc-cccc-cccc-cccccccccccc")],
                MultipleChoiceMode.Multiple),
        ];

        var classDefinition = new ClassDefinition("article") { Id = "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee" };

        foreach (var built in inputs.Select(ManagementElementService.Build))
        {
            classDefinition.AddProperty(built.Property);

            foreach (var enumDefinition in built.Enums)
            {
                classDefinition.AddEnum(enumDefinition);
            }
        }

        return new ManagementClassCodeGenerator(classDefinition, classDefinition.ClassName).GenerateCode();
    }

    private static IEnumerable<string> ReferencedTypeNames(CompilationUnitSyntax root) =>
        root.DescendantNodes()
            .SelectMany(node => node switch
            {
                AttributeSyntax a => [a.Name.ToString() + "Attribute"],
                PropertyDeclarationSyntax p => IdentifiersIn(p.Type),
                SimpleBaseTypeSyntax b => IdentifiersIn(b.Type),
                _ => Enumerable.Empty<string>(),
            })
            .Distinct();

    private static IEnumerable<string> IdentifiersIn(TypeSyntax type) =>
        type.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Select(i => i.Identifier.Text);

    /// <summary>
    /// Reads the Management approval snapshot, which prints one <c>// namespace</c> line before each
    /// type. A type name can appear under more than one namespace.
    /// </summary>
    private static Dictionary<string, string[]> ReadApprovedSdkSurface()
    {
        using var stream = typeof(ManagementEmittedTypesMatchSdkTests).Assembly
            .GetManifestResourceStream("ManagementPublicApi.approved.txt")
            ?? throw new InvalidOperationException("The Management approval snapshot is not embedded in this test assembly.");
        using var reader = new StreamReader(stream);

        var byName = new Dictionary<string, List<string>>();
        var currentNamespace = string.Empty;

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("// "))
            {
                currentNamespace = line[3..].Trim();
                continue;
            }

            var match = Regex.Match(line, @"^public (?:\w+ )*(?:class|record|interface|struct|enum) (\w+)");
            if (match.Success)
            {
                byName.TryAdd(match.Groups[1].Value, []);
                byName[match.Groups[1].Value].Add(currentNamespace);
            }
        }

        return byName.ToDictionary(kv => kv.Key, kv => kv.Value.Distinct().ToArray());
    }
}
