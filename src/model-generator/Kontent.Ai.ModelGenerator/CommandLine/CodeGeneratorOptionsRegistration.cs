using Kontent.Ai.ModelGenerator.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.ModelGenerator.CommandLine;

internal static class CodeGeneratorOptionsRegistration
{
    private const string LegacyBaseRecordKey = "BaseClass";

    public static IServiceCollection AddCodeGeneratorOptions(this IServiceCollection services, IConfigurationRoot configuration)
    {
        // The binder sets the obsolete BaseClass alias after BaseRecord, so without this a BaseClass key in
        // appSettings.json would override -b on the command line.
        var baseRecord = ResolveBaseRecord(configuration);

        return services
            .Configure<CodeGeneratorOptions>(configuration)
            .PostConfigure<CodeGeneratorOptions>(options => options.BaseRecord = baseRecord);
    }

    // The last source that sets either key decides, so an empty -b clears a base record set in the file.
    private static string? ResolveBaseRecord(IConfigurationRoot configuration) =>
        configuration.Providers
            .Reverse()
            .Select(provider => new[] { ValueOf(provider, nameof(CodeGeneratorOptions.BaseRecord)), ValueOf(provider, LegacyBaseRecordKey) })
            .FirstOrDefault(values => values.Any(value => value is not null))
            ?.FirstOrDefault(value => !string.IsNullOrEmpty(value));

    private static string? ValueOf(IConfigurationProvider provider, string key) =>
        provider.TryGet(key, out var value) ? value ?? "" : null;
}
