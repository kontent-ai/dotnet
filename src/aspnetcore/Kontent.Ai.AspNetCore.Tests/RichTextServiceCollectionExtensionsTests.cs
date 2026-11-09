using Kontent.Ai.AspNetCore.RichText;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Kontent.Ai.AspNetCore.Tests;

public class RichTextServiceCollectionExtensionsTests
{
    public enum Overload
    {
        BuilderOnly,
        WithServiceProvider,
    }

    [Theory]
    [InlineData(Overload.BuilderOnly)]
    [InlineData(Overload.WithServiceProvider)]
    public void AddKontentRichText_RegistersASingleton(Overload overload)
    {
        var services = new ServiceCollection();
        Register(services, overload, () => { });

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IHtmlResolver>(), provider.GetRequiredService<IHtmlResolver>());
    }

    [Fact]
    public async Task AddKontentRichText_WithoutConfigure_ProducesBuiltInResolver()
    {
        var services = new ServiceCollection();
        services.AddKontentRichText();

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<IHtmlResolver>();

        Assert.Equal("a &lt; b", await resolver.ResolveAsync(new TextRichTextContent("a < b")));
    }

    [Theory]
    [InlineData(Overload.BuilderOnly)]
    [InlineData(Overload.WithServiceProvider)]
    public void AddKontentRichText_RunsTheCallbackOnFirstResolution(Overload overload)
    {
        var calls = 0;
        var services = new ServiceCollection();
        Register(services, overload, () => calls++);

        using var provider = services.BuildServiceProvider();
        Assert.Equal(0, calls);

        _ = provider.GetRequiredService<IHtmlResolver>();
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AddKontentRichText_WithServiceProviderConfigure_ExposesServiceProvider()
    {
        var marker = new object();
        var services = new ServiceCollection();
        services.AddSingleton(marker);

        object? resolvedFromConfigure = null;
        services.AddKontentRichText((sp, _) => resolvedFromConfigure = sp.GetRequiredService<object>());

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IHtmlResolver>();

        Assert.Same(marker, resolvedFromConfigure);
    }

    [Theory]
    [InlineData(Overload.BuilderOnly, Overload.BuilderOnly)]
    [InlineData(Overload.BuilderOnly, Overload.WithServiceProvider)]
    [InlineData(Overload.WithServiceProvider, Overload.BuilderOnly)]
    [InlineData(Overload.WithServiceProvider, Overload.WithServiceProvider)]
    public void AddKontentRichText_CalledTwice_LastWins(Overload first, Overload second)
    {
        var firstCalled = false;
        var secondCalled = false;
        var services = new ServiceCollection();
        Register(services, first, () => firstCalled = true);
        Register(services, second, () => secondCalled = true);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IHtmlResolver>();

        Assert.False(firstCalled);
        Assert.True(secondCalled);
    }

    private static void Register(IServiceCollection services, Overload overload, Action onConfigure)
    {
        if (overload == Overload.BuilderOnly)
        {
            services.AddKontentRichText(_ => onConfigure());
        }
        else
        {
            services.AddKontentRichText((_, _) => onConfigure());
        }
    }
}
