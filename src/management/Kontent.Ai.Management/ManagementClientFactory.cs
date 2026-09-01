using Kontent.Ai.Common;

namespace Kontent.Ai.Management;

internal sealed class ManagementClientFactory(IServiceProvider serviceProvider) : IManagementClientFactory
{
    public IManagementClient Get() => Get(NamedClients.Default);

    public IManagementClient Get(string name) =>
        KeyedClients.Resolve<IManagementClient>(serviceProvider, name, "management client", "AddManagementClient");
}
