using System.Text.Json;
using AwesomeAssertions;
using Kontent.Ai.Sync.Configuration;

namespace Kontent.Ai.Sync.Tests.Models;

/// <summary>
/// A delta read from the API and written back out has to produce the value the API sends. The type-level
/// converter takes precedence over the one the SDK configures and carried no naming policy, so writing
/// produced <c>"Changed"</c> against the wire's <c>"changed"</c> - readable, but not round-trippable.
/// </summary>
public class ChangeTypeSerializationTests
{
    [Theory]
    [InlineData(ChangeType.Changed, "changed")]
    [InlineData(ChangeType.Deleted, "deleted")]
    public void Write_UsesTheWireName(ChangeType value, string expected)
    {
        JsonSerializer.Serialize(value).Should().Be($"\"{expected}\"");
        JsonSerializer.Serialize(value, RefitSettingsProvider.CreateDefaultJsonSerializerOptions())
            .Should().Be($"\"{expected}\"");
    }

    [Theory]
    [InlineData("changed", ChangeType.Changed)]
    [InlineData("deleted", ChangeType.Deleted)]
    public void Read_AcceptsTheWireName(string wire, ChangeType expected)
    {
        JsonSerializer.Deserialize<ChangeType>($"\"{wire}\"").Should().Be(expected);
    }
}
