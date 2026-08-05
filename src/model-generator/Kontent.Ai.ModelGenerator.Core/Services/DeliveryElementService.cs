using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Contract;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.ModelGenerator.Core.Services;

public class DeliveryElementService : IDeliveryElementService
{
    protected readonly CodeGeneratorOptions Options;

    public DeliveryElementService(IOptions<CodeGeneratorOptions> options)
    {
        Validate(options.Value);
        Options = options.Value;
    }

    public string GetElementType(string elementType)
    {
        ArgumentNullException.ThrowIfNull(elementType);

        return elementType;
    }

    private static void Validate(CodeGeneratorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
    }
}
