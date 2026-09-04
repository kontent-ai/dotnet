using Kontent.Ai.Management.Models.Types.Elements;
using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Helpers;

namespace Kontent.Ai.ModelGenerator.Core.Services;

/// <summary>
/// Adapts MAPI's <see cref="ElementMetadataBase"/> hierarchy into the
/// <see cref="ManagementElementInput"/> records that <see cref="ManagementElementService"/> consumes.
/// Pure function. Returns <c>null</c> for element types that the current generator slice doesn't
/// emit yet (the orchestrator turns those into warn-and-skip).
/// </summary>
internal static class ManagementElementMetadataAdapter
{
    /// <param name="element">
    /// The Management API element metadata to adapt. Element types the generator does not emit
    /// yet fall through to <c>null</c>.
    /// </param>
    /// <param name="contentTypeClassName">
    /// PascalCased name of the content-type class the element lives on. Used to build unique
    /// per-element enum names (<c>{ClassName}{PascalElementCodename}</c>) so the same multiple-choice
    /// element on two content types produces two distinct, collision-free enums. Unused for
    /// non-enum-producing element types.
    /// </param>
    public static ManagementElementInput? ToInput(ElementMetadataBase element, string contentTypeClassName)
    {
        // Codename and Id are nullable on the MAPI models because those shapes double as create
        // payloads, where the CMS fills both in. Every element of an EXISTING content type - the
        // only thing the generator ever reads - carries both. Resolving them lazily here keeps
        // the unsupported-type arm below returning null (warn-and-skip) rather than throwing.
        string Codename() => element.Codename ?? throw Missing("codename", element);
        string Id() => element.Id?.ToString() ?? throw Missing("id", element);

        return element switch
        {
            TextElementMetadataModel => new TextElementInput(Codename(), Id()),
            NumberElementMetadataModel => new NumberElementInput(Codename(), Id()),
            DateTimeElementMetadataModel => new DateTimeElementInput(Codename(), Id()),
            CustomElementMetadataModel => new CustomElementInput(Codename(), Id()),
            UrlSlugElementMetadataModel => new UrlSlugElementInput(Codename(), Id()),
            MultipleChoiceElementMetadataModel mc => new MultipleChoiceElementInput(
                Codename: Codename(),
                Id: Id(),
                EnumTypeName: BuildEnumTypeName(contentTypeClassName, Codename()),
                Options: (mc.Options ?? []).Select(o => new MultipleChoiceOptionInput(
                    o.Codename ?? throw Missing("option codename", element),
                    o.Id?.ToString() ?? throw Missing("option id", element))).ToList(),
                Mode: mc.Mode),
            LinkedItemsElementMetadataModel => new LinkedItemsElementInput(Codename(), Id()),
            SubpagesElementMetadataModel => new SubpagesElementInput(Codename(), Id()),
            TaxonomyElementMetadataModel => new TaxonomyElementInput(Codename(), Id()),
            RichTextElementMetadataModel => new RichTextElementInput(Codename(), Id()),
            AssetElementMetadataModel => new AssetElementInput(Codename(), Id()),
            _ => null,
        };
    }

    private static InvalidOperationException Missing(string field, ElementMetadataBase element) =>
        new($"Management element of type '{element.Type}' (codename '{element.Codename ?? "<none>"}') " +
            $"is missing its {field}. The Management API returns it on every element of an existing " +
            "content type, so this indicates an unexpected API response.");

    private static string BuildEnumTypeName(string contentTypeClassName, string elementCodename) =>
        contentTypeClassName + TextHelpers.GetValidPascalCaseIdentifierName(elementCodename);
}
