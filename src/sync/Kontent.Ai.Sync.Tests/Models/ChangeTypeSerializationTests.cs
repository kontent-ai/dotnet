using System.Text.Json;
using AwesomeAssertions;
using Kontent.Ai.Sync.Configuration;

namespace Kontent.Ai.Sync.Tests.Models;

public class ChangeTypeSerializationTests
{
    [Theory]
    [InlineData(ChangeType.Changed, "changed")]
    [InlineData(ChangeType.Deleted, "deleted")]
    [InlineData(ChangeType.Unknown, "unknown")]
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

    // The API may add change types without notice, so a name the SDK does not know is not an error.
    [Theory]
    [InlineData("archived")]
    [InlineData("Changed")]
    [InlineData("")]
    public void Read_UnrecognizedName_IsUnknown(string wire)
    {
        JsonSerializer.Deserialize<ChangeType>($"\"{wire}\"").Should().Be(ChangeType.Unknown);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("7")]
    [InlineData("null")]
    [InlineData("true")]
    public void Read_NonString_Throws(string wire)
    {
        var act = () => JsonSerializer.Deserialize<ChangeType>(wire);

        act.Should().Throw<JsonException>();
    }
}
