// Shared source, compiled into each SDK assembly - see src/common/README.md.

namespace Kontent.Ai.Common;

/// <summary>
/// Rules every SDK applies to the name of a registered client.
/// </summary>
internal static class NamedClients
{
    /// <summary>
    /// The name a client registered without one is filed under, so the unnamed and named registration paths
    /// resolve the same service.
    /// </summary>
    internal const string Default = "Default";

    /// <summary>
    /// A client name becomes a DI service key and part of an HTTP client name, and both are matched by
    /// ordinal string equality. Surrounding or embedded whitespace is easy to introduce and invisible at
    /// the point of failure, so it is rejected at registration instead.
    /// </summary>
    internal static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (name.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Client name cannot contain whitespace. Use underscores or hyphens instead.",
                nameof(name));
        }
    }
}
