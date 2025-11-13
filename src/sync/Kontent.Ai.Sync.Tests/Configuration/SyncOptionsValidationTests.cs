using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Kontent.Ai.Sync.Abstractions;
using Xunit;

namespace Kontent.Ai.Sync.Tests.Configuration;

public class SyncOptionsValidationTests
{
    [Fact]
    public void Validate_ValidOptions_ReturnsNoErrors()
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString()
        };
        var context = new ValidationContext(options);

        // Act
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().BeEmpty("valid options should have no validation errors");
    }

    [Fact]
    public void Validate_EmptyGuid_ReturnsError()
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.Empty.ToString()
        };
        var context = new ValidationContext(options);

        // Act
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().ContainSingle(r => r.MemberNames.Contains(nameof(SyncOptions.EnvironmentId)));
        results[0].ErrorMessage.Should().Contain("empty GUID");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("")]
    public void DataAnnotations_InvalidEnvironmentId_FailsValidation(string invalidEnvironmentId)
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = invalidEnvironmentId
        };

        // Act
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("invalid GUID format should fail validation");
        results.Should().Contain(r => r.MemberNames.Contains(nameof(SyncOptions.EnvironmentId)));
    }

    [Fact]
    public void Validate_PreviewMode_WithoutApiKey_ReturnsError()
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiMode = ApiMode.Preview,
            ApiKey = null
        };
        var context = new ValidationContext(options);

        // Act
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().ContainSingle(r =>
            r.MemberNames.Contains(nameof(SyncOptions.ApiKey)) &&
            r.MemberNames.Contains(nameof(SyncOptions.ApiMode)));
        results[0].ErrorMessage.Should().Contain("ApiKey is required");
        results[0].ErrorMessage.Should().Contain("Preview");
    }

    [Fact]
    public void Validate_SecureMode_WithoutApiKey_ReturnsError()
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiMode = ApiMode.Secure,
            ApiKey = null
        };
        var context = new ValidationContext(options);

        // Act
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().ContainSingle(r =>
            r.MemberNames.Contains(nameof(SyncOptions.ApiKey)) &&
            r.MemberNames.Contains(nameof(SyncOptions.ApiMode)));
        results[0].ErrorMessage.Should().Contain("ApiKey is required");
        results[0].ErrorMessage.Should().Contain("Secure");
    }

    [Fact]
    public void Validate_PreviewMode_WithApiKey_Succeeds()
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiMode = ApiMode.Preview,
            ApiKey = "test-api-key"
        };
        var context = new ValidationContext(options);

        // Act
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().BeEmpty("valid preview API configuration should pass");
    }

    [Fact]
    public void Validate_SecureMode_WithApiKey_Succeeds()
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiMode = ApiMode.Secure,
            ApiKey = "test-api-key"
        };
        var context = new ValidationContext(options);

        // Act
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().BeEmpty("valid secure API configuration should pass");
    }

    [Fact]
    public void Validate_PublicMode_DoesNotRequireApiKey()
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ApiMode = ApiMode.Public,
            ApiKey = null
        };
        var context = new ValidationContext(options);

        // Act
        var results = options.Validate(context).ToList();

        // Assert
        results.Should().BeEmpty("public production mode should not require API key");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("file:///local/path")]
    public void DataAnnotations_InvalidEndpointUrl_FailsValidation(string invalidUrl)
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ProductionEndpoint = invalidUrl
        };

        // Act
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse("invalid URL format should fail validation");
    }

    [Theory]
    [InlineData("https://deliver.kontent.ai/v2")]
    [InlineData("https://custom.endpoint.com")]
    public void DataAnnotations_ValidEndpointUrl_PassesValidation(string validUrl)
    {
        // Arrange
        var options = new SyncOptions
        {
            EnvironmentId = Guid.NewGuid().ToString(),
            ProductionEndpoint = validUrl
        };

        // Act
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);
        var isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue("valid URL format should pass validation");
    }
}
