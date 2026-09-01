using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Contract;
using Kontent.Ai.ModelGenerator.Core.Generators.Class;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.ModelGenerator.Core;

public abstract class CodeGeneratorBase(
    IOptions<CodeGeneratorOptions> options,
    IOutputProvider outputProvider,
    IClassCodeGeneratorFactory classCodeGeneratorFactory,
    IUserMessageLogger logger)
{
    protected readonly IUserMessageLogger Logger = logger;
    protected readonly IClassCodeGeneratorFactory ClassCodeGeneratorFactory = classCodeGeneratorFactory;
    protected readonly CodeGeneratorOptions Options = options.Value;
    protected readonly IOutputProvider OutputProvider = outputProvider;

    private string NoContentTypeAvailableMessage =>
        $@"No content type available for the environment ({Options.GetEnvironmentId()}). Check the environment and project settings at https://app.kontent.ai/.";

    public async Task<int> RunAsync()
    {
        // Fetched once and reused. Generating the base record from a second fetch would repeat every
        // request the content model needs, for a result that has to match the first one anyway.
        var classCodeGenerators = await GetClassCodeGenerators();

        if (classCodeGenerators.Count == 0)
        {
            Logger.LogInfo(NoContentTypeAvailableMessage);
            return 0;
        }

        WriteToOutputProvider(classCodeGenerators);

        if (!string.IsNullOrEmpty(Options.BaseRecord))
        {
            GenerateBaseClass(classCodeGenerators);
        }

        return 0;
    }

    protected void WriteToOutputProvider(string content, string fileName, bool overwriteExisting)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        Logger.LogInfo(OutputProvider.Output(content, fileName, overwriteExisting)
            ? $"{fileName} class was successfully created."
            : $"{fileName} already exists and was kept. Delete it to have it regenerated.");
    }

    protected void WriteToOutputProvider(ICollection<ClassCodeGenerator> classCodeGenerators)
    {
        // Content type codenames that differ can still sanitize to one class name, and so to one file.
        // Writing both leaves whichever ran last and reports success for both.
        var claimedFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        var written = 0;

        foreach (var codeGenerator in classCodeGenerators)
        {
            var codename = codeGenerator.ClassDefinition.Codename;
            if (claimedFiles.TryGetValue(codeGenerator.ClassFilename, out var owner))
            {
                Logger.LogWarning(
                    $"Skipping content type '{codename}': it generates the same file as '{owner}' " +
                    $"('{codeGenerator.ClassFilename}.cs'). Rename one of them in Kontent.ai.");
                continue;
            }

            claimedFiles[codeGenerator.ClassFilename] = codename;
            // Content types always overwrite, so what is handed to the provider is what lands.
            // Generated type files are always rewritten; the base record is the only output that is
            // kept if it already exists, and it is written separately.
            OutputProvider.Output(codeGenerator.GenerateCode(), codeGenerator.ClassFilename, overwriteExisting: true);
            written++;
        }

        Logger.LogInfo($"{written} content type models were successfully created.");
    }

    protected abstract Task<ICollection<ClassCodeGenerator>> GetClassCodeGenerators();

    protected void WriteConsoleErrorMessage(Exception exception, string? elementCodename, string elementType, string className)
    {
        switch (exception)
        {
            // The identifier clash carries the detail worth acting on - which two codenames collided and
            // on what - so it is passed through rather than restated.
            case InvalidOperationException:
                Logger.LogWarning($"Skipping element '{elementCodename}'. {exception.Message}");
                break;
            case InvalidIdentifierException:
                Logger.LogWarning($"Can't create valid C# Identifier from '{elementCodename}'. Skipping element.");
                break;
            case ArgumentNullException or ArgumentException:
                Logger.LogWarning($"Skipping unknown Content Element type '{elementType}'. (Content Type: '{className}', Element Codename: '{elementCodename}').");
                break;
            // The caller catches everything so one bad element cannot end the run; without this arm the
            // element vanished from the output with nothing said about it.
            default:
                Logger.LogWarning($"Skipping element '{elementCodename}' on Content Type '{className}': {exception.GetType().Name}: {exception.Message}");
                break;
        }
    }

    protected void WriteConsoleErrorMessage(string contentTypeCodename)
    {
        Logger.LogWarning($"Skipping Content Type '{contentTypeCodename}'. Can't create valid C# identifier from its name.");
    }

    private void GenerateBaseClass(ICollection<ClassCodeGenerator> classCodeGenerators)
    {
        var baseClassCodeGenerator = new BaseClassCodeGenerator(Options);

        foreach (var codeGenerator in classCodeGenerators)
        {
            baseClassCodeGenerator.AddClassNameToExtend(codeGenerator.ClassDefinition.ClassName);
        }

        var baseRecord = Options.BaseRecord;
        if (string.IsNullOrEmpty(baseRecord))
        {
            return;
        }

        var baseClassCode = baseClassCodeGenerator.GenerateBaseClassCode();
        WriteToOutputProvider(baseClassCode, baseRecord, false);

        var baseClassExtenderCode = baseClassCodeGenerator.GenerateExtenderCode();
        WriteToOutputProvider(baseClassExtenderCode, baseClassCodeGenerator.ExtenderClassName, true);
    }
}
