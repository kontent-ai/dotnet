using System.Security.Cryptography;
using System.Text;
using Kontent.Ai.AspNetCore.Webhooks;
using Kontent.Ai.AspNetCore.Webhooks.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.AspNetCore.Tests.CodeSamples;

/// <summary>
/// Source of the samples in https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/using-webhooks.
/// <see cref="ParseNotifications"/> only compiles here: it is an application's startup code, and the middleware it
/// registers is exercised through a real pipeline in <see cref="WebhookSignatureValidatorPipelineTests"/>.
/// </summary>
public sealed class Webhooks
{
    [Theory]
    [InlineData("{\"notifications\":[]}", "webhook-secret", true)]
    [InlineData("{\"notifications\":[{}]}", "webhook-secret", false)]
    [InlineData("{\"notifications\":[]}", "another-secret", false)]
    public void ValidateSignature(string payload, string secret, bool expected)
    {
        var signatureHeader = Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes("webhook-secret"), Encoding.UTF8.GetBytes("{\"notifications\":[]}")));

        // DocSection: webhooks_validate_signature
        // Validates the 'X-Kontent-ai-Signature' header against the raw webhook payload.
        static bool IsWebhookSignatureValid(string payload, string sharedSecret, string signatureHeader)
        {
            if (string.IsNullOrWhiteSpace(signatureHeader))
            {
                return false;
            }

            // Header values can be quoted depending on hosting pipeline/proxy behavior.
            var normalizedSignature = signatureHeader.Trim().Trim('"');

            var payloadBytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);
            var keyBytes = Encoding.UTF8.GetBytes(sharedSecret ?? string.Empty);

            using var hmac = new HMACSHA256(keyBytes);
            var computedBytes = hmac.ComputeHash(payloadBytes);
            var computedSignature = Convert.ToBase64String(computedBytes);

            // Use constant-time comparison to avoid timing attacks.
            var providedBytes = Encoding.UTF8.GetBytes(normalizedSignature);
            var expectedBytes = Encoding.UTF8.GetBytes(computedSignature);
            return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }
        // EndDocSection

        Assert.Equal(expected, IsWebhookSignatureValid(payload, secret, signatureHeader));
        Assert.False(IsWebhookSignatureValid(payload, secret, ""));
    }

    internal static void ParseNotifications(string[] args)
    {
        // DocSection: webhooks_parse_notifications
        // Requires the Kontent.Ai.AspNetCore package; the webhook's secret goes to "WebhookOptions:Secret" in appsettings.json
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(nameof(WebhookOptions)));

        var app = builder.Build();

        // Rejects requests to /webhooks with a missing or invalid signature with 401 Unauthorized
        app.UseWebhookSignatureValidator(context => context.Request.Path.StartsWithSegments("/webhooks"));

        // Notifications that pass validation bind to typed models
        app.MapPost("/webhooks/kontent", (WebhookNotification notification) =>
        {
            foreach (var webhook in notification.Notifications)
            {
                if (webhook.Message.ObjectType == WebhookObjectTypes.ContentItem)
                {
                    Console.WriteLine($"Content item: {webhook.Data.System.Name}");
                }
            }

            return Results.Ok();
        });

        app.Run();
        // EndDocSection
    }
}
