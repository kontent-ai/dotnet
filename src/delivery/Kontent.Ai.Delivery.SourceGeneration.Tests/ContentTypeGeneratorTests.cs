using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Kontent.Ai.Delivery.SourceGeneration.Tests;

public class ContentTypeGeneratorTests
{
    private static IReadOnlyList<string> LastRunTrees { get; set; } = [];

    [Fact]
    public void Generator_EmitsTheMarkerAttributeAsInternal()
    {
        // Every referencing assembly gets its own copy of this attribute. A public one puts the same
        // type name in each of them, so two such projects referencing each other stop compiling with
        // CS0436/CS0433 - and the fix would be for the consumer to drop a project reference.
        RunGenerator("namespace TestApp.Models;");

        var attribute = LastRunTrees.Single(tree => tree.Contains("class ContentTypeCodenameAttribute", StringComparison.Ordinal));

        attribute.Should().Contain("internal sealed class ContentTypeCodenameAttribute");
        attribute.Should().NotContain("public sealed class ContentTypeCodenameAttribute");
    }

    [Fact]
    public void Generator_WithValidContentTypes_GeneratesRegistry()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("article")]
            public class Article { }

            [ContentTypeCodename("home")]
            public record Home { }
            """;

        // Act
        var (diagnostics, output) = RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().Contain("class GeneratedTypeProvider : ITypeProvider");
        output.Should().Contain("\"article\"");
        output.Should().Contain("\"home\"");
        output.Should().Contain("typeof(global::TestApp.Models.Article)");
        output.Should().Contain("typeof(global::TestApp.Models.Home)");
    }

    [Fact]
    public void Generator_WithNoContentTypes_EmitsNoProvider()
    {
        // Runtime discovery checks the app before its references. An empty provider in an app whose models live in
        // a referenced project would be found first and map nothing.
        var source = """
            namespace TestApp.Models;

            public class Article { }
            """;

        var (diagnostics, output) = RunGenerator(source);

        diagnostics.Should().BeEmpty();
        output.Should().BeEmpty();
        LastRunTrees.Should().ContainSingle(tree => tree.Contains("class ContentTypeCodenameAttribute", StringComparison.Ordinal));
    }

    [Fact]
    public void Generator_WithEveryModelRejected_ReportsDiagnosticsAndEmitsNoProvider()
    {
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("")]
            public class Article { }

            [ContentTypeCodename("home")]
            public struct Home { }
            """;

        var (diagnostics, output) = RunGenerator(source);

        diagnostics.Select(d => d.Id).Should().BeEquivalentTo("KDSG002", "KDSG003");
        output.Should().BeEmpty();
    }

    [Fact]
    public void Generator_WithDuplicateCodename_ReportsError()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("article")]
            public class Article { }

            [ContentTypeCodename("article")]
            public class ArticleDuplicate { }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        diagnostics.Any(d => d.Id == "KDSG001").Should().BeTrue();
        diagnostics.Any(d => d.GetMessage().Contains("Duplicate codename 'article'")).Should().BeTrue();
    }

    [Fact]
    public void Generator_WithEmptyCodename_ReportsError()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("")]
            public class Article { }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        diagnostics.Any(d => d.Id == "KDSG002").Should().BeTrue();
    }

    [Fact]
    public void Generator_WithWhitespaceCodename_ReportsError()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("   ")]
            public class Article { }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        diagnostics.Any(d => d.Id == "KDSG002").Should().BeTrue();
    }

    [Fact]
    public void Generator_WithInterface_ReportsError()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("article")]
            public interface IArticle { }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        diagnostics.Any(d => d.Id == "KDSG003").Should().BeTrue();
        diagnostics.Any(d => d.GetMessage().Contains("IArticle")).Should().BeTrue();
    }

    [Fact]
    public void Generator_WithAbstractClass_ReportsError()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("article")]
            public abstract class Article { }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        diagnostics.Any(d => d.Id == "KDSG003").Should().BeTrue();
        diagnostics.Any(d => d.GetMessage().Contains("Article")).Should().BeTrue();
    }

    [Fact]
    public void Generator_WithStruct_ReportsUnsupportedTargetType()
    {
        // A struct compiles and deserializes, but the SDK hydrates elements on the deserialized instance and
        // a struct's would be lost in a copy - so it is refused at build time rather than emptied at runtime.
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("article")]
            public struct Article { }
            """;

        var (diagnostics, output) = RunGenerator(source);

        diagnostics.Should().ContainSingle(d => d.Id == "KDSG003").Which.GetMessage().Should().Contain("Article");
        output.Should().NotContain("typeof(global::TestApp.Models.Article)");
    }

    [Fact]
    public void Generator_CaseInsensitiveCodenameComparison_DetectsDuplicates()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("Article")]
            public class Article { }

            [ContentTypeCodename("ARTICLE")]
            public class ArticleUpperCase { }
            """;

        // Act
        var (diagnostics, _) = RunGenerator(source);

        // Assert
        diagnostics.Any(d => d.Id == "KDSG001").Should().BeTrue();
    }

    [Fact]
    public void Generator_OrdersEntriesAlphabetically()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            [ContentTypeCodename("zebra")]
            public class Zebra { }

            [ContentTypeCodename("apple")]
            public class Apple { }

            [ContentTypeCodename("mango")]
            public class Mango { }
            """;

        // Act
        var (diagnostics, output) = RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        var appleIndex = output.IndexOf("\"apple\"");
        var mangoIndex = output.IndexOf("\"mango\"");
        var zebraIndex = output.IndexOf("\"zebra\"");

        appleIndex.Should().BeLessThan(mangoIndex);
        mangoIndex.Should().BeLessThan(zebraIndex);
    }

    [Fact]
    public void Generator_WithNestedClass_GeneratesFullyQualifiedName()
    {
        // Arrange
        var source = """
            using Kontent.Ai.Delivery.Attributes;

            namespace TestApp.Models;

            public class Outer
            {
                [ContentTypeCodename("nested_article")]
                public class NestedArticle { }
            }
            """;

        // Act
        var (diagnostics, output) = RunGenerator(source);

        // Assert
        diagnostics.Should().BeEmpty();
        output.Should().Contain("\"nested_article\"");
        output.Should().Contain("typeof(global::TestApp.Models.Outer.NestedArticle)");
    }

    [Theory]
    [InlineData("""[ContentTypeCodename("article")] public class Article<T> { }""")]
    [InlineData("""public class Outer<T> { [ContentTypeCodename("article")] public class Article { } }""")]
    public void Generator_WithGenericType_ReportsUnsupportedTargetType(string declaration)
    {
        var (diagnostics, output) = RunGenerator(ModelSource(declaration));

        diagnostics.Should().ContainSingle(d => d.Id == "KDSG003").Which.GetMessage().Should().Contain("Article");
        output.Should().NotContain("\"article\"");
    }

    [Theory]
    [InlineData("""public class Outer { [ContentTypeCodename("article")] private class Article { } }""")]
    [InlineData("""public class Outer { [ContentTypeCodename("article")] protected class Article { } }""")]
    [InlineData("""public class Outer { [ContentTypeCodename("article")] private protected class Article { } }""")]
    [InlineData("""public class Outer { private class Inner { [ContentTypeCodename("article")] public class Article { } } }""")]
    [InlineData("""[ContentTypeCodename("article")] file class Article { }""")]
    public void Generator_WithInaccessibleType_ReportsInaccessibleTargetType(string declaration)
    {
        var (diagnostics, output) = RunGenerator(ModelSource(declaration));

        diagnostics.Should().ContainSingle(d => d.Id == "KDSG004").Which.GetMessage().Should().Contain("Article");
        output.Should().NotContain("\"article\"");
    }

    [Theory]
    [InlineData("""[ContentTypeCodename("article")] internal class Article { }""", "global::TestApp.Models.Article")]
    [InlineData("""public class Outer { [ContentTypeCodename("article")] protected internal class Article { } }""", "global::TestApp.Models.Outer.Article")]
    [InlineData("""internal class Outer { [ContentTypeCodename("article")] internal class Article { } }""", "global::TestApp.Models.Outer.Article")]
    public void Generator_WithTypeTheAssemblyCanAccess_GeneratesRegistry(string declaration, string typeName)
    {
        var (diagnostics, output) = RunGenerator(ModelSource(declaration));

        diagnostics.Should().BeEmpty();
        output.Should().Contain($"typeof({typeName})");
    }

    private static string ModelSource(string declaration) => $"""
        using Kontent.Ai.Delivery.Attributes;

        namespace TestApp.Models;

        {declaration}
        """;

    private static (ImmutableArray<Diagnostic> Diagnostics, string Output) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Add reference to Abstractions assembly for ITypeProvider
        references.Add(MetadataReference.CreateFromFile(typeof(Abstractions.ITypeProvider).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new ContentTypeGenerator();

        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        // Whatever the generator emits must compile. Errors in a test's own source - an attribute on an interface is
        // CS0592 - are that test's business.
        outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error && d.Location.SourceTree is { } tree && runResult.GeneratedTrees.Contains(tree))
            .Should().BeEmpty();
        LastRunTrees = [.. runResult.GeneratedTrees.Select(tree => tree.GetText().ToString())];
        var generatedSource = runResult.GeneratedTrees
            .Select(tree => tree.GetText().ToString())
            .FirstOrDefault(text => text.Contains("class GeneratedTypeProvider", StringComparison.Ordinal))
            ?? string.Empty;

        return (diagnostics, generatedSource);
    }
}
