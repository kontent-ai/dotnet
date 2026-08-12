using System.Text.Json;
using AwesomeAssertions;

namespace Kontent.Ai.Management.Tests.Serialization;

/// <summary>
/// Every <c>display_timezone</c> the API sends is an IANA zone name, so a fixture carrying anything else
/// records a contract the server does not have and nothing built on reading it can be trusted. Resolution
/// is checked rather than the shape, because passing the value to <see cref="TimeZoneInfo"/> is what a
/// consumer does with it, and a name that does not resolve is one no consumer can use.
/// </summary>
public class DisplayTimeZoneContractTests
{
    private static readonly string[] ZoneProperties =
        ["display_timezone", "publish_display_timezone", "unpublish_display_timezone"];

    [Fact]
    public void EveryFixtureDisplayTimeZone_IsAResolvableIanaName()
    {
        var dataRoot = Path.Combine(Environment.CurrentDirectory, "Data");
        var files = Directory.GetFiles(dataRoot, "*.json", SearchOption.AllDirectories);
        files.Should().NotBeEmpty();

        var offenders = files
            .SelectMany(file => ZoneValuesIn(file).Select(zone => (File: Path.GetFileName(file), Zone: zone)))
            .Where(found => !TimeZoneInfo.TryFindSystemTimeZoneById(found.Zone, out _))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty(
            "the API sends IANA zone names; found {0}",
            string.Join(", ", offenders.Select(o => $"\"{o.Zone}\" in {o.File}")));
    }

    private static IEnumerable<string> ZoneValuesIn(string file)
    {
        // A fixture may be an empty body, which is a response the API really does send.
        var json = File.ReadAllText(file);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return [.. Collect(document.RootElement)];
    }

    private static IEnumerable<string> Collect(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (ZoneProperties.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        yield return property.Value.GetString()!;
                    }

                    foreach (var nested in Collect(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var nested in element.EnumerateArray().SelectMany(Collect))
                {
                    yield return nested;
                }

                break;
        }
    }
}
