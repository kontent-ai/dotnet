using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kontent.Ai.AspNetCore.Webhooks;
using Kontent.Ai.AspNetCore.Webhooks.Models;
using Kontent.Ai.Delivery;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.AspNetCore.Tests;

/// <summary>
/// The webhook samples in the README and the upgrade guide, copied by hand and run through a real pipeline. A change to
/// a sample has to land here too.
/// </summary>
public class DocumentedWebhookSampleTests
{
    private const string EnvironmentId = "195a50c1-f1b8-0066-9eb4-83f7246d5459";
    private const string Secret = "test-secret";
    private const string Endpoint = "/webhooks/kontent";

    private const string ItemJson = """
        {
          "item": {
            "system": { "id": "22222222-2222-2222-2222-222222222222", "name": "About", "codename": "about", "type": "article", "language": "default", "collection": "default", "last_modified": "2026-09-08T00:00:00Z" },
            "elements": {}
          },
          "modular_content": {}
        }
        """;

    [Fact]
    public async Task ReadmeEndpoint_RejectsAnUnsignedRequest()
    {
        await using var app = await StartReadmeEndpointAsync();

        var response = await app.GetTestClient().PostAsync(Endpoint, Json("{}"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReadmeEndpoint_EvictsTheChangedItem()
    {
        await using var app = await StartReadmeEndpointAsync();
        await PrimeTheCacheAsync(app);

        var response = await PostSignedAsync(app, Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, EnvironmentId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await ReadIsCacheHitAsync(app));
    }

    [Fact]
    public async Task ReadmeEndpoint_IgnoresAnotherEnvironment()
    {
        await using var app = await StartReadmeEndpointAsync();
        await PrimeTheCacheAsync(app);

        var response = await PostSignedAsync(app, Notification(WebhookObjectTypes.ContentItem, WebhookActions.Published, Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(await ReadIsCacheHitAsync(app));
    }

    [Fact]
    public async Task ReadmeEndpoint_PurgesOnALanguageChange()
    {
        await using var app = await StartReadmeEndpointAsync();
        await PrimeTheCacheAsync(app);

        var response = await PostSignedAsync(app, Notification(WebhookObjectTypes.Language, WebhookActions.Changed, EnvironmentId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await ReadIsCacheHitAsync(app));
    }

    [Fact]
    public async Task UpgradeGuideSample_WithTheSecretSet_StartsAndValidates()
    {
        await using var app = await StartUpgradeGuideSampleAsync(Secret);

        var unsigned = await app.GetTestClient().PostAsync(Endpoint, Json("{}"));
        var signed = await PostSignedAsync(app, "{}");

        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signed.StatusCode);
    }

    [Fact]
    public async Task UpgradeGuideSample_WithoutTheSecret_StartsAndClosesTheEndpoint()
    {
        await using var app = await StartUpgradeGuideSampleAsync(secret: null);

        var response = await app.GetTestClient().PostAsync(Endpoint, Json("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<WebApplication> StartReadmeEndpointAsync()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"https://deliver.kontent.ai/{EnvironmentId}/items/about").Respond("application/json", ItemJson);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DeliveryOptions:EnvironmentId"] = EnvironmentId,
            ["DeliveryOptions:EnableResilience"] = "false",
            ["WebhookOptions:Secret"] = Secret,
        });

        builder.Services.AddDeliveryClient(delivery =>
        {
            delivery.Options.BindConfiguration("DeliveryOptions");
            delivery.UseMemoryCache(cache => cache.DefaultExpiration = TimeSpan.FromHours(1));
            delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock);
        });
        builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(nameof(WebhookOptions)));

        var app = builder.Build();

        app.UseWebhookSignatureValidator(context => context.Request.Path.StartsWithSegments("/webhooks"));

        app.MapPost("/webhooks/kontent", async (
            WebhookNotification notification,
            IDeliveryCacheManager cache,
            IDeliveryClient client,
            IOptions<DeliveryOptions> delivery,
            CancellationToken cancellationToken) =>
        {
            var environmentId = Guid.Parse(delivery.Value.EnvironmentId);

            var relevant = notification.Notifications
                .Where(n => n.Message.EnvironmentId == environmentId)
                .Where(n => n.Message.ObjectType != WebhookObjectTypes.ContentItem || n.Message.DeliverySlot == WebhookDeliverySlots.Published)
                .ToList();

            var invalidated = await cache.InvalidateAsync(relevant, client, cancellationToken);
            return invalidated ? Results.NoContent() : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });

        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> StartUpgradeGuideSampleAsync(string? secret)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["WebhookOptions:Secret"] = secret });
        var app = builder.Build();

        static bool IsWebhookRequest(HttpContext context) =>
            context.Request.Path.StartsWithSegments("/webhooks", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(app.Configuration["WebhookOptions:Secret"]))
        {
            app.UseWebhookSignatureValidator(IsWebhookRequest, app.Configuration.GetSection(nameof(WebhookOptions)));
        }
        else
        {
            app.Logger.LogWarning("WebhookOptions:Secret is not set; the webhook endpoints are disabled.");
            app.UseWhen(IsWebhookRequest, branch => branch.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }));
        }

        app.MapPost(Endpoint, () => Results.NoContent());

        await app.StartAsync();
        return app;
    }

    private static async Task PrimeTheCacheAsync(WebApplication app)
    {
        Assert.False(await ReadIsCacheHitAsync(app));
        Assert.True(await ReadIsCacheHitAsync(app));
    }

    private static async Task<bool> ReadIsCacheHitAsync(WebApplication app)
    {
        var result = await app.Services.GetRequiredService<IDeliveryClient>().GetItem<IDynamicElements>("about").ExecuteAsync();
        Assert.True(result.IsSuccess);
        return result.IsCacheHit;
    }

    private static string Notification(string objectType, string action, string environmentId) => JsonSerializer.Serialize(new WebhookNotification
    {
        Notifications =
        [
            new WebhookModel
            {
                Data = new WebhookData { System = new WebhookItem { Id = Guid.NewGuid(), Name = "About", Codename = "about", LastModified = DateTime.UtcNow } },
                Message = new WebhookMessage { EnvironmentId = Guid.Parse(environmentId), ObjectType = objectType, Action = action, DeliverySlot = WebhookDeliverySlots.Published },
            },
        ],
    });

    private static Task<HttpResponseMessage> PostSignedAsync(WebApplication app, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = Json(body) };
        request.Headers.Add("X-Kontent-ai-Signature",
            Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(body))));
        return app.GetTestClient().SendAsync(request);
    }

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");
}
