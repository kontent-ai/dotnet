using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kontent.Ai.AspNetCore.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kontent.Ai.AspNetCore.Tests;

/// <summary>
/// The overload that takes its <see cref="WebhookOptions"/> from the container resolves them per request,
/// so a host that never configured them looks fine until the first webhook arrives. Exercised through a
/// real pipeline rather than by calling the middleware directly, because what is under test is the
/// registration - that the branch is taken and the options are found.
/// </summary>
public class WebhookSignatureValidatorPipelineTests
{
    private const string Secret = "test-secret";
    private const string Body = "payload";

    [Fact]
    public async Task ContainerResolvedOptions_ValidSignature_ReachesTheEndpoint()
    {
        using var host = await StartHostAsync(configureOptions: true);

        var response = await SendAsync(host, Signature(Body, Secret));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("handled", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ContainerResolvedOptions_InvalidSignature_IsRejected()
    {
        using var host = await StartHostAsync(configureOptions: true);

        var response = await SendAsync(host, "not-the-signature");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ContainerResolvedOptions_NeverConfigured_FailsAtTheFirstRequest()
    {
        using var host = await StartHostAsync(configureOptions: false);

        var act = async () => await SendAsync(host, Signature(Body, Secret));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(act);
        Assert.Contains(nameof(WebhookOptions.Secret), exception.Message);
    }

    [Fact]
    public async Task ContainerResolvedOptions_PathOutsideThePredicate_IsNotValidated()
    {
        using var host = await StartHostAsync(configureOptions: true);

        var response = await host.GetTestClient().PostAsync("/elsewhere", new StringContent(Body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<IHost> StartHostAsync(bool configureOptions)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    if (configureOptions)
                    {
                        services.Configure<WebhookOptions>(options => options.Secret = Secret);
                    }
                })
                .Configure(app =>
                {
                    app.UseWebhookSignatureValidator(context => context.Request.Path.StartsWithSegments("/webhook"));
                    app.Run(context => context.Response.WriteAsync("handled"));
                }))
            .StartAsync();

        return host;
    }

    private static Task<HttpResponseMessage> SendAsync(IHost host, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhook")
        {
            Content = new StringContent(Body),
        };
        request.Headers.Add("X-Kontent-ai-Signature", signature);

        return host.GetTestClient().SendAsync(request);
    }

    private static string Signature(string body, string secret) =>
        Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));
}
