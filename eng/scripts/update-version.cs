// Bumps a product's version in eng/Versions.props and promotes its changelog's
// "## Unreleased" section to the new version.
//
//   dotnet run eng/scripts/update-version.cs -- <product> <major|minor|patch|x.y.z>
//
// Examples:
//   dotnet run eng/scripts/update-version.cs -- aspnetcore minor
//   dotnet run eng/scripts/update-version.cs -- aspnetcore 1.0.0-preview.1
//
// Leaves everything staged for review; commits nothing. A file-based app (.NET 10)
// so there is no project to maintain and no extra toolchain to install.

using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: update-version <product> <major|minor|patch|explicit-version>");
    return 1;
}

var (product, bump) = (args[0], args[1]);

var repoRoot = FindRepoRoot();
if (repoRoot is null) { Console.Error.WriteLine("not inside a git repository"); return 1; }

var productsPath = Path.Combine(repoRoot, "eng", "products.json");
var versionsPath = Path.Combine(repoRoot, "eng", "Versions.props");

using var doc = JsonDocument.Parse(File.ReadAllText(productsPath));
if (!doc.RootElement.TryGetProperty(product, out var entry))
{
    Console.Error.WriteLine($"unknown product '{product}'. Known: {string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name))}");
    return 1;
}

var versionProperty = entry.GetProperty("versionProperty").GetString()!;
var changelogPath = Path.Combine(repoRoot, entry.GetProperty("changelog").GetString()!);

// --- read the current version -------------------------------------------------
var versionsXml = File.ReadAllText(versionsPath);
var propRegex = new Regex($@"(<{versionProperty}>)([^<]*)(</{versionProperty}>)");
var match = propRegex.Match(versionsXml);
if (!match.Success) { Console.Error.WriteLine($"{versionProperty} not found in eng/Versions.props"); return 1; }

var current = match.Groups[2].Value;
var next = NextVersion(current, bump);
if (next is null) { Console.Error.WriteLine($"cannot apply '{bump}' to '{current}'"); return 1; }

// --- rewrite eng/Versions.props ----------------------------------------------
// Braces are required: a bare "$1" followed by a digit would be read as group $10.
File.WriteAllText(versionsPath, propRegex.Replace(versionsXml, $"${{1}}{next}${{3}}", 1));

// --- promote the changelog ----------------------------------------------------
var changelog = File.ReadAllText(changelogPath);
if (!changelog.Contains("## Unreleased"))
{
    Console.Error.WriteLine($"no '## Unreleased' heading in {changelogPath}");
    return 1;
}

var body = Regex.Match(changelog, @"## Unreleased\s*\n(.*?)(?=\n## |\z)", RegexOptions.Singleline).Groups[1].Value.Trim();
if (body.Length == 0)
    Console.Error.WriteLine("warning: '## Unreleased' is empty - releasing with no changelog entries");

var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
changelog = changelog.Replace("## Unreleased", $"## Unreleased\n\n## {next} ({today})", StringComparison.Ordinal);
File.WriteAllText(changelogPath, changelog);

Console.WriteLine($"{product}: {current} -> {next}");
Console.WriteLine($"  eng/Versions.props   {versionProperty} updated");
Console.WriteLine($"  {entry.GetProperty("changelog").GetString()}  '## Unreleased' promoted to '## {next} ({today})'");
Console.WriteLine();
Console.WriteLine($"Review, commit, then tag: {product}-v{next}");
return 0;

static string? NextVersion(string current, string bump)
{
    if (bump is not ("major" or "minor" or "patch")) return bump; // explicit version

    var core = current.Split('-')[0];               // drop any prerelease suffix
    var parts = core.Split('.');
    if (parts.Length != 3 || !parts.All(p => int.TryParse(p, out _))) return null;

    var (ma, mi, pa) = (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
    return bump switch
    {
        "major" => $"{ma + 1}.0.0",
        "minor" => $"{ma}.{mi + 1}.0",
        _       => $"{ma}.{mi}.{pa + 1}",
    };
}

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}
