using Kontent.Ai.ModelGenerator.Core.Helpers;

namespace Kontent.Ai.ModelGenerator.Core.Common;

public class ClassDefinition(string codeName)
{
    public const string ContentTypeCodenameIdentifier = "ContentTypeCodename";

    private const string CodenameConstantSuffix = "Codename";

    /// <summary>
    /// Which codename claimed each emitted identifier.
    /// </summary>
    /// <remarks>
    /// A property, its codename constant and the type's own <see cref="ContentTypeCodenameIdentifier"/>
    /// constant all become members of one record, so one registry has to decide what is taken. Checking
    /// raw codenames instead let distinct codenames that sanitize to one identifier - <c>my_element</c>
    /// and <c>my__element</c>, or <c>title</c> against <c>title_codename</c> - through into generated
    /// code that does not compile.
    /// </remarks>
    private readonly Dictionary<string, string> _identifierOwners = new(StringComparer.Ordinal)
    {
        [ContentTypeCodenameIdentifier] = codeName,
    };

    private readonly HashSet<string> _renamedCodenameConstants = [];

    public List<Property> Properties { get; } = [];

    /// <summary>
    /// Sibling enum types to emit alongside the content-type record. Populated by the
    /// Management emission path for multiple-choice elements; the Delivery path leaves this empty.
    /// </summary>
    public List<EnumDefinition> Enums { get; } = [];

    public List<string> PropertyCodenameConstants { get; } = [];

    public IReadOnlySet<string> RenamedCodenameConstants => _renamedCodenameConstants;

    public string ClassName => TextHelpers.GetValidPascalCaseIdentifierName(Codename);

    public string Codename { get; } = codeName;

    /// <summary>
    /// Optional content-type identifier (GUID, formatted as a string). Populated by the Management
    /// emission path so the generated <c>[KontentType]</c> attribute carries both codename and id;
    /// left <c>null</c> by the Delivery path.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Adds an element as a property and the codename constant that names it.
    /// </summary>
    /// <remarks>
    /// Both are registered together, or neither is: a property emitted without its constant and a
    /// constant left behind by a rejected property are equally broken, and the caller skipping the
    /// element has no way to undo half of it.
    /// </remarks>
    /// <param name="property">The property to emit.</param>
    /// <exception cref="InvalidOperationException">
    /// Either identifier is already claimed by another element of this content type.
    /// </exception>
    public void AddProperty(Property property)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (property.Identifier == ContentTypeCodenameIdentifier)
        {
            property = new Property(property.Codename, property.TypeName, property.Id, property.Initializer)
            {
                IdentifierOverride = "_" + ContentTypeCodenameIdentifier
            };
        }

        var constantIdentifier = $"{TextHelpers.GetValidPascalCaseIdentifierName(property.Codename)}{CodenameConstantSuffix}";
        var constantIsRenamed = constantIdentifier == ContentTypeCodenameIdentifier;
        if (constantIsRenamed)
        {
            constantIdentifier = "_" + constantIdentifier;
        }

        EnsureAvailable(property.Identifier, property.Codename);
        EnsureAvailable(constantIdentifier, property.Codename);

        _identifierOwners[property.Identifier] = property.Codename;
        _identifierOwners[constantIdentifier] = property.Codename;

        if (constantIsRenamed)
        {
            _renamedCodenameConstants.Add(property.Codename);
        }

        PropertyCodenameConstants.Add(property.Codename);
        Properties.Add(property);
    }

    public void AddEnum(EnumDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (Enums.Exists(e => e.Name == definition.Name))
        {
            throw new InvalidOperationException(
                $"Enum with name '{definition.Name}' is already included.");
        }

        Enums.Add(definition);
    }

    private void EnsureAvailable(string identifier, string codename)
    {
        if (!_identifierOwners.TryGetValue(identifier, out var owner))
        {
            return;
        }

        var reason = owner == codename
            ? $"'{codename}' would emit '{identifier}' twice"
            : $"'{codename}' and '{owner}' both produce the identifier '{identifier}'";

        throw new InvalidOperationException(
            $"Content type '{Codename}': {reason}. Rename one of the elements in Kontent.ai.");
    }
}
