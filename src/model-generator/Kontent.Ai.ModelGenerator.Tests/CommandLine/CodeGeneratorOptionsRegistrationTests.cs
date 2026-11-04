using System.Text;
using Kontent.Ai.ModelGenerator.CommandLine;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.ModelGenerator.Tests.CommandLine;

public class CodeGeneratorOptionsRegistrationTests
{
    public static TheoryData<string, string[], string?> BaseRecordSources => new()
    {
        { """{ "BaseClass": null }""", ["-b", "CliBase"], "CliBase" },
        { """{ "BaseClass": "FileBase" }""", ["-b", "CliBase"], "CliBase" },
        { """{ "BaseClass": "FileBase" }""", ["--baseRecord", "CliBase"], "CliBase" },
        { """{ "BaseRecord": "FileBase" }""", ["--baseClass", "CliBase"], "CliBase" },
        { """{ "BaseRecord": "FileBase" }""", [], "FileBase" },
        { """{ "BaseClass": "FileBase" }""", [], "FileBase" },
        { """{ "BaseRecord": "NewBase", "BaseClass": "OldBase" }""", [], "NewBase" },
        { """{ "BaseRecord": null, "BaseClass": "OldBase" }""", [], "OldBase" },
        { """{ "BaseClass": "FileBase" }""", ["-r", "CliBase"], "CliBase" },
        { """{ "BaseClass": "FileBase" }""", ["-m", "-b", "CliBase"], "CliBase" },
        { """{ "BaseRecord": "FileBase" }""", ["-b", ""], null },
        { """{ "BaseClass": "FileBase" }""", ["--baseRecord", ""], null },
        { """{ "BaseRecord": "FileBase" }""", ["--baseClass", ""], null },
        { """{ "BaseRecord": null }""", [], null },
        { "{}", [], null },
    };

    [Theory]
    [MemberData(nameof(BaseRecordSources))]
    public void AddCodeGeneratorOptions_CommandLineOverridesAppSettingsWhicheverKeyEitherUses(
        string appSettings, string[] args, string? expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(appSettings)))
            .AddCommandLine(ArgHelpers.StripModeSwitches(args), ArgHelpers.GetSwitchMappings(args))
            .Build();

        var options = new ServiceCollection()
            .AddCodeGeneratorOptions(configuration)
            .BuildServiceProvider()
            .GetRequiredService<IOptions<CodeGeneratorOptions>>()
            .Value;

        options.BaseRecord.Should().Be(expected);
    }
}
