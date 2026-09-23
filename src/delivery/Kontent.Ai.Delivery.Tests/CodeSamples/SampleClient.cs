using System.Net;
using System.Text;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Generated;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Delivery.Tests.CodeSamples;

/// <summary>
/// A client for doc samples. Samples do not assert the outgoing request, they only need each call answered
/// with a recorded response: every request gets the next fixture, and the last one repeats.
/// </summary>
internal static class SampleClient
{
    public static IDeliveryClient Create(params string[] fixtures)
    {
        var bodies = fixtures
            .Select(fixture => File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Fixtures", fixture)))
            .ToArray();
        var index = 0;

        var mock = new MockHttpMessageHandler();
        mock.Fallback.Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(bodies[Math.Min(index++, bodies.Length - 1)], Encoding.UTF8, "application/json"),
        });

        var services = new ServiceCollection();
        services.AddSingleton<ITypeProvider, GeneratedTypeProvider>();
        services.AddDeliveryClient(
            new DeliveryOptions { EnvironmentId = Guid.NewGuid().ToString() },
            delivery => delivery.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock));
        return services.BuildServiceProvider().GetRequiredService<IDeliveryClient>();
    }
}
