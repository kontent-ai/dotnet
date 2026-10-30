using Microsoft.CodeAnalysis;

namespace Kontent.Ai.Delivery.SourceGeneration;

/// <summary>
/// Diagnostic descriptors for the ContentType source generator.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "Kontent.Ai.Delivery.SourceGeneration";

    /// <summary>
    /// KDSG001: Duplicate codename used by multiple types.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateCodename = new(
        id: "KDSG001",
        title: "Duplicate content type codename",
        messageFormat: "Duplicate codename '{0}' used by: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Each content type codename must be unique across all types decorated with [ContentTypeCodename].");

    /// <summary>
    /// KDSG002: Invalid codename - null, empty, or whitespace.
    /// </summary>
    public static readonly DiagnosticDescriptor InvalidCodename = new(
        id: "KDSG002",
        title: "Invalid content type codename",
        messageFormat: "Invalid codename: cannot be null, empty, or whitespace",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The content type codename must be a non-empty string.");

    /// <summary>
    /// KDSG003: Unsupported target type - interface, abstract class, struct or generic type.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedTargetType = new(
        id: "KDSG003",
        title: "Unsupported target type for [ContentTypeCodename]",
        messageFormat: "[ContentTypeCodename] cannot be applied to '{0}': a content model must be a concrete class or record class, not an interface, an abstract class, a struct or a generic type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "[ContentTypeCodename] can only be applied to concrete, non-generic classes. The SDK creates the model and hydrates elements on that instance: a struct's values would be lost in a copy, and an open generic type cannot be created.");

    /// <summary>
    /// KDSG004: Inaccessible target type - private, protected or file-local, or nested in such a type.
    /// </summary>
    public static readonly DiagnosticDescriptor InaccessibleTargetType = new(
        id: "KDSG004",
        title: "Inaccessible target type for [ContentTypeCodename]",
        messageFormat: "[ContentTypeCodename] cannot be applied to '{0}': the generated type provider cannot reference it, so it and every type containing it must be public or internal, and not file-local",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The generated type provider refers to every content model from its own namespace, so it can map only types the rest of the assembly can access.");
}
