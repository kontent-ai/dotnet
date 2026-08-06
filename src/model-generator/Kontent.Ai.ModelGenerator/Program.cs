using Kontent.Ai.Delivery;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.ModelGenerator.Core;
using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Contract;
using Kontent.Ai.ModelGenerator.Core.Services;
using Kontent.Ai.ModelGenerator.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.ModelGenerator;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var invalidArgs = ArgHelpers.FindInvalidArgs(args);
            if (invalidArgs.Count > 0)
            {
                foreach (var problem in invalidArgs)
                {
                    await WriteErrorMessageAsync(problem);
                }

                await WriteErrorMessageAsync("Failed to run due to invalid configuration.");
                return 1;
            }

            var managementMode = ArgHelpers.IsManagementMode(args);
            // Mode-switch flags don't bind to a config property — strip them before they reach
            // Microsoft.Extensions.Configuration.AddCommandLine, which would otherwise complain.
            var bindableArgs = ArgHelpers.StripModeSwitches(args);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appSettings.json", true)
                .AddCommandLine(bindableArgs, ArgHelpers.GetSwitchMappings(args))
                .Build();

            var services = new ServiceCollection();
            services.Configure<CodeGeneratorOptions>(configuration);

            var generatorServiceType = managementMode
                ? ConfigureManagementMode(services, configuration)
                : ConfigureDeliveryMode(services, configuration);

            var serviceProvider = services.BuildServiceProvider();

            var options = serviceProvider.GetRequiredService<IOptions<CodeGeneratorOptions>>().Value;
            if (managementMode)
            {
                options.ValidateManagement();
            }
            else
            {
                options.Validate();
            }

            PrintSdkVersion(managementMode);

            return await ((CodeGeneratorBase)serviceProvider.GetRequiredService(generatorServiceType)).RunAsync();
        }
        catch (AggregateException aex)
        {
            if (aex.InnerExceptions.Count == 1 && aex.InnerException != null)
            {
                await WriteErrorMessageAsync(aex.InnerException.Message);
            }
            return 1;
        }
        catch (Exception ex)
        {
            await WriteErrorMessageAsync(ex.Message);
            return 1;
        }
    }

    private static Type ConfigureDeliveryMode(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDeliveryClient(configuration);
        services.AddTransient<IOutputProvider, FileSystemOutputProvider>();
        services.AddSingleton<IUserMessageLogger, UserMessageLogger>();
        services.AddSingleton<IClassCodeGeneratorFactory, ClassCodeGeneratorFactory>();
        services.AddSingleton<IClassDefinitionFactory, ClassDefinitionFactory>();
        services.AddSingleton<IDeliveryElementService, DeliveryElementService>();
        services.AddSingleton<DeliveryCodeGenerator>();
        return typeof(DeliveryCodeGenerator);
    }

    private static Type ConfigureManagementMode(IServiceCollection services, IConfiguration configuration)
    {
        // Binds the "ManagementOptions" configuration section (the CLI maps --environmentid / --apikey
        // onto ManagementOptions:EnvironmentId / ManagementOptions:ApiKey) and registers IManagementClient,
        // mirroring AddDeliveryClient. The container owns the client's HttpClient lifetime.
        services.AddManagementClient(configuration);

        services.AddTransient<IOutputProvider, FileSystemOutputProvider>();
        services.AddSingleton<IUserMessageLogger, UserMessageLogger>();
        services.AddSingleton<IClassCodeGeneratorFactory, ClassCodeGeneratorFactory>();
        services.AddSingleton<IClassDefinitionFactory, ClassDefinitionFactory>();
        services.AddSingleton<IManagementElementService, ManagementElementService>();
        services.AddSingleton<ManagementCodeGenerator>();
        return typeof(ManagementCodeGenerator);
    }

    private static readonly UserMessageLogger Messages = new();

    private static Task WriteErrorMessageAsync(string message) => Messages.LogErrorAsync(message);

    private static void PrintSdkVersion(bool managementMode)
    {
        var usedSdkInfo = ArgHelpers.GetUsedSdkInfo(managementMode);
        Messages.LogInfo($"Models were generated for {usedSdkInfo.Name} version {usedSdkInfo.Version}");
    }
}
