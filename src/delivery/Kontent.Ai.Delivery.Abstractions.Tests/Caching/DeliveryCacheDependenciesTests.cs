namespace Kontent.Ai.Delivery.Abstractions.Tests.Caching;

public class DeliveryCacheDependenciesTests
{
    [Fact]
    public void ForItem_ComposesTheKeyTheSdkTagsWith()
        => Assert.Equal("item_article", DeliveryCacheDependencies.ForItem("article"));

    [Fact]
    public void ForType_ComposesTheKeyTheSdkTagsWith()
        => Assert.Equal("type_article", DeliveryCacheDependencies.ForType("article"));

    [Fact]
    public void ForTaxonomy_ComposesTheKeyTheSdkTagsWith()
        => Assert.Equal("taxonomy_categories", DeliveryCacheDependencies.ForTaxonomy("categories"));

    [Fact]
    public void ForAsset_ComposesTheKeyTheSdkTagsWith()
    {
        var id = Guid.Parse("A5E1C4B2-1234-5678-9ABC-DEF012345678");

        Assert.Equal("asset_a5e1c4b2-1234-5678-9abc-def012345678", DeliveryCacheDependencies.ForAsset(id));
    }

    [Theory]
    [InlineData("Article", "item_article")]
    [InlineData("  article  ", "item_article")]
    [InlineData("ARTICLE", "item_article")]
    public void ForItem_NormalizesTheCodename(string codename, string expected)
        => Assert.Equal(expected, DeliveryCacheDependencies.ForItem(codename));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForItem_RejectsAMissingCodename(string? codename)
        => Assert.ThrowsAny<ArgumentException>(() => DeliveryCacheDependencies.ForItem(codename!));
}
