using System.ComponentModel.DataAnnotations;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Management.Configuration;
using Kontent.Ai.ModelGenerator.Core.Configuration;

namespace Kontent.Ai.ModelGenerator.CommandLine;

/// <summary>
/// Validates the options the generator was started with, before any API call is attempted.
/// </summary>
/// <remarks>
/// The SDKs already describe what a valid configuration looks like, through data annotations and
/// <see cref="IValidatableObject"/> on their own options types. Their DI registration runs that through
/// <c>ValidateOnStart</c>, which only fires under a <c>Host</c> - this tool builds a bare
/// <c>ServiceProvider</c>, so nothing would run it here. Calling <see cref="Validator"/> directly is the
/// host-independent equivalent, and is what the SDKs' own container-free constructors do. Re-deriving
/// the rules by hand is how the tool came to accept an environment id that was not a GUID, only to fail
/// later against the API.
/// </remarks>
public static class ValidationExtensions
{
    /// <summary>
    /// Validates that <see cref="CodeGeneratorOptions"/> are initialized for the Delivery SDK.
    /// </summary>
    /// <param name="codeGeneratorOptions">The options the generator was started with.</param>
    /// <exception cref="InvalidOperationException">The options are missing or invalid.</exception>
    public static void Validate(this CodeGeneratorOptions codeGeneratorOptions)
    {
        ArgumentNullException.ThrowIfNull(codeGeneratorOptions);

        if (codeGeneratorOptions.DeliveryOptions is null)
        {
            throw MissingEnvironmentId(nameof(DeliveryOptions.EnvironmentId));
        }

        ValidateOptions(codeGeneratorOptions.DeliveryOptions, "delivery");
    }

    /// <summary>
    /// Validates that <see cref="CodeGeneratorOptions"/> are initialized for the Management SDK.
    /// Both <c>EnvironmentId</c> and <c>ApiKey</c> are required.
    /// </summary>
    /// <param name="codeGeneratorOptions">The options the generator was started with.</param>
    /// <exception cref="InvalidOperationException">The options are missing or invalid.</exception>
    public static void ValidateManagement(this CodeGeneratorOptions codeGeneratorOptions)
    {
        ArgumentNullException.ThrowIfNull(codeGeneratorOptions);

        if (codeGeneratorOptions.ManagementOptions is null)
        {
            throw MissingEnvironmentId(nameof(ManagementOptions.EnvironmentId));
        }

        ValidateOptions(codeGeneratorOptions.ManagementOptions, "management");
    }

    private static InvalidOperationException MissingEnvironmentId(string memberName) =>
        new($"You have to provide the '{memberName}' argument. " +
            "See http://bit.ly/k-params for more details on configuration.");

    /// <summary>
    /// Reports every problem at once rather than the first, so a run started with several bad arguments
    /// does not have to be repeated once per mistake.
    /// </summary>
    private static void ValidateOptions(object options, string mode)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
        {
            return;
        }

        var problems = results.Select(result =>
        {
            var member = result.MemberNames.FirstOrDefault();
            return member is null ? $"  - {result.ErrorMessage}" : $"  - {member}: {result.ErrorMessage}";
        });

        throw new InvalidOperationException(
            $"The {mode} configuration is not valid:{Environment.NewLine}" +
            string.Join(Environment.NewLine, problems) + Environment.NewLine +
            "See http://bit.ly/k-params for more details on configuration.");
    }
}
