// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Common;

/// <summary>
/// Resolves the keyed client registrations the SDKs' named-client factories are built on.
/// </summary>
internal static class KeyedClients
{
    /// <summary>
    /// Resolves the client registered under <paramref name="name"/>.
    /// </summary>
    /// <remarks>
    /// Not <c>GetRequiredKeyedService</c>: the client's own registration runs inside resolution, and
    /// anything it throws is an <see cref="InvalidOperationException"/> too - so a factory that let the
    /// container raise the missing-registration error would report a caller's failing configuration hook
    /// as a missing client. Resolving nullably keeps the two apart, and names the call that fixes it.
    /// </remarks>
    /// <param name="serviceProvider">The provider holding the keyed registrations.</param>
    /// <param name="name">The client name.</param>
    /// <param name="clientDescription">What to call the client in the error, e.g. <c>"sync client"</c>.</param>
    /// <param name="registrationMethod">The registration method to point at, e.g. <c>"AddSyncClient"</c>.</param>
    internal static TClient Resolve<TClient>(
        IServiceProvider serviceProvider,
        string name,
        string clientDescription,
        string registrationMethod)
        where TClient : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return serviceProvider.GetKeyedService<TClient>(name)
            ?? throw new InvalidOperationException(
                $"No {clientDescription} registered with name '{name}'. " +
                $"Ensure you've registered the client using {registrationMethod}(\"{name}\", ...).");
    }
}
