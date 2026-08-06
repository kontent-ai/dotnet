using Kontent.Ai.ModelGenerator.Core.Common;

namespace Kontent.Ai.ModelGenerator.Core.Tests.Common;

public class ClassDefinitionTests
{
    [Fact]
    public void AddProperty_AddCustomProperty_PropertyIsAdded()
    {
        var propertyCodename = "element_1";
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement(propertyCodename, "text"));

        classDefinition.Properties.Should().ContainSingle(property => property.Codename == propertyCodename);
    }

    [Fact]
    public void AddProperty_CustomSystemField_SystemFieldIsReplaced()
    {
        var classDefinition = new ClassDefinition("Class name");

        var userDefinedSystemProperty = Property.FromContentTypeElement("system", "text");
        classDefinition.AddProperty(userDefinedSystemProperty);

        classDefinition.Properties[0].Should().Be(userDefinedSystemProperty);
    }

    [Fact]
    public void AddProperty_RegistersTheCodenameConstant()
    {
        var elementCodename = "element_codename";

        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement(elementCodename, "text"));

        classDefinition.PropertyCodenameConstants.Should().ContainSingle(property => property == elementCodename);
    }

    [Fact]
    public void AddProperty_DuplicateElementCodenames_Throws()
    {
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement("element", "text"));

        var call = () => classDefinition.AddProperty(Property.FromContentTypeElement("element", "text"));

        call.Should().ThrowExactly<InvalidOperationException>();
        classDefinition.Properties.Should().ContainSingle();
    }

    [Theory]
    // Distinct codenames, one C# identifier - the separators are stripped before PascalCasing.
    [InlineData("my_element", "my__element")]
    [InlineData("my_element", "my-element")]
    // The property emitted for one element takes the identifier the other's constant needs.
    [InlineData("title", "title_codename")]
    [InlineData("title_codename", "title")]
    // Both resolve onto the reserved ContentTypeCodename constant and are renamed to the same thing.
    [InlineData("content_type", "content_type_codename")]
    public void AddProperty_CodenamesCollidingOnOneIdentifier_ThrowsAndAddsNothing(string first, string second)
    {
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement(first, "text"));

        var call = () => classDefinition.AddProperty(Property.FromContentTypeElement(second, "text"));

        call.Should().ThrowExactly<InvalidOperationException>();
        // Neither half may survive: a stray constant is as uncompilable as a stray property.
        classDefinition.Properties.Should().ContainSingle();
        classDefinition.PropertyCodenameConstants.Should().ContainSingle();
    }

    [Fact]
    public void AddProperty_CollidingCodenames_NamesBothInTheError()
    {
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement("my_element", "text"));

        var call = () => classDefinition.AddProperty(Property.FromContentTypeElement("my__element", "text"));

        call.Should().ThrowExactly<InvalidOperationException>()
            .WithMessage("*my__element*my_element*MyElement*");
    }

    [Fact]
    public void AddProperty_ContentTypeCodenameCollision_RenamesProperty()
    {
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement("content_type_codename", "text"));

        classDefinition.Properties.Should().ContainSingle(p => p.Identifier == "_ContentTypeCodename");
    }

    [Fact]
    public void AddProperty_NoCollision_IdentifierUnchanged()
    {
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement("text", "text"));

        classDefinition.Properties.Should().ContainSingle(p => p.Identifier == "Text");
    }

    [Fact]
    public void AddProperty_ContentTypeCodenameConstantCollision_TracksRenamedConstant()
    {
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement("content_type", "text"));

        classDefinition.RenamedCodenameConstants.Should().Contain("content_type");
    }

    [Fact]
    public void AddProperty_NoCollision_NotTrackedAsRenamed()
    {
        var classDefinition = new ClassDefinition("Class name");
        classDefinition.AddProperty(Property.FromContentTypeElement("text", "text"));

        classDefinition.RenamedCodenameConstants.Should().BeEmpty();
    }
}
