using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Generators.Class;

namespace Kontent.Ai.ModelGenerator.Core.Tests.Generators.Class;

/// <summary>
/// The base record and its extender are emitted the same way in both modes, so anything they name has to
/// resolve in both. A project generated with <c>--management</c> has no reason to reference the Delivery
/// SDK, and a using for it there is a CS0246 in code the consumer did not write.
/// </summary>
public class BaseClassCodeGeneratorTests
{
    private static BaseClassCodeGenerator Generator() =>
        new(new CodeGeneratorOptions { Namespace = "Test.Models", BaseRecord = "ArticleBase" });

    [Fact]
    public void GenerateBaseClassCode_ReferencesNoSdk()
    {
        var code = Generator().GenerateBaseClassCode();

        Assert.DoesNotContain("using ", code);
        Assert.Contains("public partial record ArticleBase", code);
    }

    [Fact]
    public void GenerateExtenderCode_ReferencesNoSdk()
    {
        var generator = Generator();
        generator.AddClassNameToExtend("Article");

        var code = generator.GenerateExtenderCode();

        Assert.DoesNotContain("using ", code);
        Assert.Contains("public partial record Article : ArticleBase", code);
    }
}
