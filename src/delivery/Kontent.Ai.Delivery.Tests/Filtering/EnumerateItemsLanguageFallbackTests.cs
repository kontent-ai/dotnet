using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Delivery.Tests.Filtering;

public class EnumerateItemsLanguageFallbackTests
{
    [Fact]
    public async Task ItemsFeed_WithLanguageFallbackDisabled_AddsSystemLanguageFilter()
    {
        var env = Guid.NewGuid().ToString();
        var baseUrl = $"https://deliver.kontent.ai/{env}";
        var feedUrl = $"{baseUrl}/items-feed";
        var mockHttp = new MockHttpMessageHandler();

        mockHttp.Expect(feedUrl)
            .WithQueryString("language", "es-ES")
            .WithQueryString("system.language[eq]", "es-ES")
            .Respond("application/json",
                await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory,
                    $"Fixtures{Path.DirectorySeparatorChar}DeliveryClient{Path.DirectorySeparatorChar}articles_feed.json")));

        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, d => d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mockHttp));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDeliveryClient>();

        var items = new List<IContentItem>();
        await foreach (var item in client.GetItemsFeed()
            .WithLanguage("es-ES", LanguageFallbackMode.Disabled)
            .EnumerateAsync())
        {
            items.Add(item);
        }

        Assert.NotEmpty(items);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [InlineData(LanguageFallbackMode.Disabled, "language=es-ES&system.language[eq]=es-ES")]
    [InlineData(LanguageFallbackMode.Enabled, "language=es-ES")]
    public async Task ItemsFeed_WithLanguageCalledTwice_LastCallWins(LanguageFallbackMode secondMode, string expectedQuery)
    {
        var env = Guid.NewGuid().ToString();
        var feedUrl = $"https://deliver.kontent.ai/{env}/items-feed";
        var mockHttp = new MockHttpMessageHandler();

        mockHttp.Expect(feedUrl)
            .WithExactQueryString(expectedQuery)
            .Respond("application/json",
                await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory,
                    $"Fixtures{Path.DirectorySeparatorChar}DeliveryClient{Path.DirectorySeparatorChar}articles_feed.json")));

        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, d => d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mockHttp));

        var client = services.BuildServiceProvider().GetRequiredService<IDeliveryClient>();

        var result = await client.GetItemsFeed()
            .WithLanguage("en-US", LanguageFallbackMode.Disabled)
            .WithLanguage("es-ES", secondMode)
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        mockHttp.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task DynamicEnumerateItemsQuery_ChainedParameters_AreForwarded()
    {
        var env = Guid.NewGuid().ToString();
        var baseUrl = $"https://deliver.kontent.ai/{env}";
        var feedUrl = $"{baseUrl}/items-feed";
        var mockHttp = new MockHttpMessageHandler();

        mockHttp.Expect(feedUrl)
            .WithQueryString("language", "es-ES")
            .WithQueryString("elements", "title")
            .WithQueryString("order", "system.name[desc]")
            .WithQueryString("elements.title[eq]", "Coffee")
            .With(req => !req.Headers.Contains("X-KC-Wait-For-Loading-New-Content"))
            .Respond("application/json",
                await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory,
                    $"Fixtures{Path.DirectorySeparatorChar}DeliveryClient{Path.DirectorySeparatorChar}articles_feed.json")));

        var services = new ServiceCollection();
        services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = env }, d => d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mockHttp));

        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDeliveryClient>();

        var result = await client.GetItemsFeed()
            .WithLanguage("es-ES")
            .WithElements("title")
            .OrderBy("system.name", OrderingMode.Descending)
            .WaitForLoadingNewContent(false)
            .Where(f => f.Element("title").IsEqualTo("Coffee"))
            .ExecuteAsync();

        Assert.True(result.IsSuccess);
        mockHttp.VerifyNoOutstandingExpectation();
    }
}
