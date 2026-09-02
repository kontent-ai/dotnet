
namespace Kontent.Ai.Delivery.Abstractions.Tests.Configuration;

public class DeliveryOptionsExtensionsTests
{
    private const string EnvironmentId = "550cec62-90a6-4ab3-b3e4-3d0bb4c04f5c";
    private const string PreviewApiKey =
        "eyJ0eXAiOiwq14X65DLCJhbGciOiJIUzI1NiJ-.eyJqdGkiOiABCjJlM2FiOTBjOGM0ODVmYjdmZTDEFRQZGM1NDIyMCIsImlhdCI6IjE1Mjg454wexiLCJleHAiOiIxODc0NDg3NjqasdfwicHJvamVjdF9pZCI6Ij" +
        "g1OTEwOTlkN2458198ewqewZjI3Yzg5M2FhZTJiNTE4IiwidmVyIjoiMS4wLjAiLCJhdWQiewqgsdaWV3LmRlbGl2ZXIua2VudGljb2Nsb3VkLmNvbSJ9._tSzbNDpbE55dsaLUTGsdgesg4b693TFuhRCRsDyoc";
    private const string SecureAccessApiKey = "secure.api.key";

    [Fact]
    public void UseProductionApi_ClearsPreviewAndSecureAccess()
    {
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId }.UsePreviewApi(PreviewApiKey);

        var returned = options.UseProductionApi();

        Assert.Same(options, returned);
        Assert.False(options.UsePreviewApi);
        Assert.False(options.UseSecureAccess);
    }

    [Fact]
    public void UseProductionApi_WithSecureAccessKey_EnablesSecureAccess()
    {
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId };

        var returned = options.UseProductionApi(SecureAccessApiKey);

        Assert.Same(options, returned);
        Assert.False(options.UsePreviewApi);
        Assert.True(options.UseSecureAccess);
        Assert.Equal(SecureAccessApiKey, options.SecureAccessApiKey);
    }

    [Fact]
    public void UsePreviewApi_EnablesPreviewAndClearsSecureAccess()
    {
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId }.UseProductionApi(SecureAccessApiKey);

        var returned = options.UsePreviewApi(PreviewApiKey);

        Assert.Same(options, returned);
        Assert.True(options.UsePreviewApi);
        Assert.False(options.UseSecureAccess);
        Assert.Equal(PreviewApiKey, options.PreviewApiKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UsePreviewApi_WithoutKey_ThrowsArgumentException(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DeliveryOptions().UsePreviewApi(key!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseProductionApi_WithoutSecureAccessKey_ThrowsArgumentException(string? key)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DeliveryOptions().UseProductionApi(key!));
    }

    [Fact]
    public void UseCustomEndpoint_String_SetsBothEndpoints()
    {
        const string customEndpoint = "https://www.customProductionEndpoint.com";
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId };

        var returned = options.UseCustomEndpoint(customEndpoint);

        Assert.Same(options, returned);
        Assert.Equal(customEndpoint, options.ProductionEndpoint);
        Assert.Equal(customEndpoint, options.PreviewEndpoint);
    }

    [Fact]
    public void UseCustomEndpoint_Uri_SetsBothEndpoints()
    {
        const string customEndpoint = "https://www.customproductionendpoint.com/";
        var options = new DeliveryOptions { EnvironmentId = EnvironmentId };

        options.UseCustomEndpoint(new Uri(customEndpoint, UriKind.Absolute));

        Assert.Equal(customEndpoint, options.ProductionEndpoint);
        Assert.Equal(customEndpoint, options.PreviewEndpoint);
    }

    [Fact]
    public void UseCustomEndpoint_BeforeApiModeSwitch_AppliesDeterministically()
    {
        const string customEndpoint = "https://custom.kontent.ai";

        var options = new DeliveryOptions { EnvironmentId = EnvironmentId }
            .UseCustomEndpoint(customEndpoint)
            .UsePreviewApi(PreviewApiKey);

        Assert.Equal(customEndpoint, options.ProductionEndpoint);
        Assert.Equal(customEndpoint, options.PreviewEndpoint);
        Assert.Equal(customEndpoint, options.GetBaseUrl());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseCustomEndpoint_WithoutEndpoint_ThrowsArgumentException(string? endpoint)
    {
        Assert.ThrowsAny<ArgumentException>(() => new DeliveryOptions().UseCustomEndpoint(endpoint!));
    }

    [Fact]
    public void UseCustomEndpoint_NullUri_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DeliveryOptions().UseCustomEndpoint((Uri)null!));
    }

    [Fact]
    public void Extensions_OnNullOptions_ThrowArgumentNullException()
    {
        DeliveryOptions options = null!;

        Assert.Throws<ArgumentNullException>(() => options.UseProductionApi());
        Assert.Throws<ArgumentNullException>(() => options.UseProductionApi(SecureAccessApiKey));
        Assert.Throws<ArgumentNullException>(() => options.UsePreviewApi(PreviewApiKey));
        Assert.Throws<ArgumentNullException>(() => options.UseCustomEndpoint("https://custom.kontent.ai"));
    }

    [Fact]
    public void NewOptions_HaveNoCustomAssetDomain()
    {
        Assert.Null(new DeliveryOptions().CustomAssetDomain);
    }
}
