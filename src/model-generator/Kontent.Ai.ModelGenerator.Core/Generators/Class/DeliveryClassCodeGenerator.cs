using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Helpers;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Kontent.Ai.ModelGenerator.Core.Generators.Class;

public sealed class DeliveryClassCodeGenerator(ClassDefinition classDefinition, string classFilename, string? @namespace = ClassCodeGenerator.DefaultNamespace)
    : ClassCodeGenerator(classDefinition, classFilename, @namespace)
{
    protected override TypeDeclarationSyntax GetClassDeclaration()
    {
        var recordDeclaration = base.GetClassDeclaration();

        recordDeclaration = recordDeclaration.AddAttributeLists(
            SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(
                        SyntaxFactory.IdentifierName("ContentTypeCodename"),
                        SyntaxFactory.AttributeArgumentList(
                            SyntaxFactory.SingletonSeparatedList(
                                SyntaxFactory.AttributeArgument(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal(ClassDefinition.Codename)))))))));

        recordDeclaration = recordDeclaration.AddMembers(GetPropertyCodenameConstants());
        recordDeclaration = recordDeclaration.AddMembers(GetContentTypeCodenameConstant());
        recordDeclaration = recordDeclaration.AddMembers(GetProperties());

        return recordDeclaration;
    }

    private MemberDeclarationSyntax GetContentTypeCodenameConstant() =>
        GetFieldDeclaration("string", ClassDefinition.ContentTypeCodenameIdentifier, ClassDefinition.Codename);

    protected override UsingDirectiveSyntax[] GetApiUsings() =>
    [
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Collections.Generic")),
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Text.Json.Serialization")),
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Kontent.Ai.Delivery.Abstractions")),
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Kontent.Ai.Delivery.Attributes")),
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Kontent.Ai.Delivery.ContentItems")),
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Kontent.Ai.Delivery.ContentItems.RichText")),
        SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Kontent.Ai.Delivery.SharedModels"))
    ];

    protected override AttributeListSyntax[] BuildPropertyAttributes(Property property) =>
    [
        SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Attribute(
                    SyntaxFactory.IdentifierName("JsonPropertyName"),
                    SyntaxFactory.AttributeArgumentList(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.AttributeArgument(
                                SyntaxFactory.LiteralExpression(
                                    SyntaxKind.StringLiteralExpression,
                                    SyntaxFactory.Literal(property.Codename))))))))
    ];

    private MemberDeclarationSyntax[] GetPropertyCodenameConstants()
        => ClassDefinition.PropertyCodenameConstants
            .OrderBy(p => p)
            .Select(codename =>
            {
                var identifier = $"{TextHelpers.GetValidPascalCaseIdentifierName(codename)}Codename";
                if (ClassDefinition.RenamedCodenameConstants.Contains(codename))
                {
                    identifier = "_" + identifier;
                }

                return GetFieldDeclaration("string", identifier, codename);
            })
            .ToArray<MemberDeclarationSyntax>();

    private static FieldDeclarationSyntax GetFieldDeclaration(string typeName, string identifier, string literal) =>
        SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName(typeName),
                    SyntaxFactory.SeparatedList(
                    [
                        SyntaxFactory.VariableDeclarator(
                            SyntaxFactory.Identifier(
                                identifier),
                            null,
                            SyntaxFactory.EqualsValueClause(SyntaxFactory.LiteralExpression(
                                SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(literal)))
                        )
                    ])
                )
            )
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.ConstKeyword));
}
