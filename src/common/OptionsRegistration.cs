// Shared source, compiled into each SDK assembly - see src/common/README.md.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Common;

/// <summary>
/// Registers a client's options the way every SDK does: under the client's name, validated at startup,
/// and unnamed as well for the default client so <c>IOptions&lt;TOptions&gt;</c> resolves without one.
/// </summary>
internal static class OptionsRegistration
{
    internal static void RegisterValidated<TOptions>(
        IServiceCollection services,
        string name,
        Action<OptionsBuilder<TOptions>> configure)
        where TOptions : class
    {
        Register(services.AddOptions<TOptions>(name));

        if (name == NamedClients.Default)
        {
            Register(services.AddOptions<TOptions>());
        }

        void Register(OptionsBuilder<TOptions> builder)
        {
            configure(builder);
            builder.ValidateDataAnnotations().ValidateOnStart();
        }
    }
}
