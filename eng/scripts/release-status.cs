// Reports, per product, whether the version currently declared in eng/Versions.props has
// actually been published to nuget.org.
//
//   dotnet run eng/scripts/release-status.cs
//   dotnet run eng/scripts/release-status.cs -- --json
//
// Why this exists: preparing a release and publishing it are two separate steps. A batch can
// bump three products and then only two get released, leaving the third with a bumped version
// property and a dated changelog entry but nothing on NuGet. That state is legitimate - it just
// means "not yet" - but it is invisible, and preparing again would silently skip the version.
//
// Informational by design: it always exits 0 unless it cannot do its job (bad repo, network
// failure). A prepared-but-unpublished version is a normal intermediate state, not an error.

using System.Text.Json;
using System.Text.RegularExpressions;

var asJson = args.Contains("--json");

var repoRoot = FindRepoRoot();
if (repoRoot is null) { Console.Error.WriteLine("release-status: not inside a git repository"); return 1; }

var products = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "eng", "products.json")));
var versionsXml = File.ReadAllText(Path.Combine(repoRoot, "eng", "Versions.props"));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var rows = new List<Row>();

foreach (var product in products.RootElement.EnumerateObject())
{
    var versionProperty = product.Value.GetProperty("versionProperty").GetString()!;
    var declared = Regex.Match(versionsXml, $@"<{versionProperty}>([^<]*)</{versionProperty}>").Groups[1].Value;
    if (declared.Length == 0)
    {
        rows.Add(new Row(product.Name, "?", "NO VERSION PROPERTY", []));
        continue;
    }

    var packages = product.Value.GetProperty("expectedPackages")
        .EnumerateArray().Select(p => p.GetString()!).ToArray();

    var unpublished = new List<string>();
    foreach (var id in packages)
    {
        if (!await IsPublished(id, declared)) unpublished.Add(id);
    }

    // Partial is worth calling out separately: it usually means a publish run died part-way,
    // which is a different problem from "prepared but never released".
    var state = unpublished.Count switch
    {
        0 => "published",
        var n when n == packages.Length => "PREPARED, NOT PUBLISHED",
        _ => "PARTIALLY PUBLISHED",
    };
    rows.Add(new Row(product.Name, declared, state, unpublished));
}

if (asJson)
{
    // Written by hand rather than via JsonSerializer: the reflection-based overload trips
    // IL2026/IL3050 (trim/AOT) warnings, and this shape is three fields.
    Console.WriteLine("[");
    for (var i = 0; i < rows.Count; i++)
    {
        var r = rows[i];
        var missing = string.Join(", ", r.UnpublishedPackages.Select(p => $"\"{p}\""));
        Console.WriteLine($"  {{ \"product\": \"{r.Product}\", \"version\": \"{r.Version}\", " +
                          $"\"status\": \"{r.Status}\", \"unpublishedPackages\": [{missing}] }}" +
                          (i < rows.Count - 1 ? "," : ""));
    }
    Console.WriteLine("]");
    return 0;
}

var wProduct = Math.Max(7, rows.Max(r => r.Product.Length));
var wVersion = Math.Max(7, rows.Max(r => r.Version.Length));
Console.WriteLine($"{"product".PadRight(wProduct)}  {"version".PadRight(wVersion)}  status");
Console.WriteLine($"{new string('-', wProduct)}  {new string('-', wVersion)}  ------");
foreach (var r in rows)
{
    Console.WriteLine($"{r.Product.PadRight(wProduct)}  {r.Version.PadRight(wVersion)}  {r.Status}");
    foreach (var id in r.UnpublishedPackages.Where(_ => r.Status == "PARTIALLY PUBLISHED"))
        Console.WriteLine($"{new string(' ', wProduct + wVersion + 4)}missing: {id}");
}

var pending = rows.Where(r => r.Status != "published").ToArray();
if (pending.Length > 0)
{
    Console.WriteLine();
    Console.WriteLine("Prepared but not on NuGet:");
    foreach (var r in pending)
        Console.WriteLine($"  {r.Product} {r.Version} - release it with tag {r.Product}-v{r.Version}, " +
                          "or revert the bump in eng/Versions.props and fold the changelog entry back under '## Unreleased'.");
}

return 0;

async Task<bool> IsPublished(string id, string version)
{
    var url = $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json";
    using var response = await http.GetAsync(url);
    // A package that has never shipped at all has no index; that counts as unpublished.
    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
    response.EnsureSuccessStatusCode();

    using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return doc.RootElement.GetProperty("versions").EnumerateArray()
        .Any(v => string.Equals(v.GetString(), version, StringComparison.OrdinalIgnoreCase));
}

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}

record Row(string Product, string Version, string Status, IReadOnlyList<string> UnpublishedPackages);
