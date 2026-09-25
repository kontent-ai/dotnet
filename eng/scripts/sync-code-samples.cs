// Publishes the marked code samples to a kontent-ai-learn-code-samples checkout. What a sample is, the
// rules for writing one and what this script does to each published file: CODE_SAMPLES.md.
//
//   dotnet run eng/scripts/sync-code-samples.cs -- <samples-checkout> [--check] [--base <ref>]
//
// The checkout is a clone of your fork (origin). By default the script branches sync/dotnet-<sha> off
// upstream/main - the Learn repository itself, added as the `upstream` remote when missing - writes the
// changed files, commits them, pushes the branch to origin and opens a draft pull request against upstream.
// The checkout must be clean. --check writes nothing and touches no branch: it exits 1 on drift, on a
// section with no published file, or on a published file with no section.

#:package Microsoft.CodeAnalysis.CSharp

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var check = args.Contains("--check");
string? branch = null;
string? previousBranch = null;
string? fork = null;
var baseIndex = Array.IndexOf(args, "--base");
const string UpstreamRepository = "Kontent-ai-Learn/kontent-ai-learn-code-samples";
var baseRef = baseIndex >= 0 && baseIndex + 1 < args.Length ? args[baseIndex + 1] : "upstream/main";
var positional = args.Where((a, i) => !a.StartsWith("--") && (baseIndex < 0 || i != baseIndex + 1)).ToList();

if (positional.Count != 1)
{
    Console.Error.WriteLine("usage: sync-code-samples <samples-checkout> [--check] [--base <ref>]");
    return 1;
}

var samplesRoot = Path.GetFullPath(positional[0]);
var netRoot = Path.Combine(samplesRoot, "net");
if (!Directory.Exists(netRoot))
{
    Console.Error.WriteLine($"sync-code-samples: {samplesRoot} has no net folder; pass a clone of your fork of {UpstreamRepository}");
    return 1;
}

var repoRoot = FindRepoRoot();
if (repoRoot is null) { Console.Error.WriteLine("sync-code-samples: not inside a git repository"); return 1; }

