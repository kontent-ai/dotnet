// Reports, per product, whether the version declared in eng/Versions.props is on nuget.org.
//
//   dotnet run eng/scripts/release-status.cs [-- --json | --order | --is-published <product>]
//
// Prepared-but-unpublished is a legitimate but invisible state, so the reporting modes exit 0
// unless they cannot do the job. --order is the exception: it fails on a dependsOn cycle.
//
// --order emits the batch publish-batch.yml works through, dependencies first, tab-separated:
//   <product>\t<version>\t<tag>\t<release title>\t<"prerelease"|"">

using System.Text.Json;
using System.Text.RegularExpressions;

var asJson = args.Contains("--json");
var asOrder = args.Contains("--order");

// Answers "is this one product fully on NuGet yet" with an exit code, for the publish-batch poll
// loop - which asks up to 60 times per product and has no use for the rest of the report.
var isPublishedIndex = Array.IndexOf(args, "--is-published");
var singleProduct = isPublishedIndex >= 0 && isPublishedIndex + 1 < args.Length
    ? args[isPublishedIndex + 1]
    : null;
if (isPublishedIndex >= 0 && singleProduct is null)
{
    Console.Error.WriteLine("release-status: --is-published needs a product name");
    return 2;
}

var repoRoot = FindRepoRoot();
if (repoRoot is null) { Console.Error.WriteLine("release-status: not inside a git repository"); return 1; }

var products = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "eng", "products.json")));
var versionsXml = File.ReadAllText(Path.Combine(repoRoot, "eng", "Versions.props"));

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

var rows = new List<Row>();

if (singleProduct is not null && !products.RootElement.TryGetProperty(singleProduct, out _))
{
    Console.Error.WriteLine($"release-status: unknown product '{singleProduct}'");
    return 2;
}

foreach (var product in products.RootElement.EnumerateObject())
{
    if (singleProduct is not null && product.Name != singleProduct) continue;

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

if (singleProduct is not null)
{
    return rows.Single().Status == "published" ? 0 : 1;
}

if (asOrder)
{
    var pendingRows = rows.Where(r => r.Status != "published").ToList();
    var pendingNames = pendingRows.Select(r => r.Product).ToHashSet(StringComparer.Ordinal);

    // Kahn's algorithm over dependsOn, restricted to the products actually being released:
    // a dependency that is already on NuGet imposes no ordering here.
    var ordered = new List<Row>();
    var remaining = new List<Row>(pendingRows);
    while (remaining.Count > 0)
    {
        var ready = remaining
            .Where(r => DependenciesOf(r.Product).Where(pendingNames.Contains)
                            .All(d => ordered.Exists(o => o.Product == d)))
            // Stable within a tier, so the same repository state always produces the same batch.
            .OrderBy(r => r.Product, StringComparer.Ordinal)
            .ToList();

        if (ready.Count == 0)
        {
            // Naming the products leaves someone with the actual edit to make in eng/products.json.
            Console.Error.WriteLine(
                "release-status: dependsOn in eng/products.json has a cycle - these products cannot be ordered: " +
                string.Join(", ", remaining.Select(r => r.Product).Order(StringComparer.Ordinal)));
            return 1;
        }

        ordered.AddRange(ready);
        remaining.RemoveAll(ready.Contains);
    }

    foreach (var r in ordered)
    {
        var title = $"{products.RootElement.GetProperty(r.Product).GetProperty("expectedPackages")[0].GetString()} {r.Version}";
        var prerelease = r.Version.Contains('-') ? "prerelease" : "";
        Console.WriteLine($"{r.Product}\t{r.Version}\t{r.Product}-v{r.Version}\t{title}\t{prerelease}");
    }

    return 0;
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

IEnumerable<string> DependenciesOf(string product) =>
    products.RootElement.GetProperty(product).GetProperty("dependsOn")
        .EnumerateArray().Select(d => d.GetString()!);

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}

record Row(string Product, string Version, string Status, IReadOnlyList<string> UnpublishedPackages);
