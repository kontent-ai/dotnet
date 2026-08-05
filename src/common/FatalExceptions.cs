// Shared source, compiled into each SDK assembly - see src/common/README.md.

namespace Kontent.Ai.Common;

/// <summary>
/// Failures a catch filter must let through rather than fold into an error result: they say the process
/// is no longer sound, so continuing would report a misleading error for a much larger problem.
/// </summary>
/// <remarks>
/// Not used by the Management SDK, whose error mapping catches <c>JsonException</c> specifically.
/// </remarks>
internal static class FatalExceptions
{
    internal static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
