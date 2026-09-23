using Kontent.Ai.AspNetCore.Webhooks;
using Microsoft.AspNetCore.Builder;

namespace Kontent.Ai.AspNetCore.Tests.CodeSamples;

/// <summary>
/// Source of the samples in https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/using-webhooks.
/// The sample only compiles here: it is an application's startup code, and the middleware it registers is
/// exercised through a real pipeline in <see cref="WebhookSignatureValidatorPipelineTests"/>.
/// </summary>
public static class Webhooks
{
    public static void ValidateSignature(string[] args)
    {
        // DocSection: webhooks_validate_signature
        // Validates the 'X-Kontent-ai-Signature' header against the raw webhook payload.
        // Rejects requests to /webhooks that Kontent.ai did not sign with the webhook's secret.
        // Requires the Kontent.Ai.AspNetCore package; the secret goes to "WebhookOptions:Secret" in appsettings.json.
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();

        app.UseWebhookSignatureValidator(
            context => context.Request.Path.StartsWithSegments("/webhooks", StringComparison.OrdinalIgnoreCase),
            builder.Configuration.GetSection(nameof(WebhookOptions)));
        // EndDocSection
    }
}
