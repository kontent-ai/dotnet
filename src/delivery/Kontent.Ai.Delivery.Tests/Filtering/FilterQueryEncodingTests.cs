using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;

namespace Kontent.Ai.Delivery.Tests.Filtering;

/// <summary>
/// Characterization of the query string the filter DSL puts on the wire.
/// </summary>
/// <remarks>
/// <para>
/// These assert the exact bytes sent for a given DSL expression — the contract the Delivery API is
/// actually consuming. They deliberately go through the public DSL and <see cref="IDeliveryClient"/>
/// rather than the internal Refit interface, so they stay valid if the transport underneath changes.
/// </para>
/// <para>
/// Written ahead of the work in <c>docs/delivery-filter-dsl-plan.md</c>, which replaces the
/// reflection-based query building. Every expectation here was captured from the current
/// implementation and reviewed; they must pass <em>unchanged</em> afterwards. A diff in this file
/// during that refactor means the wire contract moved, which is the one thing that must not happen.
/// </para>
/// </remarks>
public class FilterQueryEncodingTests
{
    [Fact]
    public async Task PlainValue_IsSentAsIs()
        => Assert.Equal("?elements.title%5Beq%5D=hello", await CaptureAsync(f => f.Element("title").IsEqualTo("hello")));

    [Fact]
    public async Task ReservedCharacters_AreEscapedOnce()
        => Assert.Equal("?elements.title%5Beq%5D=Hello%20%26%20World", await CaptureAsync(f => f.Element("title").IsEqualTo("Hello & World")));

    [Fact]
    public async Task PreEncodedValue_IsEscapedAgain()
        // Deliberate: the DSL takes raw values. A caller who pre-encodes gets their percent signs
        // escaped, which surfaces the mistake rather than silently accepting either form.
        => Assert.Equal("?elements.title%5Beq%5D=Hello%2520%2526%2520World", await CaptureAsync(f => f.Element("title").IsEqualTo("Hello%20%26%20World")));

    [Fact]
    public async Task NonAsciiValue_IsUtf8PercentEncoded()
        => Assert.Equal("?elements.title%5Beq%5D=P%C5%99%C3%ADli%C4%8D%20%C5%BElu%C5%A5ou%C4%8Dk%C3%BD", await CaptureAsync(f => f.Element("title").IsEqualTo("Přílič žluťoučký")));

    [Fact]
    public async Task CommaInValue_IsEscaped_NotTreatedAsSeparator()
        => Assert.Equal("?elements.title%5Beq%5D=a%2Cb", await CaptureAsync(f => f.Element("title").IsEqualTo("a,b")));

    [Fact]
    public async Task PlusAndSlash_AreEscaped()
        // '+' must not reach the wire raw: the API would read it as a space.
        => Assert.Equal("?elements.title%5Beq%5D=a%2Bb%2Fc", await CaptureAsync(f => f.Element("title").IsEqualTo("a+b/c")));

    [Fact]
    public async Task OperatorSuffix_IsEscapedInTheKey()
        => Assert.Equal("?elements.title%5Bempty%5D=", await CaptureAsync(f => f.Element("title").IsEmpty()));

    [Fact]
    public async Task IsIn_JoinsValuesIntoOneParameter()
        // The array operators comma-join into a single value; they do not repeat the key.
        => Assert.Equal("?elements.tags%5Bin%5D=x%2Cy", await CaptureAsync(f => f.Element("tags").IsIn("x", "y")));

    [Fact]
    public async Task SystemFilter_UsesSystemPrefix()
        => Assert.Equal("?system.type%5Beq%5D=article", await CaptureAsync(f => f.System("type").IsEqualTo("article")));

    [Fact]
    public async Task MultipleFilters_KeepDeclarationOrder()
        => Assert.Equal("?system.type%5Beq%5D=article&system.language%5Beq%5D=en-US", await CaptureAsync(f => f.System("type").IsEqualTo("article").System("language").IsEqualTo("en-US")));

    [Fact]
    public async Task RepeatedFilter_IsSentAsRepeatedKey()
        // Duplicates are preserved rather than collapsed — two [contains] on one element is AND.
        => Assert.Equal("?elements.tags%5Bcontains%5D=a&elements.tags%5Bcontains%5D=b", await CaptureAsync(f => f.Element("tags").Contains("a").Element("tags").Contains("b")));

