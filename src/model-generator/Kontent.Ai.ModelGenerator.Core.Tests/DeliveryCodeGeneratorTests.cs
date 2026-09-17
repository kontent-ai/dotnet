using System.Net;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Contract;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Kontent.Ai.ModelGenerator.Core.Tests;

public class DeliveryCodeGeneratorTests
{
    private readonly IDeliveryClient _client = Substitute.For<IDeliveryClient>();
    private readonly IOutputProvider _output = Substitute.For<IOutputProvider>();
    private readonly IUserMessageLogger _logger = Substitute.For<IUserMessageLogger>();
    private readonly ClassCodeGeneratorFactory _classCodeGeneratorFactory = new();

    [Fact]
    public async Task RunAsync_ApiRejectsTheKey_ThrowsNamingTheApiKeyArgument()
    {
        SetupClientWith(FailedListing(
            HttpStatusCode.Unauthorized,
            "Missing or invalid access token. Please include the valid access token value in the Authorization header field as an HTTP bearer authorization scheme."));

        var act = () => CreateGenerator().RunAsync();

        // The API's own 401 text is about forming an Authorization header, which is not the mistake made here.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*--apikey*")
            .And.Message.Should().NotContain("bearer authorization scheme");
    }

    [Fact]
    public async Task RunAsync_ApiReturnsAnError_ThrowsWithTheApiMessageAndStatusCode()
    {
        SetupClientWith(FailedListing(HttpStatusCode.NotFound, "The requested environment was not found."));

        var act = () => CreateGenerator().RunAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*404*")
            .And.Message.Should().Contain("The requested environment was not found.");
    }

    [Fact]
    public async Task RunAsync_ErrorCarriesRequestId_ThrowsWithItIncluded()
    {
        SetupClientWith(FailedListing(HttpStatusCode.NotFound, "Nope.", requestId: "abc-123"));

        var act = () => CreateGenerator().RunAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .And.Message.Should().Contain("abc-123");
    }

    [Fact]
    public async Task RunAsync_ApiReturnsAnError_WritesNothing()
    {
        SetupClientWith(FailedListing(HttpStatusCode.Unauthorized, "Nope."));

        await CreateGenerator().Invoking(g => g.RunAsync()).Should().ThrowAsync<InvalidOperationException>();

        _output.DidNotReceive().Output(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task RunAsync_EnvironmentHasNoTypes_ReportsItAndSucceeds()
    {
        // The one case the "no content type available" message was always right for.
        var environmentId = Guid.NewGuid().ToString();
        SetupClientWith(SuccessListing());

        var result = await CreateGenerator(environmentId: environmentId).RunAsync();

        result.Should().Be(0);
        _logger.Received().LogInfo(Arg.Is<string>(m => m != null && m.Contains(environmentId)));
        _output.DidNotReceive().Output(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task RunAsync_SuccessfulListing_WritesOneFilePerType()
    {
        SetupClientWith(SuccessListing(BuildContentType("article"), BuildContentType("author")));

        var result = await CreateGenerator().RunAsync();

        result.Should().Be(0);
        _output.Received(1).Output(Arg.Any<string>(), "Article", true);
        _output.Received(1).Output(Arg.Any<string>(), "Author", true);
    }

    private void SetupClientWith(IDeliveryResult<IDeliveryTypeListingResponse> result)
    {
        var query = Substitute.For<ITypesQuery>();
        query.ExecuteAsync().Returns(result);
        _client.GetTypes().Returns(query);
    }

    private static IDeliveryResult<IDeliveryTypeListingResponse> SuccessListing(params IContentType[] types)
    {
        var response = Substitute.For<IDeliveryTypeListingResponse>();
        response.Types.Returns(types);

        var result = Substitute.For<IDeliveryResult<IDeliveryTypeListingResponse>>();
        result.IsSuccess.Returns(true);
        result.Value.Returns(response);
        return result;
    }

    private static IDeliveryResult<IDeliveryTypeListingResponse> FailedListing(
        HttpStatusCode statusCode,
        string message,
        string? requestId = null)
    {
        var error = Substitute.For<IError>();
        error.Message.Returns(message);
        error.RequestId.Returns(requestId);

        var result = Substitute.For<IDeliveryResult<IDeliveryTypeListingResponse>>();
        result.IsSuccess.Returns(false);
        result.StatusCode.Returns(statusCode);
        result.Error.Returns(error);
        return result;
    }

    private static IContentType BuildContentType(string codename)
    {
        var system = Substitute.For<IContentTypeSystemAttributes>();
        system.Codename.Returns(codename);

        var element = Substitute.For<IContentElement>();
        element.Type.Returns("text");

        var contentType = Substitute.For<IContentType>();
        contentType.System.Returns(system);
        contentType.Elements.Returns(new Dictionary<string, IContentElement> { ["title"] = element });
        return contentType;
    }

    private DeliveryCodeGenerator CreateGenerator(string? environmentId = null) =>
        new(
            Options.Create(new CodeGeneratorOptions
            {
                DeliveryOptions = new DeliveryOptions
                {
                    EnvironmentId = environmentId ?? Guid.NewGuid().ToString(),
                },
            }),
            _output,
            _client,
            _classCodeGeneratorFactory,
            _logger);
}
