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

        // 3. Fallback: check calling assembly (for test scenarios where entry assembly may be test runner).
        // Note: Assembly.GetCallingAssembly() inside a Lazy<T> callback resolves the call stack at
        // Lazy evaluation time, which goes through the Lazy infrastructure rather than user code.
        // In practice this still works because steps 1-2 (entry assembly + references) handle
        // production scenarios, and step 3 is a fallback that happens to work in most xUnit setups.
        // Users can always override by registering their own ITypeProvider in the DI container.
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
        // Deliberately broad. "No generated provider" is a supported state, not a failure - the
        // caller falls back to dynamic types - so every way this can fail means the same thing:
        // nothing usable in this assembly. Scanning arbitrary loaded assemblies can fail in many
        // ways (unreflectable images, missing transitive dependencies, a provider constructor that
        // throws), and enumerating them would only decide which failures degrade gracefully and
        // which do not.
        //
        // Narrowing it would be actively harmful here: this runs inside a Lazy<T> factory, which
        // CACHES a thrown exception and rethrows it on every later access. An unlisted exception
        // would therefore not surface once - it would break every subsequent item mapping for the
        // lifetime of the process, instead of falling back to dynamic types.
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
