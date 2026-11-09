using Kontent.Ai.AspNetCore.ImageTransformation;
using Kontent.Ai.Testing;

namespace Kontent.Ai.AspNetCore.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task PublicApi_ShouldNotChangeUnexpectedly()
        => Verifier.Verify(PublicApiApproval.Surface(typeof(AssetTagHelper).Assembly));
}
