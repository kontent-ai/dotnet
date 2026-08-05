// Nothing here is hand-written except the attribute usage: both the attribute and the type provider
// come from the generator. If the analyzer were packed to the wrong path, or built against a Roslyn
// newer than this SDK's, the generator would be skipped silently and this would not compile.
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Attributes;
using Kontent.Ai.Delivery.Generated;

[ContentTypeCodename("article")]
public sealed class Article
{
    public string? Title { get; init; }
}

public static class Program
{
    public static void Main()
    {
        ITypeProvider provider = new GeneratedTypeProvider();
        var codename = provider.GetCodename(typeof(Article));

        if (codename != "article")
        {
            throw new InvalidOperationException($"generator emitted '{codename}', expected 'article'");
        }

        Console.WriteLine("sourcegen smoke: generator ran and resolved 'article'");
    }
}
