using System.Text.Json;
using System.Text.Json.Serialization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Configuration;
using Kontent.Ai.Delivery.ContentItems;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Delivery.Tests.Extensions;

/// <summary>
/// Registering <see cref="JsonSerializerOptions"/> is an ordinary thing for an application to do, and it
/// must not reach the SDK's deserializer. One that arrives without <c>ContentItemConverterFactory</c>
/// captures no raw item JSON, so hydration finds nothing and typed models come back empty - a failure
/// with no exception and no log line to follow.
/// </summary>
public class SharedJsonOptionsTests
{
    private const string EnvironmentId = "d79786fb-042c-47ec-8e5c-beaf93e38b84";

    [Fact]
    public void ApplicationRegisteredInstance_IsNotAdoptedAsTheSdkSerializer()
    {
        var applicationOptions = new JsonSerializerOptions { WriteIndented = true };

        var provider = BuildProvider(services => services.AddSingleton(applicationOptions));

        Assert.NotSame(applicationOptions, ResolveSdkOptions(provider));
    }

    [Fact]
    public void ApplicationRegisteredFactory_IsNotAdoptedAsTheSdkSerializer()
    {
        // A factory registration hides the instance from descriptor inspection, which used to leave Refit
        // and the mappers on two different serializers rather than one wrong one.
        var applicationOptions = new JsonSerializerOptions { WriteIndented = true };

        var provider = BuildProvider(services => services.AddSingleton(_ => applicationOptions));

        Assert.NotSame(applicationOptions, ResolveSdkOptions(provider));
    }

    [Fact]
    public void ApplicationKeyedRegistration_IsLeftAlone()
    {
        var applicationOptions = new JsonSerializerOptions { WriteIndented = true };

        var provider = BuildProvider(services => services.AddKeyedSingleton("app", applicationOptions));

        Assert.NotSame(applicationOptions, ResolveSdkOptions(provider));
        Assert.Same(applicationOptions, provider.GetRequiredKeyedService<JsonSerializerOptions>("app"));
    }

    [Fact]
    public void ApplicationRegistration_IsStillTheOneTheApplicationResolves()
    {
        var applicationOptions = new JsonSerializerOptions { WriteIndented = true };

        var provider = BuildProvider(services => services.AddSingleton(applicationOptions));

        Assert.Same(applicationOptions, provider.GetRequiredService<JsonSerializerOptions>());
    }

    [Fact]
    public void SdkSerializer_CarriesTheConvertersHydrationDependsOn()
    {
        var options = ResolveSdkOptions(BuildProvider());

        Assert.Contains(options.Converters, c => c is JsonConverterFactory);
    }

    [Fact]
    public void Deserializer_StillCapturesRawItemJson_WhenTheApplicationRegistersItsOwnOptions()
    {
        // The consequence the wiring exists for: no captured raw JSON means hydration has nothing to map
        // from, and a typed model comes back empty without anything being thrown or logged.
        var provider = BuildProvider(services => services.AddSingleton(new JsonSerializerOptions()));

        var item = provider.GetRequiredService<ContentDeserializer>()
            .Deserialize<IDynamicElements>(JsonSerializer.Deserialize<JsonElement>(ItemJson));

        Assert.True(((IRawContentItem)item).RawItemJson.HasValue);
    }

    [Fact]
    public void SecondClient_SharesTheFirstClientsOptionsInstance()
    {
        var services = new ServiceCollection();
        services.AddDeliveryClient("first", d => d.Options.Configure(o => o.EnvironmentId = EnvironmentId));
        services.AddDeliveryClient("second", d => d.Options.Configure(o => o.EnvironmentId = EnvironmentId));

        // One instance keeps System.Text.Json's per-options metadata cache shared across clients.
        Assert.NotNull(ResolveSdkOptions(services.BuildServiceProvider()));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? registerApplicationServices = null)
    {
        var services = new ServiceCollection();
        registerApplicationServices?.Invoke(services);
        services.AddDeliveryClient(d => d.Options.Configure(o => o.EnvironmentId = EnvironmentId));

        return services.BuildServiceProvider();
    }

    private static JsonSerializerOptions ResolveSdkOptions(IServiceProvider provider)
        => provider.GetRequiredService<DeliveryJsonOptions>().Value;

    private const string ItemJson = """
    {
        "system": {
            "id": "00000000-0000-0000-0000-000000000001",
            "name": "Test",
            "codename": "test",
            "type": "article",
            "collection": "default",
            "workflow": "default",
            "workflow_step": "published",
            "language": "en-US",
            "last_modified": "2024-01-01T00:00:00Z",
            "sitemap_locations": []
        },
        "elements": {}
    }
    """;
}
