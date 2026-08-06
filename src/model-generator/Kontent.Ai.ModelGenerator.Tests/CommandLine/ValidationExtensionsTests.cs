using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.CommandLine;

namespace Kontent.Ai.ModelGenerator.Tests.CommandLine;

public class ValidationExtensionsTests
{
    private static readonly string EnvironmentId = Guid.NewGuid().ToString();

    [Fact]
    public void Validate_NoDeliveryOptions_Throws()
    {
        var options = new CodeGeneratorOptions();

        var call = options.Validate;

        call.Should().Throw<Exception>()
            .WithMessage("*EnvironmentId*");
    }

    [Fact]
    public void Validate_EmptyEnvironmentId_Throws()
    {
        var options = new CodeGeneratorOptions
        {
            DeliveryOptions = new DeliveryOptions { EnvironmentId = "" },
        };

        var call = options.Validate;

        call.Should().Throw<Exception>().WithMessage("*EnvironmentId*");
    }

    [Fact]
    public void Validate_DeliveryEnvironmentId_Ok()
    {
        var options = new CodeGeneratorOptions
        {
            DeliveryOptions = new DeliveryOptions { EnvironmentId = EnvironmentId },
        };

        var call = options.Validate;

        call.Should().NotThrow();
    }

    [Fact]
    public void ValidateManagement_NoManagementOptions_Throws()
    {
        var options = new CodeGeneratorOptions();

        var call = options.ValidateManagement;

        call.Should().Throw<Exception>().WithMessage("*EnvironmentId*");
    }

    [Fact]
    public void ValidateManagement_EmptyEnvironmentId_Throws()
    {
        var options = new CodeGeneratorOptions
        {
            ManagementOptions = new ManagementOptions { ApiKey = "secret" },
        };

        var call = options.ValidateManagement;

        call.Should().Throw<Exception>().WithMessage("*EnvironmentId*");
    }

    [Fact]
    public void ValidateManagement_EmptyApiKey_Throws()
    {
        var options = new CodeGeneratorOptions
        {
            ManagementOptions = new ManagementOptions { EnvironmentId = "abc-123" },
        };

        var call = options.ValidateManagement;

        call.Should().Throw<Exception>().WithMessage("*ApiKey*");
    }

    [Fact]
    public void ValidateManagement_BothSet_Ok()
    {
        var options = new CodeGeneratorOptions
        {
            ManagementOptions = new ManagementOptions { EnvironmentId = EnvironmentId, ApiKey = "secret" },
        };

        var call = options.ValidateManagement;

        call.Should().NotThrow();
    }

    // The tool used to accept any non-blank environment id, deferring the failure to the first API call.
    // It now runs the SDKs' own validation, which requires a GUID.
    [Fact]
    public void Validate_NonGuidEnvironmentId_Throws()
    {
        var options = new CodeGeneratorOptions
        {
            DeliveryOptions = new DeliveryOptions { EnvironmentId = "abc-123" },
        };

        var call = options.Validate;

        call.Should().Throw<InvalidOperationException>().WithMessage("*EnvironmentId*");
    }

    [Fact]
    public void ValidateManagement_NonGuidEnvironmentId_Throws()
    {
        var options = new CodeGeneratorOptions
        {
            ManagementOptions = new ManagementOptions { EnvironmentId = "abc-123", ApiKey = "secret" },
        };

        var call = options.ValidateManagement;

        call.Should().Throw<InvalidOperationException>().WithMessage("*EnvironmentId*");
    }

    // Every problem is reported at once, so a run with several bad arguments is not a guessing game.
    [Fact]
    public void ValidateManagement_MultipleProblems_ReportsAll()
    {
        var options = new CodeGeneratorOptions
        {
            ManagementOptions = new ManagementOptions { EnvironmentId = "abc-123", ApiKey = "" },
        };

        var call = options.ValidateManagement;

        call.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("EnvironmentId").And.Contain("ApiKey");
    }
}
