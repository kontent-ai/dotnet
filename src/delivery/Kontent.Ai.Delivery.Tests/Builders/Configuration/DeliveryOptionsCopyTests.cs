using System.Reflection;
using Kontent.Ai.Delivery.Abstractions;

namespace Kontent.Ai.Delivery.Tests.Builders.Configuration;

/// <summary>
/// <see cref="DeliveryOptions.CopyTo"/> carries a prebuilt options instance into the DI options pattern. It is
/// reflected rather than listing the properties it carries, which would keep compiling when an option is added
/// and silently stop carrying it. Reflected here for the same reason: naming them would leave a new one
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
    public void CopyTo_CarriesTheValues()
    {
        var source = new DeliveryOptions
        {
            EnvironmentId = "11111111-1111-1111-1111-111111111111",
            DefaultRenditionPreset = "mobile",
            CustomAssetDomain = "assets.example.com",
            EnableResilience = false
        }.UsePreviewApi("preview-key").UseCustomEndpoint("https://preview.example.com/{0}");
        var destination = new DeliveryOptions();

        source.CopyTo(destination);

        Assert.Equal("11111111-1111-1111-1111-111111111111", destination.EnvironmentId);
        Assert.Equal("preview-key", destination.PreviewApiKey);
        Assert.True(destination.UsePreviewApi);
        Assert.Equal("https://preview.example.com/{0}", destination.PreviewEndpoint);
        Assert.Equal("mobile", destination.DefaultRenditionPreset);
        Assert.Equal("assets.example.com", destination.CustomAssetDomain);
        Assert.False(destination.EnableResilience);
    }

    // A value that differs from the property's default, so a property the copy skips fails the comparison.
    private static object DistinctValueFor(PropertyInfo property) => property.PropertyType switch
    {
        var t when t == typeof(string) => $"copied-{property.Name}",
        var t when t == typeof(bool) => true,
        var t when t == typeof(int) => 42,
        var t when Nullable.GetUnderlyingType(t) == typeof(int) => 42,
        var t when t == typeof(TimeSpan?) => TimeSpan.FromMinutes(7),
        var t when t.IsEnum => Enum.GetValues(t).GetValue(Enum.GetValues(t).Length - 1)!,
        _ => throw new NotSupportedException(
            $"{property.Name} is a {property.PropertyType.Name}; add a distinct value for it here."),
    };
}
