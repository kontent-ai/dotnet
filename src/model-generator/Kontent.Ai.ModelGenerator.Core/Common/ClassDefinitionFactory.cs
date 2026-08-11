using Kontent.Ai.ModelGenerator.Core.Contract;

namespace Kontent.Ai.ModelGenerator.Core.Common;

public class ClassDefinitionFactory : IClassDefinitionFactory
{
    public ClassDefinition CreateClassDefinition(string codename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codename);

        return new ClassDefinition(codename);
    }

    /// <inheritdoc />
    public ClassDefinition CreateClassDefinitionWithoutCodenameConstants(string codename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codename);

        return new ClassDefinition(codename, emitsCodenameConstants: false);
    }
}
