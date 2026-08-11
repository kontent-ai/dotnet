using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.CommandLine;
using Microsoft.Extensions.Configuration;

namespace Kontent.Ai.ModelGenerator.Tests.CommandLine;

public class ArgHelpersTests
{
    [Theory]
    [InlineData("--environmentid=123")]
    [InlineData("--environmentId=123")]
    [InlineData("--ENVIRONMENTID=123")]
    public void FindInvalidArgs_EnvironmentIdCasingVariants_ReturnsNoProblems(string argument)
    {
        var result = ArgHelpers.FindInvalidArgs([argument]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void GetSwitchMappings_EnvironmentIdMapping_IsCaseInsensitive()
    {
        var mappings = ArgHelpers.GetSwitchMappings([]);

        mappings.ContainsKey("--environmentid").Should().BeTrue();
        mappings.ContainsKey("--environmentId").Should().BeTrue();
        mappings.Keys.Count(key => key.Equals("--environmentid", StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    public static TheoryData<string[]> ValidNullabilityArgs => new()
    {
        new[] { "--nullability=strict" },
        new[] { "--nullability=semantic" },
        new[] { "--Nullability=Semantic" },
        new[] { "--nullability", "strict" },
        new[] { "--nullability", "semantic" },
    };

    [Theory]
    [MemberData(nameof(ValidNullabilityArgs))]
    public void FindInvalidArgs_NullabilityArg_ReturnsNoProblems(string[] args)
    {
        var result = ArgHelpers.FindInvalidArgs(args);

        result.Should().BeEmpty();
    }

    public static TheoryData<string[]> InvalidNullabilityArgs => new()
    {
        new[] { "--nullability=bogus" },
        new[] { "--nullability=" },
        new[] { "--nullability", "bogus" },
        new[] { "--nullability" },
        new[] { "--nullability", "--baseRecord", "Foo" },
    };

    [Theory]
    [MemberData(nameof(InvalidNullabilityArgs))]
    public void FindInvalidArgs_InvalidNullabilityValue_ReturnsProblems(string[] args)
    {
        var result = ArgHelpers.FindInvalidArgs(args);

        result.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("strict", NullabilityMode.Strict)]
    [InlineData("semantic", NullabilityMode.Semantic)]
    [InlineData("Semantic", NullabilityMode.Semantic)]
    [InlineData("SEMANTIC", NullabilityMode.Semantic)]
    public void ConfigurationBinding_NullabilityArg_BindsCodeGeneratorOptions(string value, NullabilityMode expected)
    {
        var args = new[] { $"--nullability={value}" };
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(args, ArgHelpers.GetSwitchMappings(args))
            .Build();

        var options = configuration.Get<CodeGeneratorOptions>()!;

        options.Nullability.Should().Be(expected);
    }

    [Fact]
    public void ConfigurationBinding_NullabilityArgOmitted_DefaultsToStrict()
    {
        var configuration = new ConfigurationBuilder()
            .AddCommandLine([], ArgHelpers.GetSwitchMappings([]))
            .Build();

        var options = configuration.Get<CodeGeneratorOptions>() ?? new CodeGeneratorOptions();

        options.Nullability.Should().Be(NullabilityMode.Strict);
    }

    [Theory]
    [InlineData("-m")]
    [InlineData("--management")]
    [InlineData("--MANAGEMENT")]
    [InlineData("-M")]
    public void IsManagementMode_FlagPresent_ReturnsNoProblems(string flag)
    {
        ArgHelpers.IsManagementMode([flag]).Should().BeTrue();
    }

    [Fact]
    public void IsManagementMode_NoFlag_ReturnsProblems()
    {
        ArgHelpers.IsManagementMode(["-i", "abc"]).Should().BeFalse();
    }

    [Fact]
    public void StripModeSwitches_RemovesModeFlagsOnly()
    {
        var result = ArgHelpers.StripModeSwitches(["-m", "-i", "abc", "--management", "-k", "xyz"]);

        result.Should().Equal("-i", "abc", "-k", "xyz");
    }

    [Fact]
    public void GetSwitchMappings_ManagementMode_MapsIToManagementEnvironmentId()
    {
        var mappings = ArgHelpers.GetSwitchMappings(["-m"]);

        mappings["-i"].Should().Be("ManagementOptions:EnvironmentId");
        mappings["--environmentId"].Should().Be("ManagementOptions:EnvironmentId");
        mappings["-k"].Should().Be("ManagementOptions:ApiKey");
        mappings["--apiKey"].Should().Be("ManagementOptions:ApiKey");
    }

    [Fact]
    public void GetSwitchMappings_DeliveryMode_MapsIToDeliveryEnvironmentId()
    {
        var mappings = ArgHelpers.GetSwitchMappings([]);

        mappings["-i"].Should().Be("DeliveryOptions:EnvironmentId");
        mappings.Should().NotContainKey("-k");
        mappings.Should().NotContainKey("--apiKey");
    }

    public static TheoryData<string[]> ManagementSwitchArgs => new()
    {
        new[] { "-m" },
        new[] { "--management" },
        new[] { "-m", "--apiKey=xxx" },
    };

    [Theory]
    [MemberData(nameof(ManagementSwitchArgs))]
    public void FindInvalidArgs_ManagementSwitch_Accepted(string[] args)
    {
        ArgHelpers.FindInvalidArgs(args).Should().BeEmpty();
    }

    [Fact]
    public void FindInvalidArgs_ManagementOptionsLongForm_AcceptedInManagementMode()
    {
        ArgHelpers.FindInvalidArgs(["-m", "--ManagementOptions:EnvironmentId=abc"]).Should().BeEmpty();
        ArgHelpers.FindInvalidArgs(["-m", "--ManagementOptions:ApiKey=xyz"]).Should().BeEmpty();
    }

    // The section-qualified form binds without a switch mapping, so it survived the mode check that the
    // short flags go through: `-i <guid> --ManagementOptions:ApiKey=xyz` generated Delivery models, exit 0.
    [Theory]
    [InlineData("--ManagementOptions:ApiKey=xyz")]
    [InlineData("--ManagementOptions:EnvironmentId=abc")]
    public void FindInvalidArgs_ManagementOptionsLongForm_RejectedWithoutTheModeSwitch(string argument)
    {
        var result = ArgHelpers.FindInvalidArgs(["-i", "abc-123", argument]);

        result.Should().ContainSingle().Which.Should().Contain("--management");
    }

    [Fact]
    public void FindInvalidArgs_DeliveryOptionsLongForm_RejectedInManagementMode()
    {
        var result = ArgHelpers.FindInvalidArgs(["-m", "--DeliveryOptions:EnvironmentId=abc"]);

        result.Should().ContainSingle().Which.Should().Contain("Delivery API");
    }

    [Fact]
    public void FindInvalidArgs_Nullability_RejectedInManagementMode()
    {
        // Management models are uniformly nullable by contract, so there is nothing for this to select -
        // it used to be accepted and then quietly do nothing.
        var result = ArgHelpers.FindInvalidArgs(["-m", "--nullability=semantic"]);

        result.Should().ContainSingle().Which.Should().Contain("Delivery models only");
    }

    [Fact]
    public void ConfigurationBinding_ManagementMode_PopulatesManagementOptions()
    {
        var args = new[] { "-m", "-i", "abc-123", "-k", "secret" };
        var bindableArgs = ArgHelpers.StripModeSwitches(args);
        var configuration = new ConfigurationBuilder()
            .AddCommandLine(bindableArgs, ArgHelpers.GetSwitchMappings(args))
            .Build();

        var options = configuration.Get<CodeGeneratorOptions>()!;

        options.ManagementOptions.Should().NotBeNull();
        options.ManagementOptions.EnvironmentId.Should().Be("abc-123");
        options.ManagementOptions.ApiKey.Should().Be("secret");
        // -i in management mode should NOT also populate DeliveryOptions.
        options.DeliveryOptions?.EnvironmentId.Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData("-k")]
    [InlineData("--apiKey")]
    public void FindInvalidArgs_ManagementArgWithoutTheModeSwitch_PointsAtTheModeSwitch(string argument)
    {
        var result = ArgHelpers.FindInvalidArgs([argument, "secret"]);

        result.Should().ContainSingle().Which.Should().Contain("--management");
    }

    [Theory]
    [InlineData("-p")]
    [InlineData("--projectid")]
    public void FindInvalidArgs_DeliveryArgInManagementMode_SaysItIsNotUsedThere(string argument)
    {
        var result = ArgHelpers.FindInvalidArgs(["-m", argument, "abc-123"]);

        result.Should().ContainSingle().Which.Should().Contain("Delivery API");
    }

    public static TheoryData<string[]> ArgsBelongingToTheActiveMode => new()
    {
        new[] { "-m", "-k", "secret" },
        new[] { "-m", "-i", "abc-123" },
        new[] { "-i", "abc-123" },
        new[] { "-p", "abc-123" },
    };

    [Theory]
    [MemberData(nameof(ArgsBelongingToTheActiveMode))]
    public void FindInvalidArgs_ArgumentBelongingToTheActiveMode_ReturnsNoProblems(string[] args)
    {
        ArgHelpers.FindInvalidArgs(args).Should().BeEmpty();
    }
}
