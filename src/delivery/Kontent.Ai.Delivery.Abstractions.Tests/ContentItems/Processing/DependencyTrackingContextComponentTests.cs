namespace Kontent.Ai.Delivery.Abstractions.Tests.ContentItems.Processing;

public class DependencyTrackingContextComponentTests
{
    [Theory]
    // Generated component codenames. Tracking these is harmless: a webhook names the owning item, so the
    // key is never matched, and the caller filters them out where the response says which is which.
    [InlineData("n27ec1626_93ac_0129_64e5_1beeda45416c")]
    [InlineData("d7610e80_9a93_01ef_284c_c1dfdbcf43ee")]
    // Authored codenames that take the same shape. Nothing here may drop them.
    [InlineData("product_sku_0123_blue")]
    [InlineData("release_notes_0101_draft")]
    [InlineData("coffee_beverages_explained")]
    [InlineData("n27ec1626_93ac_4629_64e5_1beeda45416c")]
    public void TrackItem_RecordsEveryCodename(string key)
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackItem(key);

        Assert.Contains($"item_{key}", ctx.Dependencies);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TrackItem_NullOrWhitespace_DoesNotRecord(string? codename)
    {
        var ctx = new DependencyTrackingContext();
        ctx.TrackItem(codename);
        Assert.Empty(ctx.Dependencies);
    }

    [Theory]
    [InlineData("article")]
    [InlineData("Article")]
    [InlineData("blog_post")]
    public void TrackItemType_ValidCodename_RecordsTypeDependency(string typeCodename)
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackItemType(typeCodename);

        Assert.Single(ctx.Dependencies);
        Assert.Contains($"type_{typeCodename.ToLowerInvariant()}", ctx.Dependencies);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void TrackItemType_NullOrWhitespace_DoesNotRecord(string? typeCodename)
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackItemType(typeCodename);

        Assert.Empty(ctx.Dependencies);
    }

    [Fact]
    public void TrackItemType_DuplicateCalls_RecordsOnce()
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackItemType("article");
        ctx.TrackItemType("article");
        ctx.TrackItemType("ARTICLE");

        Assert.Single(ctx.Dependencies);
    }

    [Fact]
    public void TrackAsset_RecordsAssetDependency()
    {
        var assetId = Guid.NewGuid();
        var ctx = new DependencyTrackingContext();

        ctx.TrackAsset(assetId);

        Assert.Single(ctx.Dependencies);
        Assert.Contains($"asset_{assetId}", ctx.Dependencies);
    }

    [Fact]
    public void TrackAsset_EmptyGuid_StillRecords()
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackAsset(Guid.Empty);

        Assert.Single(ctx.Dependencies);
        Assert.Contains($"asset_{Guid.Empty}", ctx.Dependencies);
    }

    [Fact]
    public void TrackAsset_DuplicateCalls_RecordsOnce()
    {
        var assetId = Guid.NewGuid();
        var ctx = new DependencyTrackingContext();

        ctx.TrackAsset(assetId);
        ctx.TrackAsset(assetId);

        Assert.Single(ctx.Dependencies);
    }

    [Theory]
    [InlineData("personas")]
    [InlineData("manufacturer")]
    public void TrackTaxonomy_ValidGroup_RecordsDependency(string taxonomyGroup)
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackTaxonomy(taxonomyGroup);

        Assert.Single(ctx.Dependencies);
        Assert.Contains($"taxonomy_{taxonomyGroup}", ctx.Dependencies);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TrackTaxonomy_NullOrWhitespace_DoesNotRecord(string? taxonomyGroup)
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackTaxonomy(taxonomyGroup);

        Assert.Empty(ctx.Dependencies);
    }

    [Fact]
    public void TrackTaxonomy_DuplicateCalls_RecordsOnce()
    {
        var ctx = new DependencyTrackingContext();

        ctx.TrackTaxonomy("personas");
        ctx.TrackTaxonomy("personas");

        Assert.Single(ctx.Dependencies);
    }

    [Fact]
    public void Dependencies_ReturnsSnapshotNotLiveReference()
    {
        var ctx = new DependencyTrackingContext();
        ctx.TrackItem("first_item");

        // Take a snapshot
        var snapshot = ctx.Dependencies.ToList();

        // Mutate the context after taking the snapshot
        ctx.TrackItem("second_item");

        // The snapshot should not include the second item
        Assert.Single(snapshot);
        Assert.Contains("item_first_item", snapshot);
        Assert.DoesNotContain("item_second_item", snapshot);

        // But the live dependencies should have both
        var liveSnapshot = ctx.Dependencies.ToList();
        Assert.Equal(2, liveSnapshot.Count);
    }

    [Fact]
    public void Dependencies_EmptyContext_ReturnsEmptyCollection()
    {
        var ctx = new DependencyTrackingContext();

        var deps = ctx.Dependencies;

        Assert.Empty(deps);
    }

    [Fact]
    public void MixedTracking_AllTypesRecorded()
    {
        var assetId = Guid.NewGuid();
        var ctx = new DependencyTrackingContext();

        ctx.TrackItem("my_article");
        ctx.TrackItemType("article");
        ctx.TrackAsset(assetId);
        ctx.TrackTaxonomy("personas");

        var deps = ctx.Dependencies.ToList();
        Assert.Equal(4, deps.Count);
        Assert.Contains("item_my_article", deps);
        Assert.Contains("type_article", deps);
        Assert.Contains($"asset_{assetId}", deps);
        Assert.Contains("taxonomy_personas", deps);
    }
}

