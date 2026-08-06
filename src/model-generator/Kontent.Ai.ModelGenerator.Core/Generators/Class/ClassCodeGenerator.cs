using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Helpers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Kontent.Ai.ModelGenerator.Core.Generators.Class;

public abstract class ClassCodeGenerator : GeneralGenerator
{
    public const string DefaultNamespace = "KontentAiModels";

    public ClassDefinition ClassDefinition { get; }

    public string ClassFilename { get; }

    protected ClassCodeGenerator(ClassDefinition classDefinition, string classFilename, string? @namespace = DefaultNamespace) : base(@namespace)
    {
        ClassDefinition = classDefinition ?? throw new ArgumentNullException(nameof(classDefinition));
        ClassFilename = string.IsNullOrWhiteSpace(classFilename) ? ClassDefinition.ClassName : classFilename;
    }

    public string GenerateCode()
    {
        var usings = GetApiUsings();
        var classDeclaration = GetClassDeclaration();

        var compilationUnit = GetCompilationUnit(classDeclaration, usings);

        var customWorkspace = new AdhocWorkspace();
        return Formatter.Format(compilationUnit, customWorkspace).ToFullString().NormalizeLineEndings();
    }

    protected abstract UsingDirectiveSyntax[] GetApiUsings();

    /// <summary>
    /// Returns the attribute lists to apply to each emitted property.
    /// Override in subclasses to inject SDK-specific attributes (e.g. <c>[JsonPropertyName]</c> for Delivery,
    /// <c>[KontentElement]</c> + constraint attributes for Management). Default emits nothing.
    /// </summary>
    protected virtual AttributeListSyntax[] BuildPropertyAttributes(Property property) => [];

    protected virtual MemberDeclarationSyntax[] GetProperties()
        => ClassDefinition.Properties.OrderBy(p => p.Identifier).Select(element =>
        {
            var property = SyntaxFactory
                .PropertyDeclaration(SyntaxFactory.ParseTypeName(element.TypeName), element.Identifier)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

            var attributeLists = BuildPropertyAttributes(element);
            if (attributeLists.Length > 0)
            {
                property = property.AddAttributeLists(attributeLists);
            }

            property = property.AddAccessorListAccessors(
                GetAccessorDeclaration(SyntaxKind.GetAccessorDeclaration),
                GetAccessorDeclaration(SyntaxKind.InitAccessorDeclaration));

            // Emit explicit initializer (e.g. = string.Empty / = [] / = RichTextContent.Empty)
            // when the Property carries one. Used by Semantic nullability mode.
            var initializer = element.Initializer;
            if (!string.IsNullOrEmpty(initializer))
            {
                property = property.WithInitializer(
                    SyntaxFactory.EqualsValueClause(SyntaxFactory.ParseExpression(initializer)))
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
            }

            return property;
        }).ToArray<MemberDeclarationSyntax>();

    protected virtual TypeDeclarationSyntax GetClassDeclaration()
    {
        TypeDeclarationSyntax typeDeclaration = SyntaxFactory.RecordDeclaration(
            attributeLists: default,
            modifiers: default,
            keyword: SyntaxFactory.Token(SyntaxKind.RecordKeyword),
            identifier: SyntaxFactory.Identifier(ClassDefinition.ClassName),
            typeParameterList: null,
            parameterList: null,
            baseList: null,
            constraintClauses: default,
            openBraceToken: SyntaxFactory.Token(SyntaxKind.OpenBraceToken),
            members: default,
            closeBraceToken: SyntaxFactory.Token(SyntaxKind.CloseBraceToken),
            semicolonToken: default);

        return typeDeclaration
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword));
    }

    protected override SyntaxTrivia ClassDescription() =>
        ClassDeclarationHelper.GenerateSyntaxTrivia(
            @$"{LostChangesComment}
// To extend this record, create a separate partial record with the same name.");

    protected static AccessorDeclarationSyntax GetAccessorDeclaration(SyntaxKind kind) =>
        SyntaxFactory.AccessorDeclaration(kind).WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

    /// <summary>
    /// Returns sibling type declarations (e.g. enums) to emit alongside the main type, under
    /// the same namespace. Default empty. Management mode overrides to emit per-element enums
    /// for multiple-choice fields.
    /// </summary>
    protected virtual MemberDeclarationSyntax[] GetAdditionalNamespaceMembers() => [];

    private CompilationUnitSyntax GetCompilationUnit(TypeDeclarationSyntax classDeclaration, UsingDirectiveSyntax[] usings)
    {
        var siblingTypes = GetAdditionalNamespaceMembers();
        var namespaceMembers = new MemberDeclarationSyntax[1 + siblingTypes.Length];
        namespaceMembers[0] = classDeclaration;
        for (var i = 0; i < siblingTypes.Length; i++)
        {
            namespaceMembers[i + 1] = siblingTypes[i];
        }

        var fileScopedNamespace = SyntaxFactory.FileScopedNamespaceDeclaration(
                SyntaxFactory.IdentifierName(Namespace))
            .AddMembers(namespaceMembers);

        var compilationUnit = SyntaxFactory.CompilationUnit()
            .AddUsings(usings)
            .AddMembers(fileScopedNamespace);

        var leadingTrivia = SyntaxFactory.TriviaList(
            ClassDescription(),
            SyntaxFactory.Trivia(SyntaxFactory.NullableDirectiveTrivia(
                SyntaxFactory.Token(SyntaxKind.EnableKeyword), isActive: true)),
            SyntaxFactory.CarriageReturnLineFeed,
            SyntaxFactory.CarriageReturnLineFeed);

        compilationUnit = compilationUnit.WithLeadingTrivia(leadingTrivia);

        return compilationUnit;
    }
}
