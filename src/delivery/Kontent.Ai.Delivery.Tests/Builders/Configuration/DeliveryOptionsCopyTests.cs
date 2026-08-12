using System.Reflection;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Configuration;

namespace Kontent.Ai.Delivery.Tests.Builders.Configuration;

/// <summary>
/// The copy is what carries a prebuilt options instance into the DI options pattern, and the build is what
/// produces one. Both used to assign property by property, which keeps compiling when an option is added and
/// silently stops carrying it — the caller sets a value and the client never sees it. The copy is reflected
/// here for the same reason it is reflected in the source: naming the properties would leave a new one
/// uncovered in exactly the case that matters.
/// </summary>
public class DeliveryOptionsCopyTests
{
    [Fact]
    public void CopyTo_CarriesEveryWritableProperty()
    {
        var source = new DeliveryOptions();
        var writable = typeof(DeliveryOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p is { CanRead: true, CanWrite: true })
            .ToList();

        Assert.NotEmpty(writable);
        foreach (var property in writable)
        {
            property.SetValue(source, DistinctValueFor(property));
        }

        var destination = new DeliveryOptions();
        source.CopyTo(destination);

        foreach (var property in writable)
        {
            Assert.Equal(property.GetValue(source), property.GetValue(destination));
        }
    }

    [Fact]
    public void Build_CarriesWhatTheBuilderWasTold()
    {
        var built = DeliveryOptionsBuilder.CreateInstance()
            .WithEnvironmentId("11111111-1111-1111-1111-111111111111")
            .UsePreviewApi("preview-key")
            .WithCustomEndpoint("https://preview.example.com/{0}")
            .WithDefaultRenditionPreset("mobile")
            .WithCustomAssetDomain("assets.example.com")
            .DisableRetryPolicy()
            .Build();

        Assert.Equal("11111111-1111-1111-1111-111111111111", built.EnvironmentId);
        Assert.Equal("preview-key", built.PreviewApiKey);
        Assert.True(built.UsePreviewApi);
        Assert.Equal("https://preview.example.com/{0}", built.PreviewEndpoint);
        Assert.Equal("mobile", built.DefaultRenditionPreset);
        Assert.Equal("assets.example.com", built.CustomAssetDomain);
        Assert.False(built.EnableResilience);
    }

    // A value that differs from the property's default, so a property the copy skips fails the comparison.
    private static object DistinctValueFor(PropertyInfo property) => property.PropertyType switch
    {
        var t when t == typeof(string) => $"copied-{property.Name}",
        var t when t == typeof(bool) => true,
        var t when t == typeof(int) => 42,
        var t when Nullable.GetUnderlyingType(t) == typeof(int) => 42,
        var t when t.IsEnum => Enum.GetValues(t).GetValue(Enum.GetValues(t).Length - 1)!,
        _ => throw new NotSupportedException(
            $"{property.Name} is a {property.PropertyType.Name}; add a distinct value for it here."),
    };
}
