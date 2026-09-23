using Kontent.Ai.ModelGenerator.CommandLine;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Kontent.Ai.ModelGenerator.Tests.CodeSamples;

/// <summary>
/// Source of https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/main/net/strongly-typed-models/strongly_typed_models_generators.sh.
/// The command is published from the marked section of the .sh file next to this one; this test runs its
/// arguments through the checks the tool itself runs at startup.
/// </summary>
public class GeneratorCommand
{
    private static readonly string[] Command = File
        .ReadAllLines(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "CodeSamples", "strongly_typed_models_generators.sh"))
        .Where(line => !line.StartsWith('#') && line.Length > 0)
        .ToArray();

    [Fact]
    public void Sample_InvokesTheToolByItsCommandName()
    {
        var command = Command[^1].Split(' ')[0];

        command.Should().Be(typeof(ArgHelpers).Assembly.GetName().Name);
    }

    [Fact]
    public void Sample_ArgumentsPassTheToolsValidation()
    {
        var args = Command[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1..];

        ArgHelpers.FindInvalidArgs(args).Should().BeEmpty();

        var options = new ConfigurationBuilder()
            .AddCommandLine(ArgHelpers.StripModeSwitches(args), ArgHelpers.GetSwitchMappings(args))
            .Build()
            .Get<CodeGeneratorOptions>();

        options.Should().NotBeNull();
        options.Invoking(o => o.Validate()).Should().NotThrow();
    }
}
