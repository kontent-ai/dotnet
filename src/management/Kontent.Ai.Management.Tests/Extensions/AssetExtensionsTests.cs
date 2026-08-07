using AwesomeAssertions;
using Kontent.Ai.Management.Extensions;
using Kontent.Ai.Management.Models.AssetFolders;

namespace Kontent.Ai.Management.Tests.Extensions;

public class AssetExtensionsTests
{
    // A three-level tree with two branches, so a match in the second branch only turns up if the search
    // keeps going after the first branch comes back empty.
    private static readonly Guid MarketingId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CampaignsId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SpringId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ProductId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static IReadOnlyList<AssetFolderHierarchy> Tree() =>
    [
        new()
        {
            Id = MarketingId, Name = "Marketing", Codename = "marketing", ExternalId = "ext-marketing",
            Folders =
            [
                new()
                {
                    Id = CampaignsId, Name = "Campaigns", Codename = "campaigns", ExternalId = "ext-campaigns",
                    Folders =
                    [
                        new() { Id = SpringId, Name = "Spring", Codename = "spring", ExternalId = "ext-spring" },
                    ],
                },
            ],
        },
        new() { Id = ProductId, Name = "Product", Codename = "product", ExternalId = "ext-product" },
    ];

    [Fact]
    public void GetFolderHierarchyById_FindsANestedFolder() =>
        Tree().GetFolderHierarchyById(SpringId)!.Name.Should().Be("Spring");

    [Fact]
    public void GetFolderHierarchyById_UnknownId_ReturnsNull() =>
        Tree().GetFolderHierarchyById(Guid.NewGuid()).Should().BeNull();

    [Fact]
    public void GetFolderHierarchyByCodename_FindsANestedFolder() =>
        Tree().GetFolderHierarchyByCodename("campaigns")!.Id.Should().Be(CampaignsId);

    [Fact]
    public void GetFolderHierarchyByExternalId_FindsANestedFolder() =>
        Tree().GetFolderHierarchyByExternalId("ext-spring")!.Id.Should().Be(SpringId);

    [Fact]
    public void GetFolderHierarchy_SearchesLaterSiblingsAfterAnEmptyBranch()
    {
        // "Product" sits after a branch that is searched to its full depth and yields nothing.
        Tree().GetFolderHierarchyById(ProductId)!.Name.Should().Be("Product");
    }

    [Fact]
    public void GetFolderHierarchyById_NullFolders_Throws()
    {
        var act = () => ((IEnumerable<AssetFolderHierarchy>)null!).GetFolderHierarchyById(Guid.NewGuid());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetParentLinkedFolderHierarchy_LinksEachFolderToItsParent()
    {
        var linked = Tree().GetParentLinkedFolderHierarchy();

        var marketing = linked.Single(f => f.Id == MarketingId);
        marketing.Parent.Should().BeNull();

        var campaigns = marketing.Folders!.Single();
        campaigns.Parent.Should().BeSameAs(marketing);

        var spring = campaigns.Folders!.Single();
        spring.Parent.Should().BeSameAs(campaigns);
        spring.Folders.Should().BeNull();
    }

    [Fact]
    public void GetParentLinkedFolderHierarchy_CarriesEveryIdentifierAcross()
    {
        var product = Tree().GetParentLinkedFolderHierarchy().Single(f => f.Id == ProductId);

        product.Name.Should().Be("Product");
        product.Codename.Should().Be("product");
        product.ExternalId.Should().Be("ext-product");
    }

    [Fact]
    public void GetFullFolderPath_JoinsTheAncestryWithBackslashes()
    {
        var linked = Tree().GetParentLinkedFolderHierarchy();
        var spring = linked.Single(f => f.Id == MarketingId).Folders!.Single().Folders!.Single();

        spring.GetFullFolderPath().Should().Be(@"Marketing\Campaigns\Spring");
    }

    [Fact]
    public void GetFullFolderPath_RootFolder_IsJustItsName() =>
        Tree().GetParentLinkedFolderHierarchy().Single(f => f.Id == ProductId)
            .GetFullFolderPath().Should().Be("Product");

    [Fact]
    public void GetFullFolderPath_UnnamedAncestor_ContributesNoSegment()
    {
        // Name is required on the wire model, but the linking hierarchy is a plain class a caller can
        // build by hand, so an empty name must not leave a stray separator.
        var parent = new AssetFolderLinkingHierarchy { Name = "" };
        var child = new AssetFolderLinkingHierarchy { Name = "Child", Parent = parent };

        child.GetFullFolderPath().Should().Be("Child");
    }

    [Fact]
    public void GetParentLinkedFolderHierarchyById_FindsANestedFolder() =>
        Tree().GetParentLinkedFolderHierarchy()
            .GetParentLinkedFolderHierarchyById(SpringId)!.Name.Should().Be("Spring");

    [Fact]
    public void GetParentLinkedFolderHierarchyByCodename_FindsANestedFolder() =>
        Tree().GetParentLinkedFolderHierarchy()
            .GetParentLinkedFolderHierarchyByCodename("campaigns")!.Id.Should().Be(CampaignsId);

    [Fact]
    public void GetParentLinkedFolderHierarchyByExternalId_FindsANestedFolder() =>
        Tree().GetParentLinkedFolderHierarchy()
            .GetParentLinkedFolderHierarchyByExternalId("ext-marketing")!.Id.Should().Be(MarketingId);

    [Fact]
    public void GetParentLinkedFolderHierarchyById_UnknownId_ReturnsNull() =>
        Tree().GetParentLinkedFolderHierarchy()
            .GetParentLinkedFolderHierarchyById(Guid.NewGuid()).Should().BeNull();

    [Fact]
    public void GetParentLinkedFolderHierarchy_EmptyInput_ReturnsEmpty() =>
        Array.Empty<AssetFolderHierarchy>().GetParentLinkedFolderHierarchy().Should().BeEmpty();

    [Fact]
    public void NullGuards_ThrowArgumentNullException()
    {
        var linked = () => ((IEnumerable<AssetFolderHierarchy>)null!).GetParentLinkedFolderHierarchy();
        var byCodename = () => ((IEnumerable<AssetFolderHierarchy>)null!).GetFolderHierarchyByCodename("x");
        var byExternalId = () => ((IEnumerable<AssetFolderHierarchy>)null!).GetFolderHierarchyByExternalId("x");
        var linkedById = () => ((IEnumerable<AssetFolderLinkingHierarchy>)null!).GetParentLinkedFolderHierarchyById(Guid.NewGuid());
        var linkedByCodename = () => ((IEnumerable<AssetFolderLinkingHierarchy>)null!).GetParentLinkedFolderHierarchyByCodename("x");
        var linkedByExternalId = () => ((IEnumerable<AssetFolderLinkingHierarchy>)null!).GetParentLinkedFolderHierarchyByExternalId("x");
        var path = () => ((AssetFolderLinkingHierarchy)null!).GetFullFolderPath();

        linked.Should().Throw<ArgumentNullException>();
        byCodename.Should().Throw<ArgumentNullException>();
        byExternalId.Should().Throw<ArgumentNullException>();
        linkedById.Should().Throw<ArgumentNullException>();
        linkedByCodename.Should().Throw<ArgumentNullException>();
        linkedByExternalId.Should().Throw<ArgumentNullException>();
        path.Should().Throw<ArgumentNullException>();
    }
}
