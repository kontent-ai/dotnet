using System.Globalization;
using Kontent.Ai.AspNetCore.ImageTransformation;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Urls.ImageTransformation;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.AspNetCore.Tests;

public class AssetTagHelperTests
{
    private const string AssetUrl = "https://assets.example.com/folder/asset.jpg";

    [Fact]
    public async Task ProcessAsync_WithResponsiveWidthsAndMediaConditions_RendersImgWithSrcsetAndSizes()
    {
        var options = Options.Create(new ImageTransformationOptions
        {
            ResponsiveWidths = new[] { 200, 400, 800 }
        });
        var helper = new AssetTagHelper(options)
        {
            Asset = new TestAsset { Url = AssetUrl, Description = "Coffee" },
            DefaultWidth = 300
        };

        var context = CreateContext();
        var output = CreateOutput(getChildContent: async () =>
        {
            var media = new MediaConditionTagHelper { MinWidth = 769, ImageWidth = 300 };
            await media.ProcessAsync(context, CreateOutput());
        });

        await helper.ProcessAsync(context, output);

        Assert.Equal("img", output.TagName);
        Assert.Equal(TagMode.SelfClosing, output.TagMode);
        Assert.Equal($"{AssetUrl}?w=800", AttrValue(output, "src"));
        Assert.Equal("Coffee", AttrValue(output, "alt"));
        Assert.Equal("Coffee", AttrValue(output, "title"));

        var srcset = AttrValue(output, "srcset");
        Assert.Contains($"{AssetUrl}?w=200 200w", srcset);
        Assert.Contains($"{AssetUrl}?w=400 400w", srcset);
        Assert.Contains($"{AssetUrl}?w=800 800w", srcset);

        Assert.Equal("(min-width: 769px) 300px, 300px", AttrValue(output, "sizes"));
    }

