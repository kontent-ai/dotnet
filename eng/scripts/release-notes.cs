// Prints the GitHub Release notes for a tag, taken from that product's CHANGELOG.md.
//
//   dotnet run eng/scripts/release-notes.cs -- management-v9.0.0-beta-5
//   dotnet run eng/scripts/release-notes.cs -- delivery-v19.5.0 > notes.md
//
// The changelog is the source of truth; this just reshapes one entry for the release page:
//
//   * drops the "## <version> (<date>)" heading - the release page already shows title and tag
//   * promotes the entry's own "###" sections to "##" so they read as top level
//   * appends an install snippet, with --prerelease when the version is a prerelease
//
// Exits non-zero if the tag does not parse, the product is unknown, or the changelog has no
// entry for that version - the same conditions release-plan.cs refuses to publish on.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: release-notes <product>-v<version>");
    return 1;
}

var tag = args[0];
var repoRoot = FindRepoRoot();
if (repoRoot is null) return Fail("not inside a git repository");

var m = Regex.Match(tag, @"^(?<product>.+)-v(?<version>.+)$");
if (!m.Success) return Fail($"tag '{tag}' is not in <product>-v<version> form");

var product = m.Groups["product"].Value;
var version = m.Groups["version"].Value;

using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "eng", "products.json")));
if (!doc.RootElement.TryGetProperty(product, out var entry))
    return Fail($"unknown product '{product}'");

var changelogRel = entry.GetProperty("changelog").GetString()!;
var changelog = File.ReadAllText(Path.Combine(repoRoot, changelogRel));

// Capture from this version's heading up to the next "## " heading (or end of file).
var section = Regex.Match(
    changelog,
    $@"^##\s+{Regex.Escape(version)}(?:\s[^\n]*)?\n(?<body>.*?)(?=^##\s|\z)",
    RegexOptions.Multiline | RegexOptions.Singleline);

if (!section.Success)
    return Fail($"{changelogRel} has no '## {version}' heading");

var body = section.Groups["body"].Value.Trim();
if (body.Length == 0)
    return Fail($"the '## {version}' entry in {changelogRel} is empty");

// Promote the entry's own headings one level: "### Fixes" reads better as "## Fixes" on a
// release page. Fenced code blocks are skipped so shell comments are not rewritten.
var output = new StringBuilder();
var inFence = false;
foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
{
    if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
    output.AppendLine(!inFence && line.StartsWith("###", StringComparison.Ordinal) ? line[1..] : line);
}

var primaryPackage = entry.GetProperty("expectedPackages").EnumerateArray().First().GetString()!;
var prereleaseFlag = version.Contains('-') ? " --prerelease" : "";

output.AppendLine();
output.AppendLine("## Installation");
output.AppendLine();
output.AppendLine("```bash");
output.AppendLine($"dotnet add package {primaryPackage}{prereleaseFlag}");
output.AppendLine("```");
output.AppendLine();
output.AppendLine($"**Full changelog**: [`{changelogRel}`](https://github.com/kontent-ai/dotnet/blob/main/{changelogRel})");

Console.Write(output.ToString());
return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"release-notes: {message}");
    return 1;
}

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}
