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

    private static string? ResolveBaseRecord(IConfigurationRoot configuration) =>
        configuration.Providers
            .Reverse()
            .Select(provider => ValueOf(provider, nameof(CodeGeneratorOptions.BaseRecord)) ?? ValueOf(provider, LegacyBaseRecordKey))
            .FirstOrDefault(value => value is not null);

    private static string? ValueOf(IConfigurationProvider provider, string key) =>
        provider.TryGet(key, out var value) && !string.IsNullOrEmpty(value) ? value : null;
}
