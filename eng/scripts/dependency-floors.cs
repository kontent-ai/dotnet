// Reports how far each cross-product dependency floor in Directory.Packages.props has fallen
// behind what is actually published on nuget.org.
//
//   dotnet run eng/scripts/dependency-floors.cs
//   dotnet run eng/scripts/dependency-floors.cs -- --json
//
// Why this exists: releasing a product does not touch the version its siblings depend on.
// Those floors are raised deliberately, in their own PR, after the dependency is published -
// see CONTRIBUTING.md, "Changing an API that another product consumes". Nothing else in the
// repo notices when one goes stale, so without this the gap is invisible until someone
// happens to look.
//
// Lagging is NOT an error. A floor is the minimum version a published package promises to
// work with, and raising it forces every downstream consumer to upgrade too. This reports the
// gap and leaves the decision to a human. It exits non-zero only when a floor names a version
// that is not on nuget.org at all - that one is a genuine breakage, because every restore in
// the repo resolves it, including the root restore in release.yml.
//
// Ordering is nuget.org's own: the flat-container index lists versions in SemVer order, so
// "newer" here means "listed after the floor". One caveat that scheme carries - a prerelease
// label like `beta-5` is a single alphanumeric SemVer identifier, compared lexically rather
// than numerically, so `9.0.0-beta-10` sorts BEFORE `9.0.0-beta-5`. The out-of-order pass
// below catches that case rather than silently under-reporting it.

using System.Text.Json;
using System.Text.RegularExpressions;

var asJson = args.Contains("--json");

var repoRoot = FindRepoRoot();
if (repoRoot is null) { Console.Error.WriteLine("dependency-floors: not inside a git repository"); return 1; }

var products = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "eng", "products.json")));
var packagesXml = File.ReadAllText(Path.Combine(repoRoot, "Directory.Packages.props"));

// Which product ships each package, and which products consume that product. dependsOn is the
// declared cross-product graph, so this stays right as products are added without a second list.
var owner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
foreach (var product in products.RootElement.EnumerateObject())
    foreach (var pkg in product.Value.GetProperty("expectedPackages").EnumerateArray())
        owner[pkg.GetString()!] = product.Name;

string[] ConsumersOf(string package) =>
    owner.TryGetValue(package, out var shippedBy)
        ? [.. products.RootElement.EnumerateObject()
            .Where(p => p.Value.GetProperty("dependsOn").EnumerateArray().Any(d => d.GetString() == shippedBy))
            .Select(p => p.Name)]
        : [];

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var rows = new List<Row>();

foreach (Match m in Regex.Matches(packagesXml, @"<PackageVersion\s+Include=""(Kontent\.Ai\.[^""]+)""\s+Version=""([^""]+)"""))
{
    var (id, floor) = (m.Groups[1].Value, m.Groups[2].Value);

    var published = await PublishedVersions(id);
    if (published is null)
    {
        rows.Add(new Row(id, floor, "-", 0, "NOT ON NUGET", ConsumersOf(id)));
        continue;
    }

    var at = published.FindIndex(v => string.Equals(v, floor, StringComparison.OrdinalIgnoreCase));
    if (at < 0)
    {
        rows.Add(new Row(id, floor, published[^1], 0, "NOT ON NUGET", ConsumersOf(id)));
        continue;
    }

    // Everything nuget.org sorts after the floor, plus anything it sorts before but that is
    // really a later prerelease of the same core version (the `beta-10` case above).
    var newer = published.Skip(at + 1).ToList();
    newer.AddRange(published.Take(at).Where(v => IsLaterPrereleaseOutOfOrder(floor, v)));

    var status = newer.Count == 0 ? "current" : $"behind by {newer.Count}";
    rows.Add(new Row(id, floor, newer.Count == 0 ? floor : newer[^1], newer.Count, status, ConsumersOf(id)));
}

if (rows.Count == 0)
{
    Console.Error.WriteLine("dependency-floors: no Kontent.Ai.* entries found in Directory.Packages.props");
    return 1;
}

var broken = rows.Where(r => r.Status == "NOT ON NUGET").ToArray();

