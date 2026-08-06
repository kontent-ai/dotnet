using Kontent.Ai.Testing;

namespace Kontent.Ai.Sync.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task SyncPublicApi_ShouldNotChangeUnexpectedly()
        => Verify(PublicApiApproval.Surface(typeof(Kontent.Ai.Sync.ServiceCollectionExtensions).Assembly));
}
