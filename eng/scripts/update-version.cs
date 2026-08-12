// Bumps a product's version in eng/Versions.props and promotes its changelog's
// "## Unreleased" section to the new version. Stages everything; commits nothing.
//
//   dotnet run eng/scripts/update-version.cs -- <product> <major|minor|patch|x.y.z>
//   dotnet run eng/scripts/update-version.cs -- aspnetcore 1.0.0-preview.1

using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: update-version <product> <major|minor|patch|prerelease|release|explicit-version>");
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

// `prerelease` continues a label that already exists; every other bump either drops the
// prerelease or picks a new label outright. See the warning helper for why that matters.
WarnIfPrereleaseWillMisSort(next, bump == "prerelease" ? 9 : 1);

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
// Every prerelease entry in these changelogs carries this marker, so a reader scanning the file
// can tell at a glance which versions are not GA. Keep generated headings consistent with that.
var heading = $"## {next} ({today})" + (next.Contains('-') ? "  _(prerelease)_" : "");
changelog = changelog.Replace("## Unreleased", $"## Unreleased\n\n{heading}", StringComparison.Ordinal);
File.WriteAllText(changelogPath, changelog);

Console.WriteLine($"{product}: {current} -> {next}");
Console.WriteLine($"  eng/Versions.props   {versionProperty} updated");
Console.WriteLine($"  {entry.GetProperty("changelog").GetString()}  '## Unreleased' promoted to '{heading}'");
Console.WriteLine();
Console.WriteLine($"Review, commit, then tag: {product}-v{next}");
return 0;

