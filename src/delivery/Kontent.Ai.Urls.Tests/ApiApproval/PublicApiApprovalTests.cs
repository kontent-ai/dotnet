using Kontent.Ai.Testing;
using Kontent.Ai.Urls.ImageTransformation;

namespace Kontent.Ai.Urls.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task PublicApi_ShouldNotChangeUnexpectedly()
        => Verify(PublicApiApproval.Surface(typeof(ImageUrlBuilder).Assembly));
}
