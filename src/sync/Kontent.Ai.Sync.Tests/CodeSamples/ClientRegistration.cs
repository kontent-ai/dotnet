namespace Kontent.Ai.Sync.Tests.CodeSamples;

/// <summary>
/// The client construction every published Sync sample opens with. The sync script replaces the
/// client declaration in each published file with this block, so it only has to compile - the
/// placeholder would fail options validation if it ran.
/// </summary>
public class ClientRegistration
{
    public void CreateClient()
    {
        // DocClient
        // Or register it through DI with services.AddSyncClient()
        using var client = SyncClient.Create(new SyncOptions { EnvironmentId = "your-environment-id" }
            .UsePreviewApi("your-preview-api-key"));
        // EndDocClient
    }
}
