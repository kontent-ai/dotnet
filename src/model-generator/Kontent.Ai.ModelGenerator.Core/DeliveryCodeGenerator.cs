using System.Net;
using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Contract;
using Kontent.Ai.ModelGenerator.Core.Generators.Class;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.ModelGenerator.Core;

public class DeliveryCodeGenerator : CodeGeneratorBase
{
    private readonly IDeliveryClient _deliveryClient;

    public DeliveryCodeGenerator(
        IOptions<CodeGeneratorOptions> options,
        IOutputProvider outputProvider,
        IDeliveryClient deliveryClient,
        IClassCodeGeneratorFactory classCodeGeneratorFactory,
        IUserMessageLogger logger)
        : base(options, outputProvider, classCodeGeneratorFactory, logger)
    {
        _deliveryClient = deliveryClient;
    }

    protected override async Task<ICollection<ClassCodeGenerator>> GetClassCodeGenerators()
    {
        var contentTypes = UnwrapListing(await _deliveryClient.GetTypes().ExecuteAsync());

        var codeGenerators = new List<ClassCodeGenerator>();
        foreach (var contentType in contentTypes)
        {
            try
            {
                codeGenerators.Add(GetClassCodeGenerator(contentType));
            }
            catch (InvalidIdentifierException)
            {
                WriteConsoleErrorMessage(contentType.System.Codename);
            }
        }

        return codeGenerators;
    }

    /// <summary>
    /// Reads the content types out of an <see cref="IDeliveryResult{T}"/>. The Delivery client never throws
    /// on API errors — it surfaces them via <see cref="IDeliveryResult{T}.IsSuccess"/> — so a failed listing is
    /// turned into an exception here to abort generation with a readable message. Returning an empty list
    /// instead would report the environment as having no content types and exit successfully.
    /// </summary>
    private static IReadOnlyList<IContentType> UnwrapListing(IDeliveryResult<IDeliveryTypeListingResponse> result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(DescribeFailure(result));
        }

        // A success result with a null body shouldn't happen, but guard so a downstream
        // NullReferenceException can't mask it — abort with the same readable message instead.
        return result.Value?.Types ?? throw new InvalidOperationException(
            "The Delivery API reported success but returned no content types.");
    }

    private static string DescribeFailure(IDeliveryResult<IDeliveryTypeListingResponse> result)
    {
        // The API's own 401 text explains how to form an Authorization header, which is not the mistake
        // someone typing a command made. Every other status passes its message through. The API answers
        // 401 for a missing key as well as a wrong one, and -k/--apiKey is a Management-mode argument.
        var reason = result.StatusCode == HttpStatusCode.Unauthorized
            ? "the API key is missing or was rejected. Pass --DeliveryOptions:UseSecureAccess true with "
              + "--DeliveryOptions:SecureAccessApiKey <key>, or --DeliveryOptions:UsePreviewApi true with "
              + "--DeliveryOptions:PreviewApiKey <key>."
            : result.Error?.Message ?? "unknown error.";

        var requestId = result.Error?.RequestId;
        var trailer = string.IsNullOrWhiteSpace(requestId) ? string.Empty : $" Request ID: {requestId}.";

        return $"Failed to list content types from the Delivery API ({(int)result.StatusCode}): {reason}{trailer}";
    }

    internal ClassCodeGenerator GetClassCodeGenerator(IContentType contentType)
    {
        var classDefinition = ClassDefinitionFactory.CreateClassDefinition(contentType.System.Codename);

        foreach (var element in contentType.Elements)
        {
            try
            {
                var property = Property.FromContentTypeElement(element.Key, element.Value.Type, Options.Nullability);
                classDefinition.AddProperty(property);
            }
            catch (Exception e)
            {
                WriteConsoleErrorMessage(e, element.Key, element.Value.Type, classDefinition.ClassName);
            }
        }

        var classFilename = classDefinition.ClassName;

        return ClassCodeGeneratorFactory.CreateClassCodeGenerator(Options, classDefinition, classFilename);
    }
}
