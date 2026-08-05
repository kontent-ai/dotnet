using Kontent.Ai.AspNetCore.Webhooks;
using Kontent.Ai.Testing;

namespace Kontent.Ai.AspNetCore.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task PublicApi_ShouldNotChangeUnexpectedly()
        => Verifier.Verify(PublicApiApproval.Surface(typeof(SignatureMiddleware).Assembly));
}
