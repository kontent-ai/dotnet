using Kontent.Ai.Common;
using Kontent.Ai.Management.Api;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Conversion;
using Kontent.Ai.Management.Extensions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;
using System.Net;

namespace Kontent.Ai.Management.Tests.Base;

/// <summary>
/// Builds an <see cref="IManagementClient"/> whose Refit transport is backed by a
/// <see cref="MockHttpMessageHandler"/>, installed as the primary handler through the same registration
/// the SDK uses, so tests arrange responses and assert outgoing requests via MockHttp's fluent API.
/// Resilience is switched off explicitly: a fixture answering 5xx must fail the call, not be retried. Supply a pre-registered
/// <c>converter</c> for strongly-typed variant paths: the client's own converter would otherwise
/// auto-scan the whole test assembly and trip the intentional codename collision among the test fixtures.
/// </summary>
internal static class MockClientFactory
{
    public const string EnvironmentId = "a9931a80-9af4-010b-0590-ecb1273cf1b8";
    public const string SubscriptionId = "9c7b9841-ea99-48a7-a46d-65b2549d6c05";

    // Refit composes "{BaseAddress}{relative path}", so the base must carry no trailing slash or every path
    // doubles the separator. Endpoint defaults to "https://manage.kontent.ai"; the SDK appends "/v2/projects/{id}".
    public static string BaseUrl => $"https://manage.kontent.ai/v2/projects/{EnvironmentId}";

    // Subscription-scoped endpoints resolve against the subscription scope instead of the project.
    public static string SubscriptionBaseUrl => $"https://manage.kontent.ai/v2/subscriptions/{SubscriptionId}";

    public static (IManagementClient Client, MockHttpMessageHandler Mock) Create(ContentItemEnvelopeConverter? converter = null)
    {
        var mock = new MockHttpMessageHandler();
        var (managementApi, subscriptionApi, owned) = CreateApis(mock);
        var client = new ManagementClient(managementApi, subscriptionApi, owned, converter);
        return (client, mock);
    }

    /// <summary>
    /// Runs the SDK's registration in a private container with <paramref name="primaryHandler"/> as the
    /// innermost handler, and draws the configured scopes' Refit clients from it.
    /// </summary>
    public static (IManagementApi? Api, ISubscriptionApi? SubscriptionApi, IDisposable OwnedResources) CreateApis(
        HttpMessageHandler primaryHandler,
        ManagementOptions? options = null)
    {
        options ??= new ManagementOptions
        {
            ApiKey = "Dummy_API_key",
            EnvironmentId = EnvironmentId,
            SubscriptionId = SubscriptionId,
        };
        var services = new ServiceCollection();
        services.AddManagementClient(options, management =>
        {
            management.HttpClient.ConfigurePrimaryHttpMessageHandler(() => primaryHandler);
            management.SubscriptionHttpClient.ConfigurePrimaryHttpMessageHandler(() => primaryHandler);
        });

        // Resilience stays off for every mock client, as it was in the hand-built factory this replaced.
        // It is set on the container's copy of the options, so the caller's instance is left as it was.
        services.PostConfigure<ManagementOptions>(NamedClients.Default, o => o.EnableResilience = false);

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        return ServiceCollectionExtensions.CreateOwnedApis(provider, NamedClients.Default);
    }

    // Doc-sample helpers. Samples don't assert the outgoing request — they just need the call to return a canned
    // (or empty) response so the snippet runs. The catch-all responder is FIFO-clamped over the given fixtures:
    // each call returns the next fixture, the last one repeats, and with none the response is an empty 200 OK.
    public static IManagementClient CreateForSample(string folder, params string[] fixtureFiles)
    {
        var bodies = fixtureFiles.Select(f => File.ReadAllText(SamplePath(folder, f))).ToArray();
        var mock = new MockHttpMessageHandler();
        var index = 0;
        mock.Fallback.Respond(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            if (bodies.Length > 0)
            {
                response.Content = new StringContent(bodies[Math.Min(index++, bodies.Length - 1)]);
            }
            return response;
        });

        var (managementApi, subscriptionApi, owned) = CreateApis(mock);
        return new ManagementClient(managementApi, subscriptionApi, owned);
    }

    private static string SamplePath(string folder, string file)
        => Path.Combine(Environment.CurrentDirectory, "Data", folder, file);
}
