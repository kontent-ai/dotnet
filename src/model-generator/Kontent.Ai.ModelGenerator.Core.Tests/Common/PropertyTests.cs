using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Configuration;

namespace Kontent.Ai.ModelGenerator.Core.Tests.Common;

public class PropertyTests
{
    [Fact]
    public void Constructor_MissingIdParam_ObjectIsInitializedWithCorrectValues()
    {
        var element = new Property("element_codename", "string");

        element.Identifier.Should().Be("ElementCodename");
        element.TypeName.Should().Be("string");
        element.Id.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("id")]
    public void Constructor_IdParamPresent_ObjectIsInitializedWithCorrectValues(string? id)
    {
        var element = new Property("element_codename", "string", id);

        element.Identifier.Should().Be("ElementCodename");
        element.TypeName.Should().Be("string");
        element.Id.Should().Be(id);
    }

    [Theory]
    [InlineData("text", "string?")]
    [InlineData("rich_text", "RichTextContent?")]
    [InlineData("number", "double?")]
    [InlineData("multiple_choice", "IEnumerable<MultipleChoiceOption>?")]
    [InlineData("date_time", "DateTimeContent?")]
    [InlineData("asset", "IEnumerable<Asset>?")]
    [InlineData("modular_content", "IEnumerable<IEmbeddedContent>?")]
    [InlineData("taxonomy", "IEnumerable<TaxonomyTerm>?")]
    [InlineData("url_slug", "string?")]
    [InlineData("custom", "string?")]
    public void FromContentTypeElement_DeliveryApiModel_Returns(string contentType, string expectedTypeName)
    {
        var codename = "element_codename";
        var expectedCodename = "ElementCodename";

        var element = Property.FromContentTypeElement(codename, contentType);

        element.Identifier.Should().Be(expectedCodename);
        element.TypeName.Should().Be(expectedTypeName);
    }

    [Fact]
    public void FromContentTypeElement_DeliveryApiModel_InvalidContentTypeElement_Throws()
    {
        var fromContentTypeElementCall = () => Property.FromContentTypeElement("codename", "unknown content type");

        fromContentTypeElementCall.Should().ThrowExactly<ArgumentException>();
    }

    [Theory]
    [InlineData("text", "string", "string.Empty")]
    [InlineData("rich_text", "RichTextContent", "RichTextContent.Empty")]
    [InlineData("number", "double?", null)]
    [InlineData("multiple_choice", "IEnumerable<MultipleChoiceOption>", "[]")]
    [InlineData("date_time", "DateTimeContent?", null)]
    [InlineData("asset", "IEnumerable<Asset>", "[]")]
    [InlineData("modular_content", "IEnumerable<IEmbeddedContent>", "[]")]
    [InlineData("taxonomy", "IEnumerable<TaxonomyTerm>", "[]")]
    [InlineData("url_slug", "string", "string.Empty")]
    [InlineData("custom", "string?", null)]
    public void FromContentTypeElement_Semantic_ReturnsTypeAndInitializer(string contentType, string expectedTypeName, string? expectedInitializer)
    {
        var element = Property.FromContentTypeElement("element_codename", contentType, NullabilityMode.Semantic);

        element.TypeName.Should().Be(expectedTypeName);
        element.Initializer.Should().Be(expectedInitializer);
    }

    [Theory]
    [InlineData("text", "string?")]
    [InlineData("rich_text", "RichTextContent?")]
    [InlineData("multiple_choice", "IEnumerable<MultipleChoiceOption>?")]
    public void FromContentTypeElement_Strict_DoesNotProduceInitializer(string contentType, string expectedTypeName)
    {
        var element = Property.FromContentTypeElement("element_codename", contentType, NullabilityMode.Strict);

        element.TypeName.Should().Be(expectedTypeName);
        element.Initializer.Should().BeNull();
    }

    [Fact]
    public void Identifier_WithIdentifierOverride_ReturnsOverride()
    {
        var element = new Property("element_codename", "string") { IdentifierOverride = "_CustomName" };

        element.Identifier.Should().Be("_CustomName");
    }

    [Fact]
    public void Identifier_WithoutIdentifierOverride_ReturnsPascalCase()
    {
        var element = new Property("element_codename", "string");

        element.Identifier.Should().Be("ElementCodename");
    }
}
