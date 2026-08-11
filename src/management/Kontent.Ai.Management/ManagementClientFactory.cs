using Kontent.Ai.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Management;

internal sealed class ManagementClientFactory(IServiceProvider serviceProvider) : IManagementClientFactory
{
    public IManagementClient Get() => Get(NamedClients.Default);

    public IManagementClient Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Resolved with the nullable overload rather than catching: the client's own registration runs
        // inside resolution, and anything it throws is also an InvalidOperationException - a
        // configureHttpClient that rejected its input used to come back relabelled as a missing
        // registration, pointing at the wrong thing entirely.
        return serviceProvider.GetKeyedService<IManagementClient>(name)
            ?? throw new InvalidOperationException(
                $"No management client registered with name '{name}'. Ensure you've registered the client using AddManagementClient(\"{name}\", ...).");
    }
}
