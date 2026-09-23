using Kontent.Ai.Management.Configuration;

namespace Kontent.Ai.Management.Tests.CodeSamples;

/// <summary>
/// The client construction every published Management sample opens with. The sync script replaces the
/// client declaration in each published file with this block, so it only has to compile - the
/// placeholders would fail options validation if it ran.
/// </summary>
public class ClientRegistration
{
    public void CreateClient()
    {
        // DocClient
        // Or register it through DI with services.AddManagementClient()
        using var client = new ManagementClient(new ManagementOptions
        {
            ApiKey = "KONTENT_AI_MANAGEMENT_API_KEY",
            EnvironmentId = "KONTENT_AI_ENVIRONMENT_ID"
        });
        // EndDocClient
    }

    // Subscription endpoints are scoped to a subscription rather than an environment, and take a
    // subscription API key.
    public void CreateSubscriptionClient()
    {
        // DocClient: subscription
        // Or register it through DI with services.AddManagementClient()
        using var client = new ManagementClient(new ManagementOptions
        {
            ApiKey = "KONTENT_AI_SUBSCRIPTION_API_KEY",
            SubscriptionId = "KONTENT_AI_SUBSCRIPTION_ID"
        });
        // EndDocClient
    }
}
