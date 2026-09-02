using System.Reflection;
using AwesomeAssertions;
using Kontent.Ai.Common;

namespace Kontent.Ai.Sync.Tests.Configuration;

/// <summary>
/// <see cref="SyncOptions.CopyTo"/> is reflected rather than listing the properties it carries, which would
/// keep compiling when an option is added and silently stop carrying it. Reflected here for the same
/// reason: naming them would leave a new one uncovered in exactly the case that matters.
/// </summary>
public class SyncOptionsCopyTests
{
    [Fact]
    public void Copy_CarriesEveryWritableProperty()
    {
        var source = new SyncOptions();
        var writable = typeof(SyncOptions)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p is { CanRead: true, CanWrite: true })
            .ToList();

        writable.Should().NotBeEmpty();
        foreach (var property in writable)
        {
            property.SetValue(source, DistinctValueFor(property));
        }

        var target = new SyncOptions();
        OptionsCopier<SyncOptions>.Copy(source, target);

        foreach (var property in writable)
        {
            property.GetValue(target).Should().Be(property.GetValue(source), property.Name);
        }
    }

    [Fact]
    public void CopyTo_CarriesTheValues()
    {
        var source = new SyncOptions { EnvironmentId = "11111111-1111-1111-1111-111111111111" }.UsePreviewApi("preview-key");
        var target = new SyncOptions();

        source.CopyTo(target);

        target.EnvironmentId.Should().Be("11111111-1111-1111-1111-111111111111");
        target.ApiKey.Should().Be("preview-key");
        target.ApiMode.Should().Be(ApiMode.Preview);
    }

    // A value that differs from the property's default, so a property the copy skips fails the comparison.
    private static object DistinctValueFor(PropertyInfo property) => property.PropertyType switch
    {
        var t when t == typeof(string) => $"copied-{property.Name}",
        var t when t == typeof(bool) => true,
        var t when t.IsEnum => Enum.GetValues(t).GetValue(Enum.GetValues(t).Length - 1)!,
        var t when t == typeof(TimeSpan?) => TimeSpan.FromMinutes(7),
        _ => throw new NotSupportedException(
            $"{property.Name} is a {property.PropertyType.Name}; add a distinct value for it here."),
    };
}
