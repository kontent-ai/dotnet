using Kontent.Ai.Testing;

namespace Kontent.Ai.Management.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task ManagementPublicApi_ShouldNotChangeUnexpectedly()
        => Verifier.Verify(PublicApiApproval.Surface(typeof(Kontent.Ai.Management.IManagementClient).Assembly));
}
