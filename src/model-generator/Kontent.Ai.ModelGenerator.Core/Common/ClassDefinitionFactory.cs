namespace Kontent.Ai.ModelGenerator.Core.Common;

public static class ClassDefinitionFactory
{
    public static ClassDefinition CreateClassDefinition(string codename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codename);

        return new ClassDefinition(codename);
    }

    /// <summary>
    /// Creates a definition for an emitter that writes no codename constants, so the identifiers those
    /// constants would occupy stay available to elements.
    /// </summary>
    public static ClassDefinition CreateClassDefinitionWithoutCodenameConstants(string codename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codename);

        return new ClassDefinition(codename, emitsCodenameConstants: false);
    }
}
