using System.Net;
using Kontent.Ai.AspNetCore.ImageTransformation;
using Kontent.Ai.Delivery.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kontent.Ai.AspNetCore.Tests;

/// <summary>
/// Real Razor views, compiled with the project: a documented attribute form that stops compiling fails the build.
/// </summary>
public class CompiledViewTests
{
    [Fact]
    public async Task DocumentedAttributes_ReachTheTransformationUrl()
    {
        var html = await RenderAsync("DocumentedAttributes");

        Assert.Contains("fm=webp", html);
        Assert.Contains("q=85", html);
        Assert.Contains("fit=crop", html);
        Assert.Contains("auto=format", html);
        Assert.Contains("lossless=true", html);
        Assert.Contains("sizes=\"(min-width: 769px) 300px, (max-width: 768px) and (min-width: 330px) 689px, 300px\"", html);
    }

    // Razor still runs the child tag helpers when the parent never asks for them, so media-condition sees no sizes list.
    [Fact]
    public async Task MediaCondition_WithoutSrcset_RendersOnlyTheImage()
    {
        var html = await RenderAsync("MediaConditionWithoutSrcset");

        Assert.Contains("w=300", html);
        Assert.DoesNotContain("sizes=", html);
        Assert.DoesNotContain("media-condition", html);
    }

    private static async Task<string> RenderAsync(string view)
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddControllersWithViews();
                    services.Configure<ImageTransformationOptions>(options => options.ResponsiveWidths = [200, 400]);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapDefaultControllerRoute());
                }))
            .StartAsync();

        var response = await host.GetTestClient().GetAsync($"/Images/Show?view={view}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}

public sealed class ImagesController : Controller
{
    public IActionResult Show(string view) => View(view, new ViewAsset());
}

public sealed class ViewAsset : IAsset
{
    public string Url => "https://assets.example.com/folder/asset.jpg";
    public string? Description => "Coffee";
    public int? Height => null;
    public int? Width => null;
    public string Name => "asset.jpg";
    public int Size => 0;
    public string Type => "image/jpeg";
    public IReadOnlyDictionary<string, IAssetRendition> Renditions { get; } = new Dictionary<string, IAssetRendition>();
}
