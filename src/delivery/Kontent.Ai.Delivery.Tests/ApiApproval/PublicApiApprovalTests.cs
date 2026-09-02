using Kontent.Ai.Testing;

namespace Kontent.Ai.Delivery.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task PublicApi_ShouldNotChangeUnexpectedly()
        => Verify(PublicApiApproval.Surface(typeof(Kontent.Ai.Delivery.DeliveryClient).Assembly));

    [Fact]
    public Task CachingPublicApi_ShouldNotChangeUnexpectedly()
        => Verify(PublicApiApproval.Surface(typeof(Kontent.Ai.Delivery.Caching.MemoryCacheManager).Assembly));
}
