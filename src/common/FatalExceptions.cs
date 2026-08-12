// Shared source, compiled into each SDK assembly - see src/common/README.md.

namespace Kontent.Ai.Common;

/// <summary>
/// Failures a catch filter must let through rather than fold into an error result: they say the process
/// is no longer sound, so continuing would report a misleading error for a much larger problem.
/// </summary>
/// <remarks>
/// <see cref="StackOverflowException"/> is deliberately absent: it cannot be caught on .NET, so listing
/// it suggests a filter that never runs. The AppDomain-related failures are absent for the same reason -
/// this target has no unloadable AppDomains for them to come from.
/// <para>
/// Not used by the Management SDK, whose error mapping catches <c>JsonException</c> specifically.
/// </para>
/// </remarks>
internal static class FatalExceptions
{
    internal static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException
            or AccessViolationException
            or BadImageFormatException
            or InvalidProgramException;
}
