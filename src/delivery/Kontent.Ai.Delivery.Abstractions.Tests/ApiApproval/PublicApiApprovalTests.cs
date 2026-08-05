using Kontent.Ai.Testing;

namespace Kontent.Ai.Delivery.Abstractions.Tests.ApiApproval;

public class PublicApiApprovalTests
{
    [Fact]
    public Task PublicApi_ShouldNotChangeUnexpectedly()
        => Verify(PublicApiApproval.Surface(typeof(IDeliveryClient).Assembly));
}
