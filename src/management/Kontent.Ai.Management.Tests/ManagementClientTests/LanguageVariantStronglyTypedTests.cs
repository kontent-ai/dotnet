using AwesomeAssertions;
using Kontent.Ai.Management.Conversion;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.LanguageVariants;
using Kontent.Ai.Management.Tests.Base;
using MyProject.Models;
using RichardSzalay.MockHttp;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CustomValue = Kontent.Ai.Management.Models.Content.CustomValue;
using DateTimeValue = Kontent.Ai.Management.Models.Content.DateTimeValue;
// Models.Content carries its own `Reference`; alias the types we need so it doesn't collide with
// Models.Shared.Reference used for the identifier.
using RichTextBuilder = Kontent.Ai.Management.Models.Content.RichTextBuilder;
using RichTextValue = Kontent.Ai.Management.Models.Content.RichTextValue;
using UrlSlugMode = Kontent.Ai.Management.Models.Content.UrlSlugMode;
using UrlSlugValue = Kontent.Ai.Management.Models.Content.UrlSlugValue;

namespace Kontent.Ai.Management.Tests.ManagementClientTests;

// Strongly-typed (generated-record) language-variant coverage: the result-pattern contract (success projection,
// validation short-circuit, HTTP-failure→result) plus arg guards. The identifier→URL matrix stays covered by the
// non-generic theories in LanguageVariantTests, which exercise the same ToUrlSegment + Refit endpoint the
// generics call — deliberately not re-matrixed here.
public class LanguageVariantStronglyTypedTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(Environment.CurrentDirectory, "Data", "LanguageVariant", name));

    private static LanguageVariantIdentifier Identifier() =>
        new(Reference.ByCodename("my_article"), Reference.ByCodename("en-US"));

    private static string ArticleVariant => Fixture("StronglyTypedArticleVariant.json");

    private static string ArticleVariantsPage(string? continuationToken) => new JsonObject
    {
        ["variants"] = new JsonArray(JsonNode.Parse(ArticleVariant)),
        ["pagination"] = new JsonObject { ["continuation_token"] = continuationToken },
    }.ToJsonString();

    private static readonly Reference TypeIdentifier = Reference.ById(Guid.Parse("17ff8a28-ebe6-5c9d-95ea-18fe1ff86d2d"));

    private static string VariantUrl =>
        $"{MockClientFactory.BaseUrl}/items/codename/my_article/variants/codename/en-US";

    // The client's owned converter auto-scans typeof(T).Assembly; in the test assembly that trips the deliberate
    // StubModels/MyProject codename collision. Inject a converter with just the types under test registered.
    private static ContentItemEnvelopeConverter Converter()
    {
        var registry = new ContentTypeRegistry();
        registry.Register(typeof(Callout));
        return new ContentItemEnvelopeConverter(registry);
    }

    private static ContentItemEnvelopeConverter ArticleConverter()
    {
        var registry = new ContentTypeRegistry();
        registry.Register(typeof(Article));
        return new ContentItemEnvelopeConverter(registry);
    }

    [Fact]
    public async Task GetLanguageVariantAsync_StronglyTyped_IdentifierIsNull_Throws()
    {
        var (client, _) = MockClientFactory.Create();

        await client.Invoking(x => x.GetLanguageVariantAsync<Article>(null!))
            .Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpsertLanguageVariantAsync_StronglyTyped_IdentifierIsNull_Throws()
    {
        var (client, _) = MockClientFactory.Create();

        await client.Invoking(x => x.UpsertLanguageVariantAsync(null!, new Article()))
            .Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UpsertLanguageVariantAsync_StronglyTyped_VariantIsNull_Throws()
    {
        var (client, _) = MockClientFactory.Create();

        await client.Invoking(x => x.UpsertLanguageVariantAsync<Article>(Identifier(), null!))
            .Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetPublishedLanguageVariantAsync_StronglyTyped_ProjectsValue()
    {
        var (client, mock) = MockClientFactory.Create(Converter());
        mock.Expect(HttpMethod.Get, $"{VariantUrl}/published")
            .Respond("application/json", Fixture("StronglyTypedCalloutVariant.json"));

        var result = await client.GetPublishedLanguageVariantAsync<Callout>(Identifier());

        mock.VerifyNoOutstandingExpectation();
        result.IsSuccess.Should().BeTrue();
        result.Value.Elements.Type.Should().Equal(CalloutType.Warning);
    }

    [Fact]
    public async Task GetPublishedLanguageVariantAsync_StronglyTyped_IdentifierIsNull_Throws()
    {
        var (client, _) = MockClientFactory.Create();

        await client.Invoking(x => x.GetPublishedLanguageVariantAsync<Callout>(null!))
            .Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetLanguageVariantAsync_StronglyTyped_Success_ProjectsValueAndStatus()
    {
        var (client, mock) = MockClientFactory.Create(Converter());
        mock.Expect(HttpMethod.Get, VariantUrl)
            .Respond("application/json", Fixture("StronglyTypedCalloutVariant.json"));

        var result = await client.GetLanguageVariantAsync<Callout>(Identifier());

        mock.VerifyNoOutstandingExpectation();
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Error.Should().BeNull();

        var callout = result.Value.Elements;
        callout.Type.Should().Equal(CalloutType.Warning);
        callout.Content!.Value.Should().StartWith("<p>outer</p>");

        // The wrapper preserves the variant metadata the response carries (previously dropped on the typed path).
        result.Value.Item.Should().NotBeNull();
        result.Value.Language.Should().NotBeNull();
        result.Value.Workflow.Should().NotBeNull();

        // Rich-text component recursed through the client: bridge → converter → nested record.
        var nested = callout.Content.Components.Should().ContainSingle()
            .Which.Content.Should().BeOfType<Callout>().Subject;
        nested.Content!.Value.Should().Be("<p>inner</p>");
        nested.Type.Should().Equal(CalloutType.Info);
    }

    [Fact]
    public async Task UpsertLanguageVariantAsync_StronglyTyped_Success_SendsAndProjects()
    {
        var (client, mock) = MockClientFactory.Create(Converter());
        mock.Expect(HttpMethod.Put, VariantUrl)
            .Respond("application/json", Fixture("StronglyTypedCalloutVariant.json"));

        // RichTextBuilder → validator precheck (passes) → write-bridge → PUT → response read-bridge → projection.
        var rt = new RichTextBuilder();
        var callout = new Callout
        {
            Type = [CalloutType.Warning],
            Content = rt.Build($"<p>body</p>{rt.Component(new Callout { Type = [CalloutType.Info], Content = new RichTextValue { Value = "<p>inner</p>" } })}"),
        };

        var result = await client.UpsertLanguageVariantAsync(Identifier(), callout);

        mock.VerifyNoOutstandingExpectation();
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(HttpStatusCode.OK);
        result.Value.Elements.Type.Should().Equal(CalloutType.Warning);
    }

    [Fact]
    public async Task GetLanguageVariantAsync_StronglyTyped_ReadsElementSiblingFields()
    {
        // Full client read path (wire → DynamicElement envelopes → ProjectElements → ReadEnvelopes) must carry the
        // non-value sibling fields the wrappers add: datetime's display_timezone, url_slug's mode, custom's searchable_value.
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        mock.Expect(HttpMethod.Get, VariantUrl)
            .Respond("application/json", Fixture("StronglyTypedArticleVariant.json"));

        var article = (await client.GetLanguageVariantAsync<Article>(Identifier())).Value.Elements;

        mock.VerifyNoOutstandingExpectation();
        article.PublishingDate!.Value.Should().Be(new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero));
        article.PublishingDate.DisplayTimeZone.Should().Be("Europe/Prague");
        article.Slug!.Value.Should().Be("on-roasts");
        article.Slug.Mode.Should().Be(UrlSlugMode.Custom);
        article.Rating!.Value.Should().Be("{\"formId\":42}");
        article.Rating.SearchableValue.Should().Be("Almighty form!");
    }

    [Fact]
    public async Task UpsertLanguageVariantAsync_StronglyTyped_SendsElementSiblingFields()
    {
        // Full client write path (ToElements → Refit serialize → wire) must preserve the sibling fields
        // through the intermediate DynamicElement round-trip.
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        mock.Expect(HttpMethod.Put, VariantUrl)
            .CaptureBody(out var sentBody)
            .Respond("application/json", Fixture("StronglyTypedArticleVariant.json"));

        var article = new Article
        {
            Title = "On Roasts",
            PublishingDate = new DateTimeValue { Value = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero), DisplayTimeZone = "Europe/Prague" },
            Slug = new UrlSlugValue { Value = "on-roasts", Mode = UrlSlugMode.Custom },
            Rating = new CustomValue { Value = "{\"formId\":42}", SearchableValue = "Almighty form!" },
        };

        await client.UpsertLanguageVariantAsync(Identifier(), article);

        mock.VerifyNoOutstandingExpectation();
        var elements = JsonDocument.Parse(sentBody.Value!).RootElement.GetProperty("elements")
            .EnumerateArray()
            .ToDictionary(e => e.GetProperty("element").GetProperty("codename").GetString()!, e => e);
        elements["publishing_date"].GetProperty("value").GetString().Should().Be("2024-06-01T12:00:00Z");
        elements["publishing_date"].GetProperty("display_timezone").GetString().Should().Be("Europe/Prague");
        elements["slug"].GetProperty("mode").GetString().Should().Be("custom");
        elements["rating"].GetProperty("searchable_value").GetString().Should().Be("Almighty form!");
    }

    [Fact]
    public async Task GetLanguageVariantAsync_StronglyTyped_HttpFailure_ReturnsFailureWithStatusAndErrors()
    {
        const string body = """
            { "message": "The requested content item was not found.",
              "validation_errors": [ { "message": "Item 'x' does not exist." } ] }
            """;
        var (client, mock) = MockClientFactory.Create();
        mock.Expect(HttpMethod.Get, VariantUrl)
            .Respond(HttpStatusCode.NotFound, "application/json", body);

        var result = await client.GetLanguageVariantAsync<Callout>(Identifier());

        mock.VerifyNoOutstandingExpectation();
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
        result.Error!.Message.Should().Contain("not found");
        result.Error.ValidationErrors.Should().Contain(e => e.Message.Contains("does not exist"));
    }

    [Fact]
    public async Task ListLanguageVariantsByItemAsync_StronglyTyped_ProjectsEachVariant()
    {
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        var item = Reference.ById(Guid.Parse("4b628214-e4fe-4fe0-b1ff-955df33e1515"));
        mock.Expect(HttpMethod.Get, $"{MockClientFactory.BaseUrl}/items/{item.Id}/variants")
            .Respond("application/json", new JsonArray(JsonNode.Parse(ArticleVariant)).ToJsonString());

        var result = await client.ListLanguageVariantsByItemAsync<Article>(item);

        mock.VerifyNoOutstandingExpectation();
        result.IsSuccess.Should().BeTrue();
        var variant = result.Value.Should().ContainSingle().Subject;
        variant.Elements.Title.Should().Be("On Roasts");
        variant.LastModified.Should().Be(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task ListLanguageVariantsByTypeAsync_StronglyTyped_PagesThroughAllVariants()
    {
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        var url = $"{MockClientFactory.BaseUrl}/types/{TypeIdentifier.Id}/variants";
        mock.Expect(HttpMethod.Get, url).Respond("application/json", ArticleVariantsPage("next"));
        mock.Expect(HttpMethod.Get, url).Respond("application/json", ArticleVariantsPage(null));

        var result = await client.ListLanguageVariantsByTypeAsync<Article>(TypeIdentifier);

        mock.VerifyNoOutstandingExpectation();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2).And.AllSatisfy(v => v.Elements.Title.Should().Be("On Roasts"));
    }

    [Fact]
    public async Task ListLanguageVariantsByTypePageAsync_StronglyTyped_ReturnsOnePageWithToken()
    {
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        mock.Expect(HttpMethod.Get, $"{MockClientFactory.BaseUrl}/types/{TypeIdentifier.Id}/variants")
            .Respond("application/json", ArticleVariantsPage("next"));

        var page = (await client.ListLanguageVariantsByTypePageAsync<Article>(TypeIdentifier)).EnsureSuccess();

        mock.VerifyNoOutstandingExpectation();
        page.ContinuationToken.Should().Be("next");
        page.Items.Should().ContainSingle().Which.Elements.Title.Should().Be("On Roasts");
    }

    [Fact]
    public async Task ListLanguageVariantsOfContentTypeWithComponentsAsync_StronglyTyped_PagesThroughAllVariants()
    {
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        var url = $"{MockClientFactory.BaseUrl}/types/{TypeIdentifier.Id}/components";
        mock.Expect(HttpMethod.Get, url).Respond("application/json", ArticleVariantsPage("next"));
        mock.Expect(HttpMethod.Get, url).Respond("application/json", ArticleVariantsPage(null));

        var result = await client.ListLanguageVariantsOfContentTypeWithComponentsAsync<Article>(TypeIdentifier);

        mock.VerifyNoOutstandingExpectation();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2).And.AllSatisfy(v => v.Elements.Title.Should().Be("On Roasts"));
    }

    [Fact]
    public async Task ListLanguageVariantsOfContentTypeWithComponentsPageAsync_StronglyTyped_ReturnsOnePageWithToken()
    {
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        mock.Expect(HttpMethod.Get, $"{MockClientFactory.BaseUrl}/types/{TypeIdentifier.Id}/components")
            .Respond("application/json", ArticleVariantsPage("next"));

        var page = (await client.ListLanguageVariantsOfContentTypeWithComponentsPageAsync<Article>(TypeIdentifier)).EnsureSuccess();

        mock.VerifyNoOutstandingExpectation();
        page.ContinuationToken.Should().Be("next");
        page.Items.Should().ContainSingle().Which.Elements.Title.Should().Be("On Roasts");
    }

    [Theory]
    [InlineData("item")]
    [InlineData("type")]
    [InlineData("typePage")]
    [InlineData("components")]
    [InlineData("componentsPage")]
    public async Task TypedListings_IdentifierIsNull_Throw(string listing)
    {
        var (client, _) = MockClientFactory.Create();

        Func<Task> act = listing switch
        {
            "item" => () => client.ListLanguageVariantsByItemAsync<Article>(null!),
            "type" => () => client.ListLanguageVariantsByTypeAsync<Article>(null!),
            "typePage" => () => client.ListLanguageVariantsByTypePageAsync<Article>(null!),
            "components" => () => client.ListLanguageVariantsOfContentTypeWithComponentsAsync<Article>(null!),
            _ => () => client.ListLanguageVariantsOfContentTypeWithComponentsPageAsync<Article>(null!),
        };

        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ToTyped_ProjectsFetchedVariantAndCarriesMetadata()
    {
        var (client, mock) = MockClientFactory.Create(ArticleConverter());
        mock.Expect(HttpMethod.Get, VariantUrl).Respond("application/json", ArticleVariant);
        var untyped = (await client.GetLanguageVariantAsync(Identifier())).EnsureSuccess();

        var typed = client.ToTyped<Article>(untyped);

        typed.Elements.Title.Should().Be("On Roasts");
        typed.Elements.Slug!.Mode.Should().Be(UrlSlugMode.Custom);
        typed.Item.Should().Be(untyped.Item);
        typed.Language.Should().Be(untyped.Language);
        typed.LastModified.Should().Be(untyped.LastModified);
        typed.Workflow.Should().Be(untyped.Workflow);
    }

    [Fact]
    public void ToTyped_VariantIsNull_Throws()
    {
        var (client, _) = MockClientFactory.Create();

        client.Invoking(c => c.ToTyped<Article>(null!)).Should().ThrowExactly<ArgumentNullException>();
    }
}
