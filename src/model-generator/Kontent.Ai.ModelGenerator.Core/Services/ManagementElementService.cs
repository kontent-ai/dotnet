using Kontent.Ai.Management.Models.Types.Elements;
using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Helpers;

namespace Kontent.Ai.ModelGenerator.Core.Services;

/// <summary>
/// Maps Management API element inputs to <see cref="ManagementElementOutput"/> records ready for
/// emission. Each element projects to a property carrying a single <c>[KontentElement]</c> identity
/// attribute (multiple-choice options additionally carry <c>[KontentEnumValue]</c>); content-model
/// constraints are enforced server-side by the Management API, not mirrored onto the generated type.
/// The element subtype selects the C# value type.
/// </summary>
public static class ManagementElementService
{
    public static ManagementElementOutput Build(ManagementElementInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input switch
        {
            TextElementInput t => new ManagementElementOutput(BuildSimple(t.Codename, t.Id, "string?")),
            NumberElementInput n => new ManagementElementOutput(BuildSimple(n.Codename, n.Id, "decimal?")),
            DateTimeElementInput d => new ManagementElementOutput(BuildSimple(d.Codename, d.Id, "DateTimeValue?")),
            CustomElementInput c => new ManagementElementOutput(BuildSimple(c.Codename, c.Id, "CustomValue?")),
            UrlSlugElementInput u => new ManagementElementOutput(BuildSimple(u.Codename, u.Id, "UrlSlugValue?")),
            MultipleChoiceElementInput m => BuildMultipleChoice(m),
            LinkedItemsElementInput li => new ManagementElementOutput(BuildSimple(li.Codename, li.Id, "IEnumerable<Reference>?")),
            SubpagesElementInput sp => new ManagementElementOutput(BuildSimple(sp.Codename, sp.Id, "IEnumerable<Reference>?")),
            TaxonomyElementInput tx => new ManagementElementOutput(BuildSimple(tx.Codename, tx.Id, "IEnumerable<Reference>?")),
            RichTextElementInput rt => new ManagementElementOutput(BuildSimple(rt.Codename, rt.Id, "RichTextValue?")),
            AssetElementInput a => new ManagementElementOutput(BuildSimple(a.Codename, a.Id, "IEnumerable<AssetReference>?")),
            _ => throw new ArgumentException(
                $"Unsupported management element input type: {input.GetType().Name}",
                nameof(input)),
        };
    }

    private static ManagementProperty BuildSimple(string codename, string id, string typeName) =>
        new(codename, typeName, id, [KontentElement(codename, id)]);

    private static ManagementElementOutput BuildMultipleChoice(MultipleChoiceElementInput input)
    {
        if (string.IsNullOrWhiteSpace(input.EnumTypeName))
        {
            throw new ArgumentException(
                $"Multiple-choice element '{input.Codename}' has no EnumTypeName — the orchestrator must set one.",
                nameof(input));
        }

        // The wire carries an array either way; a single-select element is one option or none, and its property says so.
        var typeName = input.Mode == MultipleChoiceMode.Single
            ? $"{input.EnumTypeName}?"
            : $"IEnumerable<{input.EnumTypeName}>?";

        var property = new ManagementProperty(
            input.Codename,
            typeName,
            input.Id,
            [KontentElement(input.Codename, input.Id)]);

        var members = input.Options.Select(opt => new EnumMember(
            identifier: TextHelpers.GetValidPascalCaseIdentifierName(opt.Codename),
            attributes:
            [
                new AttributeSpec("KontentEnumValue",
                [
                    AttributeArg.Positional(opt.Codename),
                    AttributeArg.Positional(opt.Id),
                ])
            ])).ToList();

        var enumDef = new EnumDefinition(input.EnumTypeName, members);

        return new ManagementElementOutput(property, [enumDef]);
    }

    private static AttributeSpec KontentElement(string codename, string id) =>
        new("KontentElement",
        [
            AttributeArg.Positional(codename),
            AttributeArg.Positional(id),
        ]);
}
