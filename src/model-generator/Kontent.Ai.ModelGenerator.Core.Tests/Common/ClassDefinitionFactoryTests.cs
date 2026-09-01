using Kontent.Ai.ModelGenerator.Core.Common;

namespace Kontent.Ai.ModelGenerator.Core.Tests.Common;

public class ClassDefinitionFactoryTests
{
    [Fact]
    public void Constructor_SetsClassNameIdentifier()
    {
        var definition = ClassDefinitionFactory.CreateClassDefinition("Article type");

        definition.ClassName.Should().Be("ArticleType");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_CodenameIsNullEmptyOrWhiteSpace_Throws(string? codename)
    {
        var call = () => ClassDefinitionFactory.CreateClassDefinition(codename!);

        call.Should().Throw<ArgumentException>();
    }
}