static string? NextVersion(string current, string bump)
{
    var dash = current.IndexOf('-');
    var core = dash < 0 ? current : current[..dash];
    var pre  = dash < 0 ? null : current[(dash + 1)..];   // e.g. "beta-4", "preview.1", "rc1"

    switch (bump)
    {
        // 9.0.0-beta-4 -> 9.0.0-beta-5 ; 0.17.0-preview.1 -> 0.17.0-preview.2 ; 1.0.0-rc1 -> 1.0.0-rc2
        case "prerelease":
            if (pre is null)
            {
                Console.Error.WriteLine($"'{current}' is already stable. Use an explicit version to start a prerelease.");
                return null;
            }
            var m = Regex.Match(pre, @"^(?<prefix>.*?)(?<num>\d+)$");
            if (!m.Success)
            {
                Console.Error.WriteLine($"prerelease suffix '{pre}' has no trailing number to increment. Use an explicit version.");
                return null;
            }
            return $"{core}-{m.Groups["prefix"].Value}{int.Parse(m.Groups["num"].Value) + 1}";

        // 9.0.0-beta-4 -> 9.0.0  (promote the prerelease to its GA)
        case "release":
            if (pre is null)
            {
                Console.Error.WriteLine($"'{current}' is already stable - nothing to promote.");
                return null;
            }
            return core;

        case "major" or "minor" or "patch":
            // Refuse rather than silently skipping the GA: 9.0.0-beta-4 + patch would
            // otherwise produce 9.0.1, jumping straight past the 9.0.0 release.
            if (pre is not null)
            {
                Console.Error.WriteLine($"'{current}' is a prerelease. Use 'prerelease' ({core}-{pre} -> next), " +
                                        $"'release' (-> {core}), or an explicit version.");
                return null;
            }
            var parts = core.Split('.');
            if (parts.Length != 3 || !parts.All(x => int.TryParse(x, out _)))
            {
                Console.Error.WriteLine($"'{current}' is not major.minor.patch - use an explicit version.");
                return null;
            }
            var (ma, mi, pa) = (int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            return bump switch
            {
                "major" => $"{ma + 1}.0.0",
                "minor" => $"{ma}.{mi + 1}.0",
                _       => $"{ma}.{mi}.{pa + 1}",
            };

        default:
            // Explicit version. Nothing downstream checks its shape: release-plan.cs verifies
            // only that the tag matches this property and that the changelog has a matching
            // heading, so a typo survives as far as a tag and a failed pack - with a merged PR
            // carrying a bad version property and a bad changelog entry. Reject it here, where
            // it is still one field in a form.
            if (!Regex.IsMatch(bump, @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*)?$"))
            {
                Console.Error.WriteLine($"'{bump}' is not a valid version. Expected major.minor.patch with an " +
                                        "optional prerelease label - 9.0.0, 9.0.0-rc.1, 0.17.0-preview.2.");
                // Legal SemVer, but NuGet ignores build metadata when identifying a package, so it
                // would silently vanish somewhere between here and the pushed .nupkg.
                if (bump.Contains('+'))
                    Console.Error.WriteLine("Build metadata (+...) is not part of a NuGet package identity - drop it.");
                return null;
            }
            if (bump == current)
            {
                Console.Error.WriteLine($"'{bump}' is already the current version - nothing to bump.");
                return null;
            }
            // A version that sorts below the current one is almost always a typo, and nothing
            // downstream would catch it: the tag, the changelog heading and the pack all agree
            // with each other, so it publishes. On nuget.org it then sits below what is already
            // there and never becomes the version consumers resolve - a release that looks
            // successful everywhere except where it matters.
            if (CompareSemVer(bump, current) < 0)
            {
                Console.Error.WriteLine(
                    $"'{bump}' sorts below the current '{current}', so releasing it would leave " +
                    $"'{current}' as the newest version on nuget.org. If the current value is wrong " +
                    "and was never published, correct eng/Versions.props directly rather than bumping to it.");
                return null;
            }
            return bump;
    }
}

// SemVer 2.0 precedence. Build metadata takes no part, and is rejected before this is reached.
// The identifier-wise prerelease rule is the same one the warning below is about: `beta-5` is one
// alphanumeric identifier compared as text, so 9.0.0-beta-10 really does sort below 9.0.0-beta-5.
static int CompareSemVer(string a, string b)
{
    var (aCore, aPre) = SplitVersion(a);
    var (bCore, bPre) = SplitVersion(b);

    var aNumbers = aCore.Split('.');
    var bNumbers = bCore.Split('.');
    for (var i = 0; i < 3; i++)
    {
        var compared = long.Parse(aNumbers[i]).CompareTo(long.Parse(bNumbers[i]));
        if (compared != 0) return compared;
    }

    // A prerelease has lower precedence than the release it leads to: 9.0.0-rc.1 < 9.0.0.
    if (aPre is null && bPre is null) return 0;
    if (aPre is null) return 1;
    if (bPre is null) return -1;

    var aIds = aPre.Split('.');
    var bIds = bPre.Split('.');
    for (var i = 0; i < Math.Max(aIds.Length, bIds.Length); i++)
    {
        // Running out of identifiers first means lower precedence: rc.1 < rc.1.1.
        if (i >= aIds.Length) return -1;
        if (i >= bIds.Length) return 1;

        var aIsNumeric = long.TryParse(aIds[i], out var aNumber);
        var bIsNumeric = long.TryParse(bIds[i], out var bNumber);

        // Numeric identifiers always rank below alphanumeric ones.
        var compared = (aIsNumeric, bIsNumeric) switch
        {
            (true, true) => aNumber.CompareTo(bNumber),
            (true, false) => -1,
            (false, true) => 1,
            _ => string.CompareOrdinal(aIds[i], bIds[i]),
        };
        if (compared != 0) return compared;
    }

    return 0;

    static (string Core, string? Prerelease) SplitVersion(string version)
    {
        var dash = version.IndexOf('-');
        return dash < 0 ? (version, null) : (version[..dash], version[(dash + 1)..]);
    }
}

// `beta-5` is ONE alphanumeric SemVer identifier, compared as text - so 9.0.0-beta-10 sorts
// BEFORE 9.0.0-beta-5 and nuget.org stops offering the newer prerelease. `preview.1` splits into
// `preview` + `1` and compares numerically, so it is safe.
//
// warnFrom is 1 when a label is being chosen fresh - the one moment the shape is still free -
// and 9 when a sequence is being continued, where the advice is nearly useless. A warning rather
// than an error: the escape is a judgement call about the release, not about this command.
static void WarnIfPrereleaseWillMisSort(string version, int warnFrom)
{
    var dash = version.IndexOf('-');
    if (dash < 0) return;

    var last = version[(dash + 1)..].Split('.')[^1];
    var m = Regex.Match(last, @"^(?<prefix>.*[^\d])(?<num>\d+)$");
    if (!m.Success) return;

    var (prefix, n) = (m.Groups["prefix"].Value, int.Parse(m.Groups["num"].Value));
    if (n < warnFrom) return;

    Console.Error.WriteLine(n switch
    {
        >= 10 => $"warning: '{last}' sorts BEFORE lower-numbered labels of the same version - NuGet compares it as text, not as a number.",
        9 when warnFrom == 9 => $"warning: '{last}' is the last label of this form that sorts correctly - the next one would sort before it.",
        _ => $"warning: '{last}' glues its number into one label, so '{prefix}10' would sort before '{prefix}2'.",
    });
    Console.Error.WriteLine(warnFrom == 1
        ? $"         Prefer a dotted label - '{prefix.TrimEnd('-', '.')}.{n}' compares the number numerically."
        : "         Move to a dotted label (rc.1) or to GA rather than continuing this sequence.");
}

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}
