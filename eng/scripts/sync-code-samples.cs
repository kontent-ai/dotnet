// Publishes the marked code samples to a kontent-ai-learn-code-samples checkout.
//
//   dotnet run eng/scripts/sync-code-samples.cs -- <samples-checkout> [--check] [--base <ref>]
//
// Every `// DocSection: <id>` ... `// EndDocSection` pair under src/**/CodeSamples is the source of
// net/**/<id>.cs. The published file keeps its structure: leading comments and usings stay exactly
// as they are, as does its trailing newline. A client declaration in the file is replaced with the
// `// DocClient` ... `// EndDocClient` block of the section's product, so the registration Learn
// shows compiles against the SDK too. The code after that is replaced with the section. A section
// whose id has no published file is an error: the id is the join key with Kontent.ai Learn, so the
// file has to exist there first.
//
// Default mode creates sync/dotnet-<sha> off --base (origin/master) in the checkout, writes the
// files and stages them. It commits nothing. The checkout must be clean.
// --check writes nothing and touches no branch: it compares against the checkout as it is and
// exits 1 on drift, on a section with no published file, or on a published file with no section.

using System.Diagnostics;
using System.Text.RegularExpressions;

var check = args.Contains("--check");
var baseIndex = Array.IndexOf(args, "--base");
var baseRef = baseIndex >= 0 && baseIndex + 1 < args.Length ? args[baseIndex + 1] : "origin/master";
var positional = args.Where((a, i) => !a.StartsWith("--") && (baseIndex < 0 || i != baseIndex + 1)).ToList();

if (positional.Count != 1)
{
    Console.Error.WriteLine("usage: sync-code-samples <samples-checkout> [--check] [--base <ref>]");
    return 1;
}

var samplesRoot = Path.GetFullPath(positional[0]);
var netRoot = Path.Combine(samplesRoot, "net");
if (!Directory.Exists(netRoot)) { Console.Error.WriteLine($"sync-code-samples: no net folder in {samplesRoot}"); return 1; }

var repoRoot = FindRepoRoot();
if (repoRoot is null) { Console.Error.WriteLine("sync-code-samples: not inside a git repository"); return 1; }

var srcRoot = Path.Combine(repoRoot, "src");
var sections = new Dictionary<string, (string File, string Body)>(StringComparer.Ordinal);
var clients = new Dictionary<string, (string File, string Body)>(StringComparer.Ordinal);
foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
             .Where(f => f.Split(Path.DirectorySeparatorChar).Contains("CodeSamples"))
             .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")))
{
    foreach (var block in ReadBlocks(file))
    {
        if (block.Unclosed)
        {
            Console.Error.WriteLine($"sync-code-samples: {Rel(file)} opens {(block.Id is null ? "a DocClient" : block.Id)} and never closes it");
            return 1;
        }

        if (block.Id is null)
        {
            var product = Product(file);
            if (!clients.TryAdd(product, (file, block.Body)))
            {
                Console.Error.WriteLine($"sync-code-samples: {product} has a DocClient in both {Rel(clients[product].File)} and {Rel(file)}");
                return 1;
            }
        }
        else if (!sections.TryAdd(block.Id, (file, block.Body)))
        {
            Console.Error.WriteLine($"sync-code-samples: id '{block.Id}' is used by both {Rel(sections[block.Id].File)} and {Rel(file)}");
            return 1;
        }
    }
}

if (!check)
{
    if (Git(samplesRoot, "status", "--porcelain") is not { Length: 0 })
    {
        Console.Error.WriteLine($"sync-code-samples: {samplesRoot} has uncommitted changes");
        return 1;
    }

    if (Git(repoRoot, "status", "--porcelain", "--", "src") is not { Length: 0 })
        Console.Error.WriteLine("sync-code-samples: warning - src has uncommitted changes, the branch name will not identify the samples' source");

    var remote = baseRef.Contains('/') ? baseRef[..baseRef.IndexOf('/')] : null;
    if (remote is not null && Git(samplesRoot, "fetch", "--quiet", remote) is null) return 1;

    var branch = $"sync/dotnet-{Git(repoRoot, "rev-parse", "--short", "HEAD")}";
    if (Git(samplesRoot, "switch", "--quiet", "-c", branch, "--no-track", baseRef) is null) return 1;
    Console.WriteLine($"created {branch} off {baseRef} in {samplesRoot}");
}

var published = Directory.EnumerateFiles(netRoot, "*.cs", SearchOption.AllDirectories)
    .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

var duplicatePublished = published.Where(p => p.Value.Count > 1).ToList();
foreach (var (id, files) in duplicatePublished)
    Console.Error.WriteLine($"sync-code-samples: '{id}' is published more than once: {string.Join(", ", files)}");

