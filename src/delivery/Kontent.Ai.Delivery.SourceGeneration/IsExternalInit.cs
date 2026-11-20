namespace System.Runtime.CompilerServices;

// netstandard2.0 lacks the marker type that init-only setters, and so positional records, compile against.
#pragma warning disable S2094 // The compiler looks the type up by name; it has no members to write.
internal static class IsExternalInit;
#pragma warning restore S2094
