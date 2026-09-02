// Touches one public entry point per package. Compiling proves the assemblies resolved from the
// nuspec and the API surface is reachable; running proves the DI wiring holds at runtime.
using Kontent.Ai.Delivery;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Management;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Sync;
using Microsoft.Extensions.DependencyInjection;

var environmentId = Guid.NewGuid().ToString();

var services = new ServiceCollection();
services.AddDeliveryClient(new DeliveryOptions { EnvironmentId = environmentId });
services.AddManagementClient(management => management.Options.Configure(options =>
{
    options.EnvironmentId = environmentId;
    options.ApiKey = "smoke";
}));
services.AddSyncClient(new SyncOptions { EnvironmentId = environmentId, ApiKey = "smoke" });

var provider = services.BuildServiceProvider();

_ = provider.GetRequiredService<IDeliveryClient>();
_ = provider.GetRequiredService<IManagementClient>();
_ = provider.GetRequiredService<ISyncClient>();

Console.WriteLine("library smoke: all three clients resolved");
