using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.Testing;

namespace Kontent.Ai.ModelGenerator.Core.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task ModelGeneratorCorePublicApi_ShouldNotChangeUnexpectedly()
        => Verify(PublicApiApproval.Surface(typeof(ClassDefinition).Assembly));
}
