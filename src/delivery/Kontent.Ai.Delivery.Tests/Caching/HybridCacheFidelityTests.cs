using AwesomeAssertions;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Caching;
using Kontent.Ai.Delivery.ContentTypes;
using Kontent.Ai.Delivery.ContentTypes.Element;
using Kontent.Ai.Delivery.SharedModels;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.Delivery.Tests.Caching;

/// <summary>
/// The distributed tier stores a serialized copy, so a hit off it is only as good as the round trip.
/// Content type elements are the one polymorphic shape that travels through it: a serializer that writes
/// them by their declared type drops the taxonomy group and the multiple-choice options without failing,
/// and the reader gets a base element that a consumer's cast to <see cref="ITaxonomyElement"/> rejects.
/// <para>
/// Two managers over one <see cref="IDistributedCache"/> stand in for two nodes, which is what forces the
/// read to come off L2 - the writing node answers from its own memory tier and would never notice.
/// </para>
/// </summary>
public class HybridCacheFidelityTests
{
    private readonly IDistributedCache _distributedCache;

    public HybridCacheFidelityTests()
    {
        var services = new ServiceCollection();
        services.AddDistributedMemoryCache();
        _distributedCache = services.BuildServiceProvider().GetRequiredService<IDistributedCache>();
    }

    private FusionCacheManager Node() => FusionCacheManager.CreateHybrid(
        _distributedCache,
        new DeliveryCacheOptions { DefaultExpiration = TimeSpan.FromMinutes(5) });

    [Fact]
    public async Task CachedContentType_KeepsTaxonomyAndMultipleChoiceData_WhenReadByAnotherNode()
    {
        var response = TypeListing();

        using var writer = Node();
        var miss = await writer.GetOrSetAsync(
            "types_fidelity",
            _ => Task.FromResult<CacheEntry<DeliveryTypeListingResponse>?>(
                new CacheEntry<DeliveryTypeListingResponse>(response, ["types"])));

        using var reader = Node();
        var hit = await reader.GetOrSetAsync(
            "types_fidelity",
            _ => Task.FromResult<CacheEntry<DeliveryTypeListingResponse>?>(null));

        Assert.True(miss!.FromFactory);
        Assert.NotNull(hit);
        Assert.False(hit.FromFactory);

        var elements = ((IContentType)hit.Value.Types[0]).Elements;

        var taxonomy = Assert.IsAssignableFrom<ITaxonomyElement>(elements["category"]);
        Assert.Equal("categories", taxonomy.TaxonomyGroup);

        var multipleChoice = Assert.IsAssignableFrom<IMultipleChoiceElement>(elements["rating"]);
        Assert.Equal(["good", "bad"], multipleChoice.Options.Select(o => o.Codename));

        // The plain element still round-trips, including the codename the dictionary key carries.
        var text = elements["title"];
        Assert.Equal("text", text.Type);
        Assert.Equal("Title", text.Name);
        Assert.Equal("title", text.Codename);
    }

    private static DeliveryTypeListingResponse TypeListing() => new()
    {
        Pagination = new Pagination { Skip = 0, Limit = 10, Count = 1, NextPageUrl = string.Empty },
        Types =
        [
            new ContentType
            {
                System = new ContentTypeSystemAttributes
                {
                    Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    Name = "Article",
                    Codename = "article",
                    LastModified = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc)
                },
                Elements = new Dictionary<string, ContentElement>
                {
                    ["title"] = new ContentElement { Type = "text", Name = "Title", Codename = "title" },
                    ["category"] = new TaxonomyElement
                    {
                        Type = "taxonomy",
                        Name = "Category",
                        Codename = "category",
                        TaxonomyGroup = "categories"
                    },
                    ["rating"] = new MultipleChoiceElement
                    {
                        Type = "multiple_choice",
                        Name = "Rating",
                        Codename = "rating",
                        Options =
                        [
                            new MultipleChoiceOption { Name = "Good", Codename = "good" },
                            new MultipleChoiceOption { Name = "Bad", Codename = "bad" }
                        ]
                    }
                }
            }
        ]
    };

    [Fact]
    public async Task TwoEnvironmentsSharingOneCache_DoNotSeeEachOthersEntries()
    {
        // The key is built from query parameters, so "the item article" hashes the same for every
        // environment. Sharing one Redis between apps pointing at different environments then serves one
        // app the other's content - the environment has to be part of the key, not just the query.
        var options = new DeliveryCacheOptions { DefaultExpiration = TimeSpan.FromMinutes(5) };
        using var production = FusionCacheManager.CreateHybrid(_distributedCache, options, environmentId: "11111111-1111-1111-1111-111111111111");
        using var staging = FusionCacheManager.CreateHybrid(_distributedCache, options, environmentId: "22222222-2222-2222-2222-222222222222");

        var fromProduction = await production.GetOrSetAsync(
            "items:article",
            _ => Task.FromResult<CacheEntry<string>?>(new CacheEntry<string>("production copy", [])));

        var fromStaging = await staging.GetOrSetAsync(
            "items:article",
            _ => Task.FromResult<CacheEntry<string>?>(new CacheEntry<string>("staging copy", [])));

        fromProduction!.Value.Should().Be("production copy");
        fromStaging!.Value.Should().Be("staging copy");
        fromStaging.FromFactory.Should().BeTrue("staging must miss rather than read production's entry");
    }
}
