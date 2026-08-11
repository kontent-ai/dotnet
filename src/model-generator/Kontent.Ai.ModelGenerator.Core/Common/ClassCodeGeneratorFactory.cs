using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Contract;
using Kontent.Ai.ModelGenerator.Core.Generators.Class;

namespace Kontent.Ai.ModelGenerator.Core.Common;

public class ClassCodeGeneratorFactory : IClassCodeGeneratorFactory
{
    public ClassCodeGenerator CreateClassCodeGenerator(
        CodeGeneratorOptions options,
        ClassDefinition classDefinition,
        string classFilename)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(classDefinition);
        ArgumentNullException.ThrowIfNull(classFilename);

        return new DeliveryClassCodeGenerator(classDefinition, classFilename, options.Namespace);
    }

    /// <inheritdoc />
    public ClassCodeGenerator CreateManagementClassCodeGenerator(
        CodeGeneratorOptions options,
        ClassDefinition classDefinition,
        string classFilename)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(classDefinition);
        ArgumentNullException.ThrowIfNull(classFilename);

        return new ManagementClassCodeGenerator(classDefinition, classFilename, options.Namespace);
    }
}
