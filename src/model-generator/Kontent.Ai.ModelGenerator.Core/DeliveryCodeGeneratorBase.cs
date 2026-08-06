using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Contract;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.ModelGenerator.Core;

public abstract class DeliveryCodeGeneratorBase(
    IOptions<CodeGeneratorOptions> options,
    IOutputProvider outputProvider,
    IClassCodeGeneratorFactory classCodeGeneratorFactory,
    IClassDefinitionFactory classDefinitionFactory,
    IDeliveryElementService deliveryElementService,
    IUserMessageLogger logger) : CodeGeneratorBase(options, outputProvider, classCodeGeneratorFactory, classDefinitionFactory, logger)
{
    protected readonly IDeliveryElementService DeliveryElementService = deliveryElementService;
}