var srcRoot = Path.Combine(repoRoot, "src");
var sections = new Dictionary<string, (string File, string Body)>(StringComparer.Ordinal);
var reviews = new SortedDictionary<string, string>(StringComparer.Ordinal);
var clientNames = new Dictionary<string, string>(StringComparer.Ordinal);
var clients = new Dictionary<string, (string File, string Body)>(StringComparer.Ordinal);
foreach (var file in Directory.EnumerateFiles(srcRoot, "*.*", SearchOption.AllDirectories)
             .Where(f => Path.GetExtension(f) is ".cs" or ".sh")
             .Where(f => f.Split(Path.DirectorySeparatorChar).Contains("CodeSamples"))
             .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")))
{
    foreach (var block in ReadBlocks(file))
    {
        if (block.Unclosed)
        {
            Console.Error.WriteLine($"sync-code-samples: {Rel(file)} opens {block.Id ?? "a DocClient"} and never closes it");
            return 1;
        }

        if (block.Id is null)
        {
            var key = ClientKey(Product(file), block.ClientName);
            if (!clients.TryAdd(key, (file, block.Body)))
            {
                Console.Error.WriteLine($"sync-code-samples: DocClient {key} is declared in both {Rel(clients[key].File)} and {Rel(file)}");
                return 1;
            }
        }
        else
        {
            var lines = block.Body.Split('\n');
            var sample = lines.Where(l => !ReviewMarker().IsMatch(l) && !ClientNameMarker().IsMatch(l));
            if (!sections.TryAdd(block.Id, (file, string.Join('\n', sample))))
            {
                Console.Error.WriteLine($"sync-code-samples: id '{block.Id}' is used by both {Rel(sections[block.Id].File)} and {Rel(file)}");
                return 1;
            }

            var review = lines.Select(l => ReviewMarker().Match(l)).FirstOrDefault(m => m.Success);
            if (review is not null) reviews[block.Id] = review.Groups[1].Value;

            var clientName = lines.Select(l => ClientNameMarker().Match(l)).FirstOrDefault(m => m.Success);
            if (clientName is not null) clientNames[block.Id] = clientName.Groups[1].Value;
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

    // Checked before anything is created, so a checkout that cannot be pushed or pull-requested is left untouched.
    // The owner is read from the path rather than the host, so an SSH host alias works.
    var origin = GitQuiet(samplesRoot, "remote", "get-url", "origin");
    fork = origin is null ? "" : Regex.Match(origin, @"[:/]([^/:]+)/[^/]+?/?$").Groups[1].Value;
    if (fork.Length == 0)
    {
        Console.Error.WriteLine($"sync-code-samples: cannot read a GitHub owner from origin ({origin ?? "missing"}); origin must be your fork of {UpstreamRepository}");
        return 1;
    }
    if (string.Equals(fork, UpstreamRepository.Split('/')[0], StringComparison.OrdinalIgnoreCase))
    {
        Console.Error.WriteLine($"sync-code-samples: origin is {UpstreamRepository} itself, which only takes pull requests; clone your fork instead");
        return 1;
    }

    if (GhProblem() is { } ghProblem)
    {
        Console.Error.WriteLine($"sync-code-samples: {ghProblem}");
        return 1;
    }

    if (Git(repoRoot, "status", "--porcelain", "--", "src") is not { Length: 0 })
        Console.Error.WriteLine("sync-code-samples: warning - src has uncommitted changes, the branch name will not identify the samples' source");

    if (Git(repoRoot, "branch", "--remotes", "--contains", "HEAD") is not { Length: > 0 })
        Console.Error.WriteLine("sync-code-samples: warning - this dotnet commit is not pushed, the pull request will link a commit nobody else can see");

    var remote = baseRef.Contains('/') ? baseRef[..baseRef.IndexOf('/')] : null;
    if (remote == "upstream" && GitQuiet(samplesRoot, "remote", "get-url", "upstream") is null)
    {
        if (Git(samplesRoot, "remote", "add", "upstream", $"https://github.com/{UpstreamRepository}.git") is null) return 1;
        Console.WriteLine($"added remote upstream -> {UpstreamRepository}");
    }
    if (remote is not null && Git(samplesRoot, "fetch", "--quiet", remote) is null) return 1;

    previousBranch = Git(samplesRoot, "rev-parse", "--abbrev-ref", "HEAD");
    branch = $"sync/dotnet-{Git(repoRoot, "rev-parse", "--short", "HEAD")}";
    if (Git(samplesRoot, "switch", "--quiet", "-c", branch, "--no-track", baseRef) is null) return 1;
    Console.WriteLine($"created {branch} off {baseRef} in {samplesRoot}");
}

var published = Directory.EnumerateFiles(netRoot, "*", SearchOption.AllDirectories)
    .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

var duplicatePublished = published.Where(p => p.Value.Count > 1).ToList();
foreach (var (id, files) in duplicatePublished)
    Console.Error.WriteLine($"sync-code-samples: '{id}' is published more than once: {string.Join(", ", files)}");

var unpublished = sections.Keys.Where(id => !published.ContainsKey(id)).Order(StringComparer.Ordinal).ToList();
foreach (var id in unpublished)
    Console.Error.WriteLine($"sync-code-samples: {id} ({Rel(sections[id].File)}) has no published file - it must exist in Kontent.ai Learn first");

var mismatched = sections
    .Where(s => !string.Equals(Path.GetExtension(s.Value.File), Path.GetExtension(published[s.Key][0]), StringComparison.OrdinalIgnoreCase))
    .Select(s => s.Key)
    .ToList();
foreach (var id in mismatched)
    Console.Error.WriteLine($"sync-code-samples: {id} is written in {Rel(sections[id].File)} but published as {Path.GetFileName(published[id][0])}");

if (duplicatePublished.Count > 0 || unpublished.Count > 0 || mismatched.Count > 0) return 1;

var updated = new List<string>();
var unchanged = 0;
foreach (var (id, (file, body)) in sections.OrderBy(s => s.Key, StringComparer.Ordinal))
{
    var path = published[id][0];
    var current = File.ReadAllText(path).ReplaceLineEndings("\n");
    var clientKey = ClientKey(Product(file), clientNames.GetValueOrDefault(id));
    var client = clients.TryGetValue(clientKey, out var c) ? c.Body : null;
    var isCSharp = Path.GetExtension(file) == ".cs";
    var rendered = isCSharp ? Render(current, body, client) : Verbatim(current, body);
    if (rendered is null)
    {
        Console.Error.WriteLine($"sync-code-samples: {Path.GetRelativePath(samplesRoot, path)} declares a client, but there is no DocClient {clientKey}");
        return 1;
    }

    if (isCSharp && HasUsings(rendered))
    {
        var context = BindingContext.For(file);
        if (context is null) return 1;

        var usings = context.RequiredUsings(rendered);
        if (usings.Count == 0)
            Console.Error.WriteLine($"sync-code-samples: warning - no namespace found for {Path.GetRelativePath(samplesRoot, path)}, its usings are left as they are");
        else
            rendered = ReplaceUsings(rendered, usings);
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
if (reviews.Count > 0) Console.WriteLine($"flagged for review: {reviews.Count}");
foreach (var (id, note) in reviews) Console.WriteLine($"  review  {id}: {note}");

if (check) return updated.Count > 0 || withoutSource.Count > 0 ? 1 : 0;

if (updated.Count == 0)
{
    Git(samplesRoot, "switch", "--quiet", previousBranch!);
    Git(samplesRoot, "branch", "--quiet", "-D", branch!);
    Console.WriteLine("nothing to sync; the branch was removed");
    return 0;
}

var dotnetCommit = Git(repoRoot, "rev-parse", "HEAD")!;
if (Git(samplesRoot, ["add", "--", .. updated]) is null) return 1;
if (Git(samplesRoot, "commit", "--quiet", "-m", $"sync .NET samples from kontent-ai/dotnet@{dotnetCommit[..9]}") is null) return 1;
if (Git(samplesRoot, "push", "--quiet", "-u", "origin", branch!) is null) return 1;
Console.WriteLine($"pushed {branch} to origin");

var targetBranch = baseRef.Contains('/') ? baseRef[(baseRef.IndexOf('/') + 1)..] : baseRef;
var prBody = $"""
    Generated by `eng/scripts/sync-code-samples.cs` from [kontent-ai/dotnet@{dotnetCommit[..9]}](https://github.com/kontent-ai/dotnet/commit/{dotnetCommit}).

    Every changed file comes from a tested sample in that repository; how they map is described in its [CODE_SAMPLES.md](https://github.com/kontent-ai/dotnet/blob/main/CODE_SAMPLES.md). {updated.Count} file(s) updated.
    """ + (reviews.Count == 0 ? "" : "\n\n**Flagged for review:**\n\n" + string.Join('\n', reviews.Select(r => $"- `{r.Key}`: {r.Value}")));

using var pr = Process.Start(new ProcessStartInfo("gh",
    ["pr", "create", "--draft", "--repo", UpstreamRepository, "--base", targetBranch, "--head", $"{fork}:{branch}",
     "--title", "Sync .NET samples from kontent-ai/dotnet", "--body", prBody])
    { WorkingDirectory = samplesRoot, RedirectStandardOutput = true, RedirectStandardError = true })!;

var prOutput = pr.StandardOutput.ReadToEnd().Trim();
var prError = pr.StandardError.ReadToEnd().Trim();
pr.WaitForExit();
if (pr.ExitCode != 0)
{
    Console.Error.WriteLine($"sync-code-samples: opening the pull request failed; the branch is pushed, open it from {fork}:{branch} yourself\n{prError}");
    return 1;
}

Console.WriteLine($"opened draft pull request {prOutput}");
return 0;

// The published file keeps its leading comments and usings. Its client declaration, when it has
// one, becomes the product's DocClient block - or is dropped when the section declares its own client,
// because then the registration is what the sample shows. The section's own leading lines that repeat
// the preamble (the "// Tip:" comment) are dropped, so they are not published twice. Null when the
// file's client declaration has nothing to replace it.
static string? Render(string current, string body, string? client)
{
    var lines = current.Split('\n');
    var (clientStart, clientEnd, preambleEnd) = Preamble(lines);
    var hasClient = clientEnd > clientStart;
    var sectionOwnsClient = body.Split('\n').Any(l => ClientDeclaration().IsMatch(l) && !char.IsWhiteSpace(l[0]));
    if (hasClient && !sectionOwnsClient && client is null) return null;

    string[] preamble = !hasClient ? lines[..preambleEnd]
        : sectionOwnsClient ? lines[..clientStart]
        : [.. lines[..clientStart], .. client!.Split('\n'), .. lines[clientEnd..preambleEnd]];

    var bodyLines = body.Split('\n').ToList();
    while (bodyLines.Count > 0 && (bodyLines[0].Trim().Length == 0 || preamble.Contains(bodyLines[0])))
        bodyLines.RemoveAt(0);

    var code = string.Join('\n', bodyLines).TrimEnd();
    var rendered = preamble.Length == 0 ? code : string.Join('\n', preamble) + "\n" + code;
    return current.EndsWith('\n') ? rendered + "\n" : rendered;
}

// The preamble is the leading block of comments, using directives and blank lines (the header), then
// the `client` declaration statement when the file has one, then the blank lines after it. The comment
// lines directly above the declaration belong to it - they describe how the client is built - except a
// "// Tip:" line, which heads the whole file. Returns where the declaration starts and ends and where
// the preamble ends; clientStart == clientEnd when there is no declaration.
static (int ClientStart, int ClientEnd, int PreambleEnd) Preamble(string[] lines)
{
    var i = 0;
    while (i < lines.Length && IsPreambleLine(lines[i])) i++;
    var headerEnd = i;

    if (i >= lines.Length || !ClientDeclaration().IsMatch(lines[i])) return (headerEnd, headerEnd, headerEnd);

    var clientStart = headerEnd;
    while (clientStart > 0 && lines[clientStart - 1].TrimStart().StartsWith("//") && !TipComment().IsMatch(lines[clientStart - 1]))
        clientStart--;

    var depth = 0;
    for (; i < lines.Length; i++)
    {
        depth += lines[i].Count(c => c is '(' or '{' or '[') - lines[i].Count(c => c is ')' or '}' or ']');
        if (depth == 0 && lines[i].TrimEnd().EndsWith(';')) { i++; break; }
    }

    var clientEnd = i;
    while (i < lines.Length && lines[i].Trim().Length == 0) i++;
    return (clientStart, clientEnd, i);
}

// A sample in any other language - the model generator's shell command - is published as it is written.
static string Verbatim(string current, string body)
{
    var code = body.Trim('\n').TrimEnd();
    return current.EndsWith('\n') ? code + "\n" : code;
}

static string[] Header(string text) =>
    text.Split('\n').TakeWhile(IsPreambleLine).ToArray();

static bool HasUsings(string text) =>
    Header(text).Any(line => UsingDirective().IsMatch(line.Trim()));

// The file's using directives become the required ones, in their place: System namespaces first, then
// the rest alphabetically.
static string ReplaceUsings(string text, IReadOnlyCollection<string> namespaces)
{
    var lines = text.Split('\n').ToList();
    var header = Header(text).Length;
    var first = lines.FindIndex(line => UsingDirective().IsMatch(line.Trim()));
    for (var i = header - 1; i >= first; i--)
        if (UsingDirective().IsMatch(lines[i].Trim())) lines.RemoveAt(i);

    var ordered = namespaces
        .OrderBy(ns => ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) ? 0 : 1)
        .ThenBy(ns => ns, StringComparer.Ordinal)
        .Select(ns => $"using {ns};");
    lines.InsertRange(first, ordered);
    return string.Join('\n', lines);
}

static bool IsPreambleLine(string line)
{
    var trimmed = line.Trim();
    return trimmed.Length == 0 || trimmed.StartsWith("//") || UsingDirective().IsMatch(trimmed);
}

// A DocSection block carries its id; a DocClient block has none, and an optional name.
static IEnumerable<(string? Id, string? ClientName, string Body, bool Unclosed)> ReadBlocks(string file)
{
    var open = false;
    string? id = null;
    string? clientName = null;
    var buffer = new List<string>();
    foreach (var line in File.ReadLines(file))
    {
        var opened = OpenMarker().Match(line);
        var openedClient = OpenClientMarker().Match(line);
        if (opened.Success || openedClient.Success)
        {
            if (open) yield return (id, clientName, "", true);
            open = true;
            id = opened.Success ? opened.Groups[1].Value : null;
            clientName = openedClient.Success && openedClient.Groups[1].Success ? openedClient.Groups[1].Value : null;
            buffer.Clear();
            continue;
        }

        if (open && (id is null ? CloseClientMarker() : CloseMarker()).IsMatch(line))
        {
            yield return (id, clientName, Dedent(buffer), false);
            open = false;
            continue;
        }

        if (open) buffer.Add(line.TrimEnd());
    }

    if (open) yield return (id, clientName, "", true);
}

// A product's default client block is its unnamed one; a section picks another with `// DocClientName: <name>`.
static string ClientKey(string product, string? name) => name is null ? product : $"{product}:{name}";

static string Dedent(List<string> lines)
{
    var indent = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
    return string.Join('\n', lines.Select(l => l.Length >= indent ? l[indent..] : l.TrimStart()));
}

// Null when gh can open the pull request; otherwise what the user has to fix.
static string? GhProblem()
{
    try
    {
        using var gh = Process.Start(new ProcessStartInfo("gh", ["auth", "status"]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        gh.StandardOutput.ReadToEnd();
        gh.StandardError.ReadToEnd();
        gh.WaitForExit();
        return gh.ExitCode == 0 ? null : "gh is not logged in; run gh auth login";
    }
    catch (System.ComponentModel.Win32Exception)
    {
        return "gh is not installed; see https://cli.github.com/";
    }
}

// For probes whose failure is an answer, not an error.
static string? GitQuiet(string workingDirectory, params string[] arguments)
{
    var info = new ProcessStartInfo("git") { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);

    using var process = Process.Start(info)!;
    var output = process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode == 0 ? output.Trim() : null;
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

// Binds a published file against one product's test build: its SDK, its dependencies and the models its
// samples use. The candidate namespaces are the ones the product's samples compile with - their usings,
// the test project's global usings and the product namespaces their own namespace sits in. Test-only
// namespaces and the implicit ones are imported silently so the code binds, but are never written.
sealed class BindingContext
{
    private static readonly string[] ImplicitNamespaces =
        ["System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks"];

    private static readonly Dictionary<string, BindingContext?> Cache = new(StringComparer.Ordinal);

    private readonly IReadOnlyList<MetadataReference> _references;
    private readonly HashSet<string> _candidates;
    private readonly SyntaxTree _imports;

    private BindingContext(IReadOnlyList<MetadataReference> references, HashSet<string> candidates, IEnumerable<string> silent)
    {
        _references = references;
        _candidates = candidates;
        var globals = candidates.Concat(silent).Concat(ImplicitNamespaces).Distinct().Select(ns => $"global using {ns};");
        _imports = CSharpSyntaxTree.ParseText(string.Join('\n', globals));
    }

    public static BindingContext? For(string sampleFile)
    {
        var samples = Path.GetDirectoryName(sampleFile)!;
        while (Path.GetFileName(samples) != "CodeSamples") samples = Path.GetDirectoryName(samples)!;
        var project = Path.GetDirectoryName(samples)!;

        if (!Cache.TryGetValue(project, out var context))
            Cache[project] = context = Create(project, samples);
        return context;
    }

    private static BindingContext? Create(string project, string samples)
    {
        var csproj = Directory.EnumerateFiles(project, "*.csproj").Single();
        Console.WriteLine($"building {Path.GetFileName(csproj)} to bind samples against");
        var build = Process.Start(new ProcessStartInfo("dotnet", ["build", csproj, "--nologo", "-v", "q", "-c", "Debug"])
            { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = build.StandardOutput.ReadToEnd() + build.StandardError.ReadToEnd();
        build.WaitForExit();
        if (build.ExitCode != 0)
        {
            Console.Error.WriteLine($"sync-code-samples: building {csproj} failed\n{output}");
            return null;
        }

        var framework = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var runtimeDir = Path.TrimEndingDirectorySeparator(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var aspNetDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir)!)!, "Microsoft.AspNetCore.App");
        var aspNet = Directory.Exists(aspNetDir)
            ? Directory.EnumerateFiles(Directory.EnumerateDirectories(aspNetDir).OrderBy(d => Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : new Version()).Last(), "*.dll")
            : [];
        var shared = framework.Concat(aspNet).ToList();
        var sharedNames = shared.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var targetPath = Process.Start(new ProcessStartInfo("dotnet", ["msbuild", csproj, "-getProperty:TargetPath", "-p:Configuration=Debug"])
            { RedirectStandardOutput = true })!.StandardOutput.ReadToEnd().Trim();
        var bin = Path.GetDirectoryName(targetPath)!;
        var references = shared
            .Concat(Directory.EnumerateFiles(bin, "*.dll").Where(dll => !sharedNames.Contains(Path.GetFileName(dll))))
            .Select(TryReference)
            .OfType<MetadataReference>()
            .ToList();

        var sources = Directory.EnumerateFiles(samples, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(project, "*.cs").Where(f => File.ReadAllText(f).Contains("global using")))
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f)).GetCompilationUnitRoot())
            .ToList();
        var declared = sources
            .SelectMany(root => root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            .Select(ns => ns.Name.ToString())
            .SelectMany(Ancestors);
        var imported = sources
            .SelectMany(root => root.Usings)
            .Where(u => u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None) && u.Name is not null)
            .Select(u => u.Name!.ToString());

        var all = declared.Concat(imported).ToHashSet(StringComparer.Ordinal);
        var testOnly = all.Where(IsTestOnly).ToList();
        var candidates = all.Where(ns => !IsTestOnly(ns) && !ImplicitNamespaces.Contains(ns)).ToHashSet(StringComparer.Ordinal);
        return new BindingContext(references, candidates, testOnly);
    }

    // The namespaces a symbol's code needs that a reader has to import: those of the types it names and
    // of the extension methods it calls.
    public IReadOnlyCollection<string> RequiredUsings(string text)
    {
        var withoutUsings = string.Join('\n', text.Split('\n').Where(line => !UsingLine(line)));
        var tree = CSharpSyntaxTree.ParseText(withoutUsings);
        var compilation = CSharpCompilation.Create(
            "sample",
            [tree, _imports],
            _references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication, nullableContextOptions: NullableContextOptions.Enable));
        var model = compilation.GetSemanticModel(tree);

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (name is IdentifierNameSyntax { IsVar: true }) continue;
            if (name.Parent is QualifiedNameSyntax qualified && qualified.Right == name) continue;

            var symbol = model.GetSymbolInfo(name).Symbol;
            var ns = symbol switch
            {
                INamedTypeSymbol type when name.Parent is not MemberAccessExpressionSyntax { Name: var right } || right != name => type.ContainingNamespace,
                IMethodSymbol { ReducedFrom: not null } extension => extension.ContainingType.ContainingNamespace,
                IMethodSymbol { MethodKind: MethodKind.Constructor } ctor => ctor.ContainingType.ContainingNamespace,
                _ => null,
            };
            if (ns is { IsGlobalNamespace: false }) used.Add(ns.ToDisplayString());
        }

        return used.Where(_candidates.Contains).ToList();
    }

    private static bool UsingLine(string line) => System.Text.RegularExpressions.Regex.IsMatch(line.Trim(), @"^using\s+(static\s+)?[\w.]+(\s*=\s*[\w.<>]+)?\s*;$");

    private static bool IsTestOnly(string ns) =>
        ns.Split('.').Any(part => part is "Tests" or "Xunit") || ns.StartsWith("RichardSzalay", StringComparison.Ordinal)
        || ns.StartsWith("AwesomeAssertions", StringComparison.Ordinal) || ns.StartsWith("NSubstitute", StringComparison.Ordinal);

    private static IEnumerable<string> Ancestors(string ns)
    {
        var parts = ns.Split('.');
        return Enumerable.Range(1, parts.Length).Select(n => string.Join('.', parts[..n]));
    }

    private static MetadataReference? TryReference(string path)
    {
        try { return MetadataReference.CreateFromFile(path); }
        catch (BadImageFormatException) { return null; }
    }
}

partial class Program
{
    [GeneratedRegex(@"^\s*(?://|#)\s*DocSection:\s*(\S+)\s*$")]
    private static partial Regex OpenMarker();

    [GeneratedRegex(@"^\s*(?://|#)\s*EndDocSection\s*$")]
    private static partial Regex CloseMarker();

    [GeneratedRegex(@"^\s*(?://|#)\s*DocReview:\s*(.+?)\s*$")]
    private static partial Regex ReviewMarker();

    [GeneratedRegex(@"^\s*(?://|#)\s*DocClient(?::\s*(\S+))?\s*$")]
    private static partial Regex OpenClientMarker();

    [GeneratedRegex(@"^\s*(?://|#)\s*DocClientName:\s*(\S+)\s*$")]
    private static partial Regex ClientNameMarker();

    [GeneratedRegex(@"^\s*(?://|#)\s*EndDocClient\s*$")]
    private static partial Regex CloseClientMarker();

    [GeneratedRegex(@"^using\s+(static\s+)?[\w.]+(\s*=\s*[\w.<>]+)?\s*;$")]
    private static partial Regex UsingDirective();

    [GeneratedRegex(@"^\s*//\s*Tip:")]
    private static partial Regex TipComment();

    [GeneratedRegex(@"^\s*(await\s+)?(using\s+)?var\s+client\b")]
    private static partial Regex ClientDeclaration();
}