var unpublished = sections.Keys.Where(id => !published.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();
foreach (var id in unpublished)
    Console.Error.WriteLine($"sync-code-samples: {id} ({Rel(sections[id].File)}) has no published file - it must exist in Kontent.ai Learn first");

if (duplicatePublished.Count > 0 || unpublished.Count > 0) return 1;

var updated = new List<string>();
var unchanged = 0;
foreach (var (id, (file, body)) in sections.OrderBy(s => s.Key, StringComparer.Ordinal))
{
    var path = published[id][0];
    var current = File.ReadAllText(path).ReplaceLineEndings("\n");
    var client = clients.TryGetValue(Product(file), out var c) ? c.Body : null;
    var rendered = Render(current, body, client);
    if (rendered is null)
    {
        Console.Error.WriteLine($"sync-code-samples: {Path.GetRelativePath(samplesRoot, path)} declares a client, but {Product(file)} has no DocClient block");
        return 1;
    }

    if (rendered == current) { unchanged++; continue; }

    updated.Add(Path.GetRelativePath(samplesRoot, path));
    if (!check) File.WriteAllText(path, rendered);
}

var withoutSource = published.Keys.Where(id => !sections.ContainsKey(id))
    .Select(id => Path.GetRelativePath(samplesRoot, published[id][0]))
    .Order(StringComparer.Ordinal).ToList();

Console.WriteLine($"{(check ? "out of date" : "updated")}: {updated.Count}, unchanged: {unchanged}, published without a source: {withoutSource.Count}");
foreach (var file in updated) Console.WriteLine($"  {(check ? "stale" : "updated")}  {file}");
foreach (var file in withoutSource) Console.WriteLine($"  no source  {file}");

if (check) return updated.Count > 0 || withoutSource.Count > 0 ? 1 : 0;

if (updated.Count > 0 && Git(samplesRoot, ["add", "--", .. updated]) is null) return 1;
return 0;

// The published file keeps its leading comments and usings. Its client declaration, when it has
// one, becomes the product's DocClient block. The section's own leading lines that repeat the
// preamble (the "// Tip:" comment) are dropped, so they are not published twice. Null when the
// file declares a client and there is no DocClient to replace it with.
static string? Render(string current, string body, string? client)
{
    var lines = current.Split('\n');
    var (headerEnd, clientEnd, preambleEnd) = Preamble(lines);
    if (clientEnd > headerEnd && client is null) return null;

    var preamble = clientEnd > headerEnd
        ? [.. lines[..headerEnd], .. client!.Split('\n'), .. lines[clientEnd..preambleEnd]]
        : lines[..preambleEnd];

    var bodyLines = body.Split('\n').ToList();
    while (bodyLines.Count > 0 && (bodyLines[0].Trim().Length == 0 || preamble.Contains(bodyLines[0])))
        bodyLines.RemoveAt(0);

    var code = string.Join('\n', bodyLines).TrimEnd();
    var head = string.Join('\n', preamble);
    var rendered = preambleEnd == 0 ? code : head + "\n" + code;
    return current.EndsWith('\n') ? rendered + "\n" : rendered;
}

// The preamble is the leading block of comments, using directives and blank lines (the header),
// then the `client` declaration statement when the file has one, then the blank lines after it.
// Returns the index where each part ends; clientEnd == headerEnd when there is no declaration.
static (int HeaderEnd, int ClientEnd, int PreambleEnd) Preamble(string[] lines)
{
    var i = 0;
    while (i < lines.Length && IsPreambleLine(lines[i])) i++;
    var headerEnd = i;

    if (i < lines.Length && ClientDeclaration().IsMatch(lines[i]))
    {
        var depth = 0;
        for (; i < lines.Length; i++)
        {
            depth += lines[i].Count(c => c is '(' or '{' or '[') - lines[i].Count(c => c is ')' or '}' or ']');
            if (depth == 0 && lines[i].TrimEnd().EndsWith(';')) { i++; break; }
        }
    }

    var clientEnd = i;
    while (i < lines.Length && lines[i].Trim().Length == 0) i++;
    return (headerEnd, clientEnd, clientEnd == headerEnd ? headerEnd : i);
}

static bool IsPreambleLine(string line)
{
    var trimmed = line.Trim();
    return trimmed.Length == 0 || trimmed.StartsWith("//") || UsingDirective().IsMatch(trimmed);
}

// A DocSection block carries its id; a DocClient block has none.
static IEnumerable<(string? Id, string Body, bool Unclosed)> ReadBlocks(string file)
{
    var open = false;
    string? id = null;
    var buffer = new List<string>();
    foreach (var line in File.ReadLines(file))
    {
        var opened = OpenMarker().Match(line);
        if (opened.Success || OpenClientMarker().IsMatch(line))
        {
            if (open) yield return (id, "", true);
            (open, id) = (true, opened.Success ? opened.Groups[1].Value : null);
            buffer.Clear();
            continue;
        }

        if (open && (id is null ? CloseClientMarker() : CloseMarker()).IsMatch(line))
        {
            yield return (id, Dedent(buffer), false);
            open = false;
            continue;
        }

        if (open) buffer.Add(line.TrimEnd());
    }

    if (open) yield return (id, "", true);
}

static string Dedent(List<string> lines)
{
    var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
    return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
}

static string? Git(string workingDirectory, params string[] arguments)
{
    var info = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);

    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode == 0) return output.Trim();
    Console.Error.WriteLine($"sync-code-samples: git {string.Join(' ', arguments)} failed in {workingDirectory}\n{error.Trim()}");
    return null;
}

string Rel(string path) => Path.GetRelativePath(repoRoot, path);

string Product(string file) => Path.GetRelativePath(srcRoot, file).Split(Path.DirectorySeparatorChar)[0];

static string? FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !Directory.Exists(Path.Combine(dir, ".git")))
        dir = Path.GetDirectoryName(dir);
    return dir;
}

partial class Program
{
    [GeneratedRegex(@"^\s*//\s*DocSection:\s*(\S+)\s*$")]
    private static partial Regex OpenMarker();

    [GeneratedRegex(@"^\s*//\s*EndDocSection\s*$")]
    private static partial Regex CloseMarker();

    [GeneratedRegex(@"^\s*//\s*DocClient\s*$")]
    private static partial Regex OpenClientMarker();

    [GeneratedRegex(@"^\s*//\s*EndDocClient\s*$")]
    private static partial Regex CloseClientMarker();

    [GeneratedRegex(@"^using\s+(static\s+)?[\w.]+(\s*=\s*[\w.<>]+)?\s*;$")]
    private static partial Regex UsingDirective();

    [GeneratedRegex(@"^\s*(await\s+)?(using\s+)?var\s+client\b")]
    private static partial Regex ClientDeclaration();
}
