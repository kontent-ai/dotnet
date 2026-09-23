using Kontent.Ai.Delivery.Abstractions;

namespace Kontent.Ai.Delivery.Tests.CodeSamples;

/// <summary>
/// The client construction every published Delivery sample opens with. The sync script replaces the
/// client declaration in each published file with this block, so it only has to compile - the
/// placeholder would fail options validation if it ran.
/// </summary>
public class ClientRegistration
{
    public void CreateClient()
    {
        // DocClient
        // Or register it through DI with services.AddDeliveryClient()
        using var client = DeliveryClient.Create(new DeliveryOptions { EnvironmentId = "your-environment-id" }.UseProductionApi());
        // EndDocClient
    }
}