    [Fact]
    public async Task InterleavedKeys_AreGrouped_LosingDeclarationOrder()
        // Declared a=1, b=2, a=3 — sent as a=1, a=3, b=2. Same-key values are pulled together
        // because the filters pass through a Dictionary<string, string[]> on the way to the wire,
        // and grouping is what building that dictionary does.
        //
        // THE ONE EXPECTATION IN THIS FILE THAT THE PLANNED REFACTOR CHANGES. Rendering straight
        // from the ordered filter list emits declaration order instead, which is faithful to what
        // the caller wrote. Both are accepted by the API — the operators are AND-ed and order is
        // not significant — so this is a tidy-up, not a fix. See docs/delivery-filter-dsl-plan.md §6.1.
        => Assert.Equal("?elements.a%5Beq%5D=1&elements.a%5Beq%5D=3&elements.b%5Beq%5D=2", await CaptureAsync(f => f.Element("a").IsEqualTo("1").Element("b").IsEqualTo("2").Element("a").IsEqualTo("3")));

    [Fact]
    public async Task BoolAndNumber_UseInvariantFormatting()
        => Assert.Equal("?elements.flag%5Beq%5D=true&elements.price%5Beq%5D=1.5", await CaptureAsync(f => f.Element("flag").IsEqualTo(true).Element("price").IsEqualTo(1.5)));

    [Fact]
    public async Task FilterQuery_IsIdenticalOnEveryRetryAttempt()
    {
        // Guards the design in docs/delivery-filter-dsl-plan.md §5.2. The planned implementation moves
        // filters out of the Refit parameter list and onto the request via a DelegatingHandler. The
        // resilience pipeline sits OUTSIDE the handler chain, so every inner handler re-runs per retry
        // attempt — a handler that appends to RequestUri instead of rebuilding it would send
        // "?a=1&a=1" on the second attempt. Wrong results, no exception, and only under retry.
        //
        // Passes trivially today (nothing mutates the URI). It exists to fail the moment that stops
        // being true.
        var handler = new RetryCaptureHandler();
        var services = new ServiceCollection();
        services.AddDeliveryClient(
            new DeliveryOptions { EnvironmentId = Guid.NewGuid().ToString(), EnableResilience = true },
            configureHttpClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        var client = services.BuildServiceProvider().GetRequiredService<IDeliveryClient>();
        await client.GetItems<IDynamicElements>()
            .Where(f => f.Element("a").IsEqualTo("1").Element("b").IsEqualTo("2").Element("a").IsEqualTo("3"))
            .ExecuteAsync();

        Assert.True(handler.Queries.Count > 1, $"Expected a retry, saw {handler.Queries.Count} attempt(s).");
        Assert.Equal(handler.Queries[0], Assert.Single(handler.Queries.Distinct()));
    }

    private static async Task<string> CaptureAsync(Func<IItemsFilterBuilder, IItemsFilterBuilder> filter)
    {
        var handler = new CaptureHandler();
        var services = new ServiceCollection();
        services.AddDeliveryClient(
            new DeliveryOptions { EnvironmentId = Guid.NewGuid().ToString(), EnableResilience = false },
            configureHttpClient: builder => builder.ConfigurePrimaryHttpMessageHandler(() => handler));

        var client = services.BuildServiceProvider().GetRequiredService<IDeliveryClient>();
        await client.GetItems<IDynamicElements>().Where(filter).ExecuteAsync();

        return handler.Query ?? throw new InvalidOperationException("No request was captured.");
    }

    private const string EmptyItemsJson = """
                                          {
                                            "items": [],
                                            "pagination": { "skip": 0, "limit": 1, "count": 0, "next_page": "" },
                                            "modular_content": {}
                                          }
                                          """;

    private static HttpResponseMessage Ok() =>
        new(HttpStatusCode.OK) { Content = new StringContent(EmptyItemsJson, Encoding.UTF8, "application/json") };

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Query { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Query = request.RequestUri!.Query;
            return Task.FromResult(Ok());
        }
    }

    /// <summary>Records the query of every attempt, failing the first so the resilience pipeline retries.</summary>
    private sealed class RetryCaptureHandler : HttpMessageHandler
    {
        private int _attempts;

        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Queries.Add(request.RequestUri!.Query);

            return Task.FromResult(++_attempts == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Ok());
        }
    }
}
