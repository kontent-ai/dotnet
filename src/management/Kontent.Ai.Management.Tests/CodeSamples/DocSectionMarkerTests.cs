using System.Text.RegularExpressions;
using AwesomeAssertions;

namespace Kontent.Ai.Management.Tests.CodeSamples;

/// <summary>
/// The samples in this folder are the source for
/// https://github.com/Kontent-ai-Learn/kontent-ai-learn-code-samples/tree/master/net, where each marked
/// section is published as the file named by its id. Two properties have to hold for that to work, and
/// neither shows up as a failing test anywhere else: a section that is never closed runs on into the next
/// method's scaffolding, and a duplicated id has no single file it belongs to.
/// </summary>
public partial class DocSectionMarkerTests
{
    [GeneratedRegex(@"^\s*//\s*DocSection:\s*(\S+)\s*$")]
    private static partial Regex OpenMarker();

    [GeneratedRegex(@"^\s*//\s*EndDocSection\s*$")]
    private static partial Regex CloseMarker();

    private static IEnumerable<string> SampleFiles() =>
        Directory.EnumerateFiles(
            Path.Combine(Environment.CurrentDirectory, "..", "..", "..", "CodeSamples"), "*.cs");

    [Fact]
    public void EverySection_IsClosed()
    {
        var unclosed = new List<string>();

        foreach (var file in SampleFiles())
        {
            string? open = null;
            foreach (var line in File.ReadLines(file))
            {
                var opened = OpenMarker().Match(line);
                if (opened.Success)
                {
                    if (open is not null)
                    {
                        unclosed.Add($"{Path.GetFileName(file)}: {open}");
                    }

                    open = opened.Groups[1].Value;
                }
                else if (CloseMarker().IsMatch(line))
                {
                    open = null;
                }
            }

            if (open is not null)
            {
                unclosed.Add($"{Path.GetFileName(file)}: {open}");
            }
        }

        unclosed.Should().BeEmpty("an unclosed section runs to the next marker and publishes the test scaffolding between them");
    }

    [Fact]
    public void EveryId_IsUsedOnce()
    {
        var ids = SampleFiles()
            .SelectMany(File.ReadLines)
            .Select(line => OpenMarker().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value);

        var duplicates = ids
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        duplicates.Should().BeEmpty("the id names the published file, so two sections cannot share one");
    }

    [Fact]
    public void NoSection_ContainsTestScaffolding()
    {
        var leaks = new List<string>();

        foreach (var file in SampleFiles())
        {
            string? open = null;
            foreach (var line in File.ReadLines(file))
            {
                var opened = OpenMarker().Match(line);
                if (opened.Success)
                {
                    open = opened.Groups[1].Value;
                    continue;
                }

                if (CloseMarker().IsMatch(line))
                {
                    open = null;
                    continue;
                }

                if (open is null) continue;

                if (line.Contains("MockClientFactory") ||
                    line.Contains("[Fact]") ||
                    line.Contains("Record.ExceptionAsync") ||
                    line.TrimStart().StartsWith("Assert.", StringComparison.Ordinal))
                {
                    leaks.Add($"{Path.GetFileName(file)}: {open} -> {line.Trim()}");
                }
            }
        }

        leaks.Should().BeEmpty("whatever sits between the markers is published verbatim as the sample");
    }
}
