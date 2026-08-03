// Resolves a release tag into a concrete publish plan, and refuses to proceed if the
// repository disagrees with the tag.
//
//   dotnet run eng/scripts/release-plan.cs -- aspnetcore-v0.17.0
//
// Tag format is <product>-v<version>. The "-v" separator is required, not cosmetic:
// product names and prerelease versions both contain hyphens (model-generator,
// 9.0.0-beta-4), so a bare "-" cannot be split back into product and version.
//
// Checks, all of which fail the release rather than publish something wrong:
//   1. tag parses, and the product exists in eng/products.json
//   2. the version in the tag equals the product's property in eng/Versions.props
//   3. the product's CHANGELOG.md has a "## <version>" heading
//
// Writes product/version/projects/packages to $GITHUB_OUTPUT when running in Actions.

using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 1)
{
    Console.Error.WriteLine("usage: release-plan <product>-v<version>");
    return 1;
}

var tag = args[0];
var repoRoot = FindRepoRoot();
if (repoRoot is null) { return Fail("not inside a git repository"); }

// --- 1. parse the tag ---------------------------------------------------------
var m = Regex.Match(tag, @"^(?<product>.+)-v(?<version>.+)$");
if (!m.Success)
    return Fail($"tag '{tag}' is not in <product>-v<version> form (e.g. aspnetcore-v0.17.0)");

var product = m.Groups["product"].Value;
var version = m.Groups["version"].Value;

var productsPath = Path.Combine(repoRoot, "eng", "products.json");
using var doc = JsonDocument.Parse(File.ReadAllText(productsPath));
if (!doc.RootElement.TryGetProperty(product, out var entry))
    return Fail($"unknown product '{product}'. Known: {string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name))}");

// --- 2. tag version must match eng/Versions.props -----------------------------
var versionProperty = entry.GetProperty("versionProperty").GetString()!;
var versionsXml = File.ReadAllText(Path.Combine(repoRoot, "eng", "Versions.props"));
var declared = Regex.Match(versionsXml, $@"<{versionProperty}>([^<]*)</{versionProperty}>").Groups[1].Value;

if (declared != version)
    return Fail($"tag says {version} but eng/Versions.props declares {versionProperty}={declared}. " +
                "Bump the property and merge before tagging.");

// --- 3. the changelog must mention this version -------------------------------
var changelogRel = entry.GetProperty("changelog").GetString()!;
var changelog = File.ReadAllText(Path.Combine(repoRoot, changelogRel));
if (!Regex.IsMatch(changelog, $@"^##\s+{Regex.Escape(version)}(\s|$)", RegexOptions.Multiline))
    return Fail($"{changelogRel} has no '## {version}' heading. " +
                "Run eng/scripts/update-version.cs so the release notes exist.");

// --- emit the plan ------------------------------------------------------------
var projects = entry.GetProperty("projects").EnumerateArray().Select(p => p.GetString()!).ToArray();
var packages = entry.GetProperty("expectedPackages").EnumerateArray().Select(p => p.GetString()!).ToArray();

Console.WriteLine($"product  : {product}");
Console.WriteLine($"version  : {version}");
Console.WriteLine($"projects : {projects.Length}");
foreach (var p in projects) Console.WriteLine($"    {p}");
Console.WriteLine($"packages : {string.Join(", ", packages)}");

var outFile = Environment.GetEnvironmentVariable("GITHUB_OUTPUT");
if (!string.IsNullOrEmpty(outFile))
{
    File.AppendAllLines(outFile, [
        $"product={product}",
        $"version={version}",
        $"projects={string.Join(' ', projects)}",
        $"packages={string.Join(' ', packages)}",
    ]);
}

return 0;

static int Fail(string message)
{
    Console.Error.WriteLine($"release-plan: {message}");
    return 1;
}

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}
