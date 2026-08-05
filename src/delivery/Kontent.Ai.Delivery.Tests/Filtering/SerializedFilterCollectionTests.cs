using Kontent.Ai.Delivery.Api.Filtering;

namespace Kontent.Ai.Delivery.Tests.Filtering;

public class SerializedFilterCollectionTests
{
    [Fact]
    public void SerializedFilterCollection_PreservesDuplicateKeyEntries()
    {
        var filters = new SerializedFilterCollection
        {
            new KeyValuePair<string, string>("system.type[in]", "article"),
            new KeyValuePair<string, string>("system.type[in]", "article,blog_post")
        };

        Assert.Equal(2, filters.Count);
        Assert.Equal("system.type[in]", filters[0].Key);
        Assert.Equal("article", filters[0].Value);
        Assert.Equal("system.type[in]", filters[1].Key);
        Assert.Equal("article,blog_post", filters[1].Value);
    }

    [Fact]
    public void Render_EmptyCollection_ReturnsNull()
        => Assert.Null(FilterQueryString.Render(new SerializedFilterCollection()));

    [Fact]
    public void Render_EmitsPairsInDeclarationOrder()
    {
        var filters = new SerializedFilterCollection
        {
            new KeyValuePair<string, string>("elements.a[eq]", "1"),
            new KeyValuePair<string, string>("elements.b[eq]", "2"),
            new KeyValuePair<string, string>("elements.a[eq]", "3")
        };

        // Repeated keys stay where they were written rather than being pulled together.
        Assert.Equal("elements.a%5Beq%5D=1&elements.b%5Beq%5D=2&elements.a%5Beq%5D=3", FilterQueryString.Render(filters));
    }

    [Fact]
    public void Render_PreservesKeyCasing()
    {
        var filters = new SerializedFilterCollection
        {
            new KeyValuePair<string, string>("system.type[eq]", "article"),
            new KeyValuePair<string, string>("System.Type[eq]", "blog_post")
        };

        // Keys are sent exactly as the caller wrote them. The previous implementation grouped keys
        // case-insensitively, so a differently-cased codename was silently re-cased to whichever
        // spelling appeared first. Codenames are lowercase by convention, so this only shows up when
        // a caller already got one wrong — and surfacing that is better than quietly rewriting it.
        Assert.Equal("system.type%5Beq%5D=article&System.Type%5Beq%5D=blog_post", FilterQueryString.Render(filters));
    }

    [Fact]
    public void Render_EscapesKeyAndValue()
    {
        var filters = new SerializedFilterCollection
        {
            new KeyValuePair<string, string>("elements.title[eq]", "Hello & World")
        };

        Assert.Equal("elements.title%5Beq%5D=Hello%20%26%20World", FilterQueryString.Render(filters));
    }
}
