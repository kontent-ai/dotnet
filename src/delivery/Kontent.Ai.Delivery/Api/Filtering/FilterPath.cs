namespace Kontent.Ai.Delivery.Api.Filtering;

internal static class FilterPath
{
    internal static string System(string propertyName) => Build("system", propertyName);
    internal static string Element(string elementCodename) => Build("elements", elementCodename);

    /// <summary>
    /// Normalizes a caller-supplied codename or system property into a filter key.
    /// </summary>
    /// <remarks>
    /// Lower-cased deliberately. The Delivery API is case-insensitive here — <c>System.Codename[EQ]</c>
    /// and <c>system.codename[eq]</c> return the same items — but the SDK is not: cache keys hash the
    /// filter key verbatim, so two spellings of one query would occupy two cache entries, double the
    /// origin calls, and invalidate independently. Kontent.ai codenames are lower-case by construction,
    /// so this can only ever alter input that was already wrong.
    ///
    /// This is a READ-side normalization and must not be copied to the Management SDK, where a
    /// user-supplied value in a write payload has to reach the API exactly as given.
    /// </remarks>
    private static string Build(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Property name cannot be null or whitespace.", nameof(name));
        }

        var trimmed = name.Trim().ToLowerInvariant();

        if (trimmed.Contains(' '))
        {
            throw new ArgumentException($"Property name '{name}' contains spaces.", nameof(name));
        }

        var expectedPrefix = prefix + ".";
        if (trimmed.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        // Avoid accepting dotted input with the wrong prefix (e.g. System("elements.title")).
        return trimmed.Contains('.', StringComparison.Ordinal)
            ? throw new ArgumentException(
                $"Property name '{name}' must be provided without a prefix. Use '{prefix}.' prefix only.",
                nameof(name))
            : $"{prefix}.{trimmed}";
    }
}
