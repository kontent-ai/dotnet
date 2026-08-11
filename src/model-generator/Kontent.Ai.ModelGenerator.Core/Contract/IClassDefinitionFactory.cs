using Kontent.Ai.ModelGenerator.Core.Common;

namespace Kontent.Ai.ModelGenerator.Core.Contract;

public interface IClassDefinitionFactory
{
    ClassDefinition CreateClassDefinition(string codename);

    /// <summary>
    /// Creates a definition for an emitter that writes no codename constants, so the identifiers those
    /// constants would occupy stay available to elements.
    /// </summary>
    ClassDefinition CreateClassDefinitionWithoutCodenameConstants(string codename);
}
