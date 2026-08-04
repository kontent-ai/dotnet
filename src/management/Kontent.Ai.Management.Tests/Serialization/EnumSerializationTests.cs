using AwesomeAssertions;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Models.Types.Elements;
using Kontent.Ai.Management.Models.Workflow;
using Kontent.Ai.Management.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kontent.Ai.Management.Tests.Serialization;

public class EnumSerializationTests
{
    // Mirrors the production registration in RefitSettingsProvider.
    private static JsonSerializerOptions Options() =>
        new() { Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) } };

    [Fact]
    public void Write_UsesWireToken_NotMemberName()
    {
        var options = Options();

        JsonSerializer.Serialize(ElementMetadataType.LinkedItems, options).Should().Be("\"modular_content\"");
        JsonSerializer.Serialize(ElementMetadataType.RichText, options).Should().Be("\"rich_text\"");
        JsonSerializer.Serialize(ElementMetadataType.ContentTypeSnippet, options).Should().Be("\"snippet\"");
    }

    [Theory]
    // The wire format is not one shape: snake_case, kebab-case and camelCase all appear, which is why every
    // member names its token explicitly instead of relying on a naming policy.
    [InlineData(WorkflowStepColor.LightPurple, "\"light-purple\"")]
    [InlineData(WorkflowStepColor.PersianGreen, "\"persian-green\"")]
    [InlineData(WorkflowStepColor.Gray, "\"gray\"")]
    public void NonSnakeCaseTokens_RoundTrip(WorkflowStepColor value, string expected)
    {
        var options = Options();

        JsonSerializer.Serialize(value, options).Should().Be(expected);
        JsonSerializer.Deserialize<WorkflowStepColor>(expected, options).Should().Be(value);
    }

    [Fact]
    public void Read_IsCaseSensitive()
    {
        var options = Options();

        JsonSerializer.Deserialize<ElementMetadataType>("\"modular_content\"", options).Should().Be(ElementMetadataType.LinkedItems);

        // Only the exact wire token is accepted. The Management API emits canonical tokens, so anything else is a
        // caller error worth surfacing rather than quietly repairing.
        var miscased = () => JsonSerializer.Deserialize<ElementMetadataType>("\"MODULAR_CONTENT\"", options);
        miscased.Should().Throw<JsonException>();

        // The C# member name is not a wire token either.
        var memberName = () => JsonSerializer.Deserialize<ElementMetadataType>("\"LinkedItems\"", options);
        memberName.Should().Throw<JsonException>();
    }

    [Fact]
    public void Write_UndefinedValue_Throws()
    {
        // This is a write API — a cast like (ElementMetadataType)999 must fail fast, not ship "999" as a wire token.
        var act = () => JsonSerializer.Serialize((ElementMetadataType)999, Options());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_NumberToken_Throws()
    {
        // The Management API is a string-token API; a bare number would mint an unchecked enum value.
        var act = () => JsonSerializer.Deserialize<ElementMetadataType>("7", Options());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_UnknownString_Throws()
    {
        var act = () => JsonSerializer.Deserialize<ElementMetadataType>("\"not_a_type\"", Options());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_UndefinedNumericString_Throws()
    {
        var act = () => JsonSerializer.Deserialize<ElementMetadataType>("\"999\"", Options());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void NullableEnum_RoundTrips()
    {
        var options = Options();

        JsonSerializer.Serialize<ElementMetadataType?>(null, options).Should().Be("null");
        JsonSerializer.Serialize<ElementMetadataType?>(ElementMetadataType.Asset, options).Should().Be("\"asset\"");
        JsonSerializer.Deserialize<ElementMetadataType?>("null", options).Should().BeNull();
        JsonSerializer.Deserialize<ElementMetadataType?>("\"asset\"", options).Should().Be(ElementMetadataType.Asset);
    }

    [Fact]
    public void EnumDictionaryKey_UsesWireToken()
    {
        var options = Options();
        var json = JsonSerializer.Serialize(new Dictionary<ElementMetadataType, int> { [ElementMetadataType.RichText] = 1 }, options);

        json.Should().Be("{\"rich_text\":1}");
        JsonSerializer.Deserialize<Dictionary<ElementMetadataType, int>>(json, options)!
            .Should().ContainKey(ElementMetadataType.RichText);
    }

    [Fact]
    public void Registered_InRefitSettingsProviderOptions()
    {
        var options = RefitSettingsProvider.CreateDefaultJsonSerializerOptions();

        JsonSerializer.Serialize(ElementMetadataType.UrlSlug, options).Should().Be("\"url_slug\"");
        JsonSerializer.Deserialize<ElementMetadataType>("\"url_slug\"", options).Should().Be(ElementMetadataType.UrlSlug);
    }

    [Fact]
    public void EnumWire_AgreesWithTheSerializer_ForEveryMember()
    {
        // EnumWire resolves tokens for PATCH path segments and polymorphic discriminators, with no serializer
        // involved. It reads the same attribute but is a separate implementation rather than a shared map, so
        // pin that the two cannot disagree about what a value is called on the wire.
        var options = Options();

        foreach (var value in Enum.GetValues<ElementMetadataType>())
        {
            var serialized = JsonSerializer.Serialize(value, options).Trim('"');

            EnumWire.ToValue(value).Should().Be(serialized);
            EnumWire.TryFromValue<ElementMetadataType>(serialized, out var parsed).Should().BeTrue();
            parsed.Should().Be(value);
        }
    }

    [Fact]
    public void EnumWire_UndefinedValue_Throws()
    {
        var act = () => EnumWire.ToValue((ElementMetadataType)999);

        act.Should().Throw<ArgumentException>().WithMessage("*999*");
    }
}
