using System.Net;
using AwesomeAssertions;
using Kontent.Ai.Management.Conversion;
using Kontent.Ai.Management.Models.LanguageVariants;
using Kontent.Ai.Management.Tests.Base;
using MyProject.Models;
using RichardSzalay.MockHttp;

namespace Kontent.Ai.Management.Tests.ManagementClientTests;

/// <summary>
/// What reaches the caller when the call does not succeed. The result pattern is only worth documenting
/// as an absolute if it holds for the failures that are not API errors too - a consumer who is told some
/// failures throw will write a <c>catch</c> that never runs, and skip the <c>IsSuccess</c> check that
/// would have caught them.
/// </summary>
public class FailureShapeTests
{
    [Fact]
    public async Task ApiError_IsAFailedResult()
    {
        var (client, mock) = MockClientFactory.Create();
        mock.When($"{MockClientFactory.BaseUrl}/items/codename/x")
            .Respond(HttpStatusCode.NotFound, "application/json", """{"message":"not found","request_id":"r","error_code":100}""");

        var result = await client.GetContentItemAsync(Reference.ByCodename("x"));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TransportFailure_IsAFailedResultCarryingTheException()
    {
        var (client, mock) = MockClientFactory.Create();
        mock.When($"{MockClientFactory.BaseUrl}/items/codename/x")
            .Respond(_ => throw new HttpRequestException("No such host is known."));

        var result = await client.GetContentItemAsync(Reference.ByCodename("x"));

        result.IsSuccess.Should().BeFalse();
        result.Error!.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task UnreadableSuccessBody_IsAFailedResult()
    {
        // A 200 whose body does not deserialize. Refit captures that into the response rather than
        // throwing, so it arrives as a failed result like any other.
        var (client, mock) = MockClientFactory.Create();
        mock.When($"{MockClientFactory.BaseUrl}/items/codename/x")
            .Respond(HttpStatusCode.OK, "application/json", "{ this is not json");

        var result = await client.GetContentItemAsync(Reference.ByCodename("x"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task CallerCancellation_IsTheOneThingThatThrows()
    {
        var (client, mock) = MockClientFactory.Create();
        using var cts = new CancellationTokenSource();
        mock.When($"{MockClientFactory.BaseUrl}/items/codename/x")
            .Respond(_ =>
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        var act = async () => await client.GetContentItemAsync(Reference.ByCodename("x"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TypedVariantConversionFailure_Throws()
    {
        // The typed overloads project the response onto the generated record after the result is built, and
        // that projection is not guarded - so a model that has drifted from the content type surfaces as a
        // throw rather than a failed result. The only exception besides cancellation, and worth documenting
        // on those overloads specifically.
        var registry = new ContentTypeRegistry();
        registry.Register(typeof(Callout));
        var (client, mock) = MockClientFactory.Create(new ContentItemEnvelopeConverter(registry));

        var fixture = await File.ReadAllTextAsync(Path.Combine(
            Environment.CurrentDirectory, "Data", "LanguageVariant", "StronglyTypedCalloutVariant.json"));
        var body = fixture.Replace("\"warning\"", "\"unknown-option\"");

        mock.When($"{MockClientFactory.BaseUrl}/items/codename/my_article/variants/codename/en-US")
            .Respond(HttpStatusCode.OK, "application/json", body);

        var identifier = new LanguageVariantIdentifier(
            Reference.ByCodename("my_article"),
            Reference.ByCodename("en-US"));

        var act = async () => await client.GetLanguageVariantAsync<Callout>(identifier);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unknown-option*");
    }
}
