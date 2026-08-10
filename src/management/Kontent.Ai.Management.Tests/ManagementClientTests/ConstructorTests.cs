using AwesomeAssertions;
using Kontent.Ai.Management.Configuration;
using System.ComponentModel.DataAnnotations;

namespace Kontent.Ai.Management.Tests.ManagementClientTests;

public class ConstructorTests
{
    [Fact]
    public void Ctor_ValidOptions_DoesNotThrow()
    {
        var options = new ManagementOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiKey = "valid-key",
        };

        Action act = () => new ManagementClient(options);
        act.Should().NotThrow();
    }

    [Fact]
    public void Ctor_NullOptions_ThrowsArgumentNull()
    {
        Action act = () => new ManagementClient(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Ctor_SubscriptionIdWithoutEnvironmentId_DoesNotThrow()
    {
        // Subscription endpoints are not scoped to an environment, so a client that only calls them
        // has no environment to name.
        var options = new ManagementOptions
        {
            SubscriptionId = Guid.NewGuid().ToString(),
            ApiKey = "valid-key",
        };

        Action act = () => new ManagementClient(options);

        act.Should().NotThrow();
    }

    [Fact]
    public void Ctor_NeitherEnvironmentIdNorSubscriptionId_ThrowsValidationException()
    {
        // Scoped to nothing, so it could not call anything - worth failing at registration rather than
        // on the first request.
        var options = new ManagementOptions { ApiKey = "valid-key" };

        Action act = () => new ManagementClient(options);

        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public async Task EnvironmentEndpoint_OnASubscriptionOnlyClient_ThrowsNamingTheMissingOption()
    {
        await using var client = new ManagementClient(new ManagementOptions
        {
            SubscriptionId = Guid.NewGuid().ToString(),
            ApiKey = "valid-key",
        });

        var act = async () => await client.GetEnvironmentInformationAsync();

        // Fails before any request is built: without this the base address carries an empty path segment
        // and the API answers with a 404 that says nothing about the cause.
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*EnvironmentId is not configured*");
    }

    [Fact]
    public async Task SubscriptionEndpoint_OnAnEnvironmentOnlyClient_ThrowsNamingTheMissingOption()
    {
        await using var client = new ManagementClient(new ManagementOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiKey = "valid-key",
        });

        var act = async () => await client.ListSubscriptionProjectsAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*SubscriptionId is not configured*");
    }

    [Theory]
    [InlineData(null, "key")]                                              // missing env id ([Required])
    [InlineData("no-guid", "key")]                                         // non-GUID env id (IValidatableObject)
    [InlineData("00000000-0000-0000-0000-000000000000", "key")]            // Guid.Empty (IValidatableObject)
    [InlineData("4ee3d5cc-2e5b-4c81-9f4c-6a8f7b5d3c1e", null)]              // missing api key ([Required])
    public void Ctor_InvalidOptions_ThrowsValidationException(string? envId, string? apiKey)
    {
        // Validation goes through Validator.ValidateObject inside BuildDependencies, surfacing as
        // ValidationException — consistent with the builder path and with the standard .NET options pattern.
        var options = new ManagementOptions
        {
            EnvironmentId = envId!,
            ApiKey = apiKey!,
        };

        Action act = () => new ManagementClient(options);

        act.Should().Throw<ValidationException>();
    }

    [Theory]
    [InlineData(0)]      // no time to do anything
    [InlineData(-1)]     // not a duration
    public void Ctor_NonPositiveTimeout_ThrowsValidationException(int minutes)
    {
        var options = ValidOptions();
        options.Timeout = TimeSpan.FromMinutes(minutes);

        Action act = () => new ManagementClient(options);

        act.Should().Throw<ValidationException>().WithMessage("*Timeout*");
    }

    [Fact]
    public void Ctor_InfiniteTimeout_IsAccepted()
    {
        // The one non-positive value that means something: bounded only by the caller's token.
        var options = ValidOptions();
        options.Timeout = Timeout.InfiniteTimeSpan;

        using var client = new ManagementClient(options);

        client.Should().NotBeNull();
    }

    private static ManagementOptions ValidOptions() => new()
    {
        EnvironmentId = Guid.NewGuid().ToString(),
        ApiKey = "key",
    };

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Ctor_InvalidSubscriptionId_ThrowsValidationException(string subscriptionId)
    {
        var options = new ManagementOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiKey = "key",
            SubscriptionId = subscriptionId,
        };

        Action act = () => new ManagementClient(options);

        act.Should().Throw<ValidationException>().WithMessage("*subscription identifier*");
    }

    [Fact]
    public async Task Ctor_WithoutSubscriptionId_SubscriptionEndpointsThrow()
    {
        await using var client = new ManagementClient(new ManagementOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiKey = "key",
        });

        var act = () => client.ListSubscriptionProjectsAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SubscriptionId is not configured*");
    }
}
