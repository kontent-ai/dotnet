using System.Reflection;

namespace Kontent.Ai.Delivery.ContentItems;

/// <summary>
/// Default type provider that attempts to discover a source-generated type provider,
/// falling back to returning null for all mappings (enabling dynamic types).
/// </summary>
/// <remarks>
/// <para>
/// This class performs deterministic discovery of generated type providers without
/// scanning the entire AppDomain. Discovery order:
/// </para>
/// <list type="number">
///   <item>Check entry assembly for <c>Kontent.Ai.Delivery.Generated.GeneratedTypeProvider</c></item>
///   <item>Check assemblies referenced by entry assembly (bounded set)</item>
///   <item>Check calling assembly and its references (for test scenarios)</item>
/// </list>
/// <para>
/// If no generated type provider is found, returns null for all mappings, instructing
/// <see cref="DefaultItemTypingStrategy"/> to use dynamic types.
/// </para>
/// <para>
/// Users can override this behavior by registering their own <see cref="ITypeProvider"/>
/// in the DI container.
/// </para>
/// </remarks>
internal sealed class TypeProvider : ITypeProvider
{
    private const string GeneratedTypeProviderName = "Kontent.Ai.Delivery.Generated.GeneratedTypeProvider";

    private static readonly Lazy<ITypeProvider?> _discoveredProvider = new(DiscoverGeneratedProvider);

    public Type? GetType(string contentType)
        => _discoveredProvider.Value?.GetType(contentType);

    public string? GetCodename(Type contentType)
        => _discoveredProvider.Value?.GetCodename(contentType);

    private static ITypeProvider? DiscoverGeneratedProvider()
    {
        // 1. Check entry assembly first
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is not null)
        {
            var provider = TryCreateProviderFromAssembly(entryAssembly);
            if (provider is not null)
                return provider;

            // 2. Check referenced assemblies (bounded set)
            var referencedAssemblies = GetReferencedAssemblies(entryAssembly);
            foreach (var assembly in referencedAssemblies)
            {
                provider = TryCreateProviderFromAssembly(assembly);
                if (provider is not null)
                    return provider;
            }
        }

        // 3. Fallback: the calling assembly, for tests where the entry assembly is the test runner.
        // GetCallingAssembly inside a Lazy<T> callback resolves through the Lazy infrastructure
        // rather than user code, so this is best-effort; steps 1-2 cover production, and
        // registering an ITypeProvider in DI overrides all of it.
        var callingAssembly = Assembly.GetCallingAssembly();
        if (callingAssembly is not null && callingAssembly != entryAssembly)
        {
            var provider = TryCreateProviderFromAssembly(callingAssembly);
            if (provider is not null)
                return provider;

            var referencedAssemblies = GetReferencedAssemblies(callingAssembly);
            foreach (var assembly in referencedAssemblies)
            {
                provider = TryCreateProviderFromAssembly(assembly);
                if (provider is not null)
                    return provider;
            }
        }

        return null;
    }

    private static ITypeProvider? TryCreateProviderFromAssembly(Assembly assembly)
    {
        try
        {
            var providerType = assembly.GetType(GeneratedTypeProviderName);
            if (providerType is not null && typeof(ITypeProvider).IsAssignableFrom(providerType))
            {
                return (ITypeProvider?)Activator.CreateInstance(providerType);
            }
        }
        // Deliberately broad: "no generated provider" is a supported state, so every way this can
        // fail means the same thing - nothing usable here. Narrowing would be harmful, because a
        // Lazy<T> factory CACHES a thrown exception and rethrows it on every later access, breaking
        // every subsequent item mapping instead of falling back to dynamic types.
        catch
        {
            return null;
        }

        return null;
    }

    private static IEnumerable<Assembly> GetReferencedAssemblies(Assembly assembly)
    {
        return assembly.GetReferencedAssemblies()
            .DistinctBy(r => r.FullName)
            .Select(TryLoadAssembly)
            .Where(a => a is not null)!;
    }

    private static Assembly? TryLoadAssembly(AssemblyName reference)
    {
        try
        {
            return Assembly.Load(reference);
        }
        // Broad for the same reason as TryCreateProviderFromAssembly: an assembly that will not
        // load means "nothing to scan here", and this feeds the same Lazy<T> factory.
        catch
        {
            return null;
        }
    }
}
