using Kontent.Ai.Testing;

namespace Kontent.Ai.Delivery.SourceGeneration.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task SourceGenerationPublicApi_ShouldNotChangeUnexpectedly()
        => Verify(PublicApiApproval.Surface(typeof(ContentTypeGenerator).Assembly));
}
