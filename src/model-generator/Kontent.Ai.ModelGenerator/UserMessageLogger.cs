using Kontent.Ai.ModelGenerator.Core.Contract;

namespace Kontent.Ai.ModelGenerator;

/// <summary>
/// Writes the generator's user-facing output.
/// </summary>
/// <remarks>
/// The destinations are supplied rather than reached for, so a caller - a test, or anything hosting the
/// generator - can capture the output without reassigning <see cref="Console"/>, which is process-wide
/// and cannot be done safely by more than one test at a time.
/// </remarks>
/// <param name="output">Where informational and warning messages go. Defaults to standard output.</param>
/// <param name="error">Where errors go. Defaults to standard error.</param>
public class UserMessageLogger(TextWriter? output = null, TextWriter? error = null) : IUserMessageLogger
{
    private TextWriter Output => output ?? Console.Out;
    private TextWriter Error => error ?? Console.Error;

    /// <inheritdoc/>
    public void LogInfo(string message) => Log(message);

    /// <inheritdoc/>
    public void LogWarning(string message) => Log(message, "Warning: ");

    /// <inheritdoc/>
    public async Task LogErrorAsync(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            await Error.WriteLineAsync(message);
        }
    }

    private void Log(string message, string messagePrefix = "")
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Output.WriteLine($"{messagePrefix}{message}");
        }
    }
}