if (asJson)
{
    // Written by hand rather than via JsonSerializer, matching release-status.cs: the
    // reflection-based overload trips IL2026/IL3050 (trim/AOT) warnings on a shape this small.
    Console.WriteLine("[");
    for (var i = 0; i < rows.Count; i++)
    {
        var r = rows[i];
        var consumers = string.Join(", ", r.Consumers.Select(c => $"\"{c}\""));
        Console.WriteLine($"  {{ \"package\": \"{r.Package}\", \"floor\": \"{r.Floor}\", " +
                          $"\"latest\": \"{r.Latest}\", \"behindBy\": {r.BehindBy}, " +
                          $"\"status\": \"{r.Status}\", \"consumers\": [{consumers}] }}" +
                          (i < rows.Count - 1 ? "," : ""));
    }
    Console.WriteLine("]");
    return broken.Length > 0 ? 1 : 0;
}

var wPackage = Math.Max(7, rows.Max(r => r.Package.Length));
var wFloor = Math.Max(5, rows.Max(r => r.Floor.Length));
var wLatest = Math.Max(6, rows.Max(r => r.Latest.Length));
Console.WriteLine($"{"package".PadRight(wPackage)}  {"floor".PadRight(wFloor)}  {"latest".PadRight(wLatest)}  status");
Console.WriteLine($"{new string('-', wPackage)}  {new string('-', wFloor)}  {new string('-', wLatest)}  ------");
foreach (var r in rows)
    Console.WriteLine($"{r.Package.PadRight(wPackage)}  {r.Floor.PadRight(wFloor)}  {r.Latest.PadRight(wLatest)}  {r.Status}");

var behind = rows.Where(r => r.BehindBy > 0).ToArray();
if (behind.Length > 0)
{
    Console.WriteLine();
    Console.WriteLine("Behind the latest published version:");
    foreach (var r in behind)
    {
        var consumers = r.Consumers.Length > 0 ? string.Join(", ", r.Consumers) : "nothing in this repo";
        Console.WriteLine($"  {r.Package} {r.Floor} -> {r.Latest} ({r.BehindBy} newer), consumed by {consumers}");
    }
    Console.WriteLine();
    Console.WriteLine("This is not automatically a problem - a floor is meant to lag. Raise one only when");
    Console.WriteLine("the consuming code actually needs the newer API, in its own PR, and remember that");
    Console.WriteLine("doing so forces every downstream consumer onto the newer version too.");
}

if (broken.Length > 0)
{
    Console.WriteLine();
    Console.WriteLine("Floors naming a version that is NOT on nuget.org:");
    foreach (var r in broken)
        Console.WriteLine($"  {r.Package} {r.Floor}");
    Console.WriteLine();
    Console.WriteLine("Every restore in the repo resolves these, so this breaks the build everywhere -");
    Console.WriteLine("including the release that would have published the missing version. Either publish");
    Console.WriteLine("it or put the floor back to a version that exists.");
    return 1;
}

if (behind.Length == 0)
    Console.WriteLine("\nEvery floor is at the latest published version.");

return 0;

async Task<List<string>?> PublishedVersions(string id)
{
    var url = $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json";
    using var response = await http.GetAsync(url);
    // A package that has never shipped at all has no index.
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return [.. doc.RootElement.GetProperty("versions").EnumerateArray().Select(v => v.GetString()!)];
}

// True when `candidate` is a later prerelease of the same core version than `floor` despite
// sorting earlier - i.e. same core, same label prefix, larger trailing number. Covers the
// `9.0.0-beta-10` vs `9.0.0-beta-5` trap that this repo's `<prefix>-<n>` labels will hit at 10.
static bool IsLaterPrereleaseOutOfOrder(string floor, string candidate)
{
    var (fCore, fPre) = Split(floor);
    var (cCore, cPre) = Split(candidate);
    if (fPre is null || cPre is null || !string.Equals(fCore, cCore, StringComparison.OrdinalIgnoreCase))
        return false;

    var f = Regex.Match(fPre, @"^(?<prefix>.*?)(?<num>\d+)$");
    var c = Regex.Match(cPre, @"^(?<prefix>.*?)(?<num>\d+)$");
    return f.Success && c.Success
        && string.Equals(f.Groups["prefix"].Value, c.Groups["prefix"].Value, StringComparison.OrdinalIgnoreCase)
        && int.Parse(c.Groups["num"].Value) > int.Parse(f.Groups["num"].Value);

    static (string Core, string? Pre) Split(string version)
    {
        var dash = version.IndexOf('-');
        return dash < 0 ? (version, null) : (version[..dash], version[(dash + 1)..]);
    }
}

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}

record Row(string Package, string Floor, string Latest, int BehindBy, string Status, string[] Consumers);
