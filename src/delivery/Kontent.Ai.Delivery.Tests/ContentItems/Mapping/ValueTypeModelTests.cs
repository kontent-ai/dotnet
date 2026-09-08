using System.Text.Json.Serialization;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.ContentItems.Mapping;
using Microsoft.Extensions.DependencyInjection;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Delivery.Tests.ContentItems.Mapping;

public sealed class ValueTypeModelTests
{
    private const string EnvironmentId = "975bf280-fd91-488c-994c-2f04416e5ee3";

    public struct ValueModel
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    [Fact]
    public void CreateMappings_RefusesAValueType()
    {
        var exception = Assert.Throws<NotSupportedException>(() => PropertyMappingInfo.CreateMappings(typeof(ValueModel)));

        Assert.Contains(nameof(ValueModel), exception.Message);
    }

    [Fact]
    public async Task GetItem_WithAStructModel_ThrowsInsteadOfReturningAnEmptyModel()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"https://deliver.kontent.ai/{EnvironmentId}/items/coffee_beverages_explained")
            .Respond("application/json", await File.ReadAllTextAsync(Path.Combine(Environment.CurrentDirectory, "Fixtures", "DeliveryClient", "coffee_beverages_explained.json")));
        var services = new ServiceCollection();
        services.AddDeliveryClient(
            new DeliveryOptions { EnvironmentId = EnvironmentId, EnableResilience = false },
            d => d.HttpClient.ConfigurePrimaryHttpMessageHandler(() => mock));
        var client = services.BuildServiceProvider().GetRequiredService<IDeliveryClient>();

        await Assert.ThrowsAsync<NotSupportedException>(() => client.GetItem<ValueModel>("coffee_beverages_explained").ExecuteAsync());
    }
}