    [Fact]
    public async Task ProcessAsync_WithExplicitWidth_AppliesWidthAndSkipsSrcset()
    {
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl } };
        var context = CreateContext(("width", 500));
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal($"{AssetUrl}?w=500", AttrValue(output, "src"));
        Assert.False(output.Attributes.ContainsName("srcset"));
        Assert.False(output.Attributes.ContainsName("sizes"));
    }

    [Fact]
    public async Task ProcessAsync_WithExplicitHeight_AppliesHeightAndSkipsSrcset()
    {
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl } };
        var context = CreateContext(("height", 400));
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal($"{AssetUrl}?h=400", AttrValue(output, "src"));
        Assert.False(output.Attributes.ContainsName("srcset"));
    }

    // An empty asset element is a normal state; a literal <img-asset> must not reach the page.
    [Fact]
    public async Task ProcessAsync_WithoutAsset_RendersNothing()
    {
        var helper = new AssetTagHelper();
        var context = CreateContext(("class", "hero"));
        var output = new TagHelperOutput(
            "img-asset",
            new TagHelperAttributeList { new("class", "hero") },
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));

        await helper.ProcessAsync(context, output);

        using var writer = new StringWriter();
        output.WriteTo(writer, System.Text.Encodings.Web.HtmlEncoder.Default);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public async Task ProcessAsync_TitleAttribute_OverridesAssetDescription()
    {
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = AssetUrl, Description = "Default" },
            Title = "Custom"
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal("Custom", AttrValue(output, "alt"));
        Assert.Equal("Custom", AttrValue(output, "title"));
    }

    [Fact]
    public async Task ProcessAsync_WithoutDescriptionAndTitle_UsesEmptyString()
    {
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl } };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal(string.Empty, AttrValue(output, "alt"));
        Assert.Equal(string.Empty, AttrValue(output, "title"));
    }

    [Fact]
    public async Task ProcessAsync_WithoutOptionsOrResponsiveWidths_SkipsSrcset()
    {
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl } };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal(AssetUrl, AttrValue(output, "src"));
        Assert.False(output.Attributes.ContainsName("srcset"));
    }

    [Fact]
    public async Task ProcessAsync_SrcAttribute_IsIndependentOfResponsiveWidthsOrder()
    {
        var options = Options.Create(new ImageTransformationOptions
        {
            ResponsiveWidths = new[] { 1600, 200, 800, 400 } // unordered
        });
        var helper = new AssetTagHelper(options)
        {
            Asset = new TestAsset { Url = AssetUrl }
        };

        var context = CreateContext();
        var output = CreateOutput();
        await helper.ProcessAsync(context, output);

        Assert.Equal($"{AssetUrl}?w=1600", AttrValue(output, "src"));
    }

    // The CDN never upscales: a 1000-pixel source requested at w=2000 comes back 1000 wide. A "2000w"
    // descriptor on it would make the browser derive the wrong pixel density and render it too small.
    [Fact]
    public async Task ProcessAsync_CapsSrcsetCandidatesAtTheAssetWidth()
    {
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = AssetUrl, Width = 1000 },
            ResponsiveWidths = [200, 800, 1000, 1200, 2000]
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal(
            $"{AssetUrl}?w=200 200w,{AssetUrl}?w=800 800w,{AssetUrl}?w=1000 1000w",
            AttrValue(output, "srcset"));
        Assert.Equal($"{AssetUrl}?w=1000", AttrValue(output, "src"));
    }

    // With DefaultRenditionPreset the URL already carries a rendition's query, and that rendition's
    // width - not the original's - is what the CDN can serve.
    [Fact]
    public async Task ProcessAsync_CapsSrcsetCandidatesAtTheAppliedRenditionWidth()
    {
        const string renditionQuery = "w=500&h=403&fit=clip&rect=52,0,500,403";
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset
            {
                Url = $"{AssetUrl}?{renditionQuery}",
                Width = 1000,
                Renditions = new Dictionary<string, IAssetRendition> { ["default"] = new TestRendition { Query = renditionQuery, Width = 500 } }
            },
            ResponsiveWidths = [200, 800]
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal(
            $"{AssetUrl}?w=200&h=403&fit=clip&rect=52,0,500,403 200w,{AssetUrl}?w=500&h=403&fit=clip&rect=52,0,500,403 500w",
            AttrValue(output, "srcset"));
    }

    // The rendition is recognised by its parameters, not by the query text being identical: a URL that
    // also carries an encoding parameter is still capped at the crop's width.
    [Fact]
    public async Task ProcessAsync_RecognisesTheAppliedRendition_WhenTheUrlCarriesMoreThanItsQuery()
    {
        const string renditionQuery = "w=500&h=403&fit=clip&rect=52,0,500,403";
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset
            {
                Url = $"{AssetUrl}?{renditionQuery}&fm=jpg",
                Width = 1000,
                Renditions = new Dictionary<string, IAssetRendition> { ["default"] = new TestRendition { Query = renditionQuery, Width = 500 } }
            },
            ResponsiveWidths = [200, 800]
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.DoesNotContain("800w", AttrValue(output, "srcset"));
        Assert.Contains("w=500&h=403&fit=clip&rect=52,0,500,403&fm=jpg 500w", AttrValue(output, "srcset"));
    }

    [Fact]
    public async Task ProcessAsync_WithoutAssetWidth_UsesTheConfiguredWidthsAsIs()
    {
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = AssetUrl },
            ResponsiveWidths = [200, 2000]
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Contains($"{AssetUrl}?w=2000 2000w", AttrValue(output, "srcset"));
    }

    [Fact]
    public async Task ProcessAsync_WithNonPositiveResponsiveWidth_Throws()
    {
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = AssetUrl },
            ResponsiveWidths = [200, 0]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => helper.ProcessAsync(CreateContext(), CreateOutput()));
        Assert.Contains(nameof(ImageTransformationOptions.ResponsiveWidths), exception.Message);
    }

    [Fact]
    public async Task ProcessAsync_PerTagResponsiveWidths_OverridesOptions()
    {
        var options = Options.Create(new ImageTransformationOptions
        {
            ResponsiveWidths = new[] { 100, 200 }
        });
        var helper = new AssetTagHelper(options)
        {
            Asset = new TestAsset { Url = AssetUrl },
            ResponsiveWidths = new[] { 500, 1000 }
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        var srcset = AttrValue(output, "srcset");
        Assert.Contains($"{AssetUrl}?w=500 500w", srcset);
        Assert.Contains($"{AssetUrl}?w=1000 1000w", srcset);
        Assert.DoesNotContain("w=100 100w", srcset);
        Assert.DoesNotContain("w=200 200w", srcset);
    }

    [Fact]
    public async Task ProcessAsync_WithFormat_AppendsFormatParam()
    {
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl }, Format = ImageFormat.Webp };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Contains("fm=webp", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithQuality_AppendsQualityParam()
    {
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl }, Quality = 85 };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Contains("q=85", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithFit_AppendsFitParam()
    {
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = AssetUrl },
            Fit = ImageFitMode.Crop
        };
        var context = CreateContext(("width", 500), ("height", 300));
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Contains("fit=crop", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithAutoFormat_AppendsAutoParam()
    {
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl }, AutoFormat = true };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Contains("auto=format", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithCompression_AppendsLosslessParam()
    {
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = AssetUrl },
            Compression = ImageCompression.Lossless
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Contains("lossless=true", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_EncodingParamsAppearInEverySrcsetEntry()
    {
        var options = Options.Create(new ImageTransformationOptions
        {
            ResponsiveWidths = new[] { 200, 400 }
        });
        var helper = new AssetTagHelper(options)
        {
            Asset = new TestAsset { Url = AssetUrl },
            Format = ImageFormat.Webp,
            Quality = 85
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        var srcset = AttrValue(output, "srcset");
        // Each entry carries the encoding transforms.
        Assert.Contains("w=200", srcset);
        Assert.Contains("w=400", srcset);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(srcset, "fm=webp").Count);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(srcset, "q=85").Count);
        Assert.Contains("fm=webp", AttrValue(output, "src"));
        Assert.Contains("q=85", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithValidRendition_UsesRenditionQueryForSrc()
    {
        var rendition = new TestRendition { Query = "w=500&h=403&fit=clip&rect=52,0,500,403" };
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset
            {
                Url = AssetUrl,
                Renditions = new Dictionary<string, IAssetRendition> { ["default"] = rendition }
            },
            Rendition = "default"
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal($"{AssetUrl}?w=500&h=403&fit=clip&rect=52,0,500,403", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithValidRendition_SkipsSrcsetAndSizes()
    {
        var rendition = new TestRendition { Query = "w=500&h=403&fit=clip" };
        var options = Options.Create(new ImageTransformationOptions
        {
            ResponsiveWidths = new[] { 200, 400, 800 }
        });
        var helper = new AssetTagHelper(options)
        {
            Asset = new TestAsset
            {
                Url = AssetUrl,
                Renditions = new Dictionary<string, IAssetRendition> { ["default"] = rendition }
            },
            Rendition = "default"
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.False(output.Attributes.ContainsName("srcset"));
        Assert.False(output.Attributes.ContainsName("sizes"));
    }

    [Fact]
    public async Task ProcessAsync_WithValidRendition_LayersEncodingParamsAfterRenditionQuery()
    {
        var rendition = new TestRendition { Query = "w=500&h=403&fit=clip" };
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset
            {
                Url = AssetUrl,
                Renditions = new Dictionary<string, IAssetRendition> { ["default"] = rendition }
            },
            Rendition = "default",
            Format = ImageFormat.Webp,
            AutoFormat = true
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        var src = AttrValue(output, "src");
        Assert.StartsWith($"{AssetUrl}?w=500&h=403&fit=clip", src);
        Assert.Contains("fm=webp", src);
        Assert.Contains("auto=format", src);
    }

    // DeliveryOptions.DefaultRenditionPreset puts the preset's query on Asset.Url at mapping time; a second
    // query appended to it makes the CDN drop the crop.
    [Theory]
    [InlineData("")]
    [InlineData("?w=500&h=403&fit=clip&rect=52,0,500,403")]
    [InlineData("?w=250&h=200&fit=crop")]
    public async Task ProcessAsync_WithRendition_ReplacesAnyQueryTheUrlAlreadyCarries(string existingQuery)
    {
        const string renditionQuery = "w=500&h=403&fit=clip&rect=52,0,500,403";
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset
            {
                Url = AssetUrl + existingQuery,
                Renditions = new Dictionary<string, IAssetRendition> { ["default"] = new TestRendition { Query = renditionQuery } }
            },
            Rendition = "default",
            Format = ImageFormat.Webp
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal($"{AssetUrl}?{renditionQuery}&fm=webp", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithMissingRendition_FallsBackToNormalBehavior()
    {
        var options = Options.Create(new ImageTransformationOptions
        {
            ResponsiveWidths = new[] { 200, 400 }
        });
        var helper = new AssetTagHelper(options)
        {
            Asset = new TestAsset { Url = AssetUrl }, // no renditions
            Rendition = "nonexistent"
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.True(output.Attributes.ContainsName("srcset"));
        Assert.Equal($"{AssetUrl}?w=400", AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithCustomDomainAssetUrl_PreservesCustomDomain()
    {
        // Simulates SDK configured with WithCustomAssetDomain — Asset.Url is already rewritten.
        const string customUrl = "https://cdn.example.org/975bf280/asset.jpg";
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = customUrl },
            Format = ImageFormat.Webp
        };
        var context = CreateContext(("width", 500));
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.StartsWith(customUrl, AttrValue(output, "src"));
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("auto")]
    [InlineData("calc(100% - 2rem)")]
    [InlineData("")]
    public async Task ProcessAsync_WithNonNumericWidth_RendersWithoutTheTransformation(string width)
    {
        // HTML permits these; the image API has no equivalent. They must not take the page down.
        var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl } };
        var context = CreateContext(("width", width));
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal(AssetUrl, AttrValue(output, "src"));
    }

    [Fact]
    public async Task ProcessAsync_WithFractionalWidth_ParsesInvariantlyRegardlessOfServerCulture()
    {
        // de-DE reads "." as a digit group separator, so a current-culture parse turned 1.5 into 15 -
        // while ImageUrlBuilder writes the value back invariantly. The round trip has to agree.
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var helper = new AssetTagHelper { Asset = new TestAsset { Url = AssetUrl } };
            var context = CreateContext(("width", "1.5"));
            var output = CreateOutput();

            await helper.ProcessAsync(context, output);

            Assert.Equal($"{AssetUrl}?w=1.5", AttrValue(output, "src"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // The API percent-encodes the filename. Uri.ToString() is the display form and unescapes it, so a
    // literal space would land in srcset, where a space is the delimiter between a candidate and its
    // width descriptor.
    [Fact]
    public async Task ProcessAsync_KeepsTheFilenameEncoding_InSrcAndSrcset()
    {
        const string encodedUrl = "https://assets.example.com/folder/Patient%20care%20caf%C3%A9.jpg";
        var helper = new AssetTagHelper
        {
            Asset = new TestAsset { Url = encodedUrl, Renditions = new Dictionary<string, IAssetRendition> { ["default"] = new TestRendition { Query = "w=500&h=403&fit=clip&rect=52,0,500,403" } } },
            ResponsiveWidths = [200, 400]
        };
        var context = CreateContext();
        var output = CreateOutput();

        await helper.ProcessAsync(context, output);

        Assert.Equal($"{encodedUrl}?w=200 200w,{encodedUrl}?w=400 400w", AttrValue(output, "srcset"));
        Assert.Equal($"{encodedUrl}?w=400", AttrValue(output, "src"));

        helper.Rendition = "default";
        var renditionOutput = CreateOutput();
        await helper.ProcessAsync(CreateContext(), renditionOutput);

        Assert.Equal($"{encodedUrl}?w=500&h=403&fit=clip&rect=52,0,500,403", AttrValue(renditionOutput, "src"));
    }

    private static TagHelperContext CreateContext(params (string name, object value)[] attributes)
    {
        var attrs = new TagHelperAttributeList(
            attributes.Select(a => new TagHelperAttribute(a.name, a.value)));
        return new TagHelperContext(attrs, new Dictionary<object, object?>(), Guid.NewGuid().ToString("N"));
    }

    private static TagHelperOutput CreateOutput(string tagName = "img-asset", Func<Task>? getChildContent = null)
    {
        return new TagHelperOutput(
            tagName,
            new TagHelperAttributeList(),
            async (useCachedResult, encoder) =>
            {
                if (getChildContent != null)
                {
                    await getChildContent();
                }
                return new DefaultTagHelperContent();
            });
    }

    private static string AttrValue(TagHelperOutput output, string name) =>
        output.Attributes[name].Value?.ToString() ?? string.Empty;

    private sealed class TestAsset : IAsset
    {
        public string Url { get; init; } = "";
        public string? Description { get; init; }
        public int? Height { get; init; }
        public int? Width { get; init; }
        public string Name { get; init; } = "";
        public int Size { get; init; }
        public string Type { get; init; } = "";
        public IReadOnlyDictionary<string, IAssetRendition> Renditions { get; init; }
            = new Dictionary<string, IAssetRendition>();
    }

    private sealed class TestRendition : IAssetRendition
    {
        public string RenditionId { get; init; } = "";
        public string PresetId { get; init; } = "";
        public int Width { get; init; }
        public int Height { get; init; }
        public string Query { get; init; } = "";
    }
}
