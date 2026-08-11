using Kontent.Ai.ModelGenerator.Core.Common;
using Kontent.Ai.ModelGenerator.Core.Configuration;
using Kontent.Ai.ModelGenerator.Core.Generators.Class;

namespace Kontent.Ai.ModelGenerator.Core.Contract;

public interface IClassCodeGeneratorFactory
{
    /// <summary>
    /// Creates the generator that emits Delivery models.
    /// </summary>
    ClassCodeGenerator CreateClassCodeGenerator(
        CodeGeneratorOptions options,
        ClassDefinition classDefinition,
        string classFilename);

    /// <summary>
    /// Creates the generator that emits Management models.
    /// </summary>
    /// <remarks>
    /// Its own method rather than a mode flag on the one above: the two emitters differ in what they write,
    /// not in a setting, and this interface previously offered only the Delivery one while the Management
    /// path constructed its generator directly - a seam that looked like the way in and was not.
    /// </remarks>
    ClassCodeGenerator CreateManagementClassCodeGenerator(
        CodeGeneratorOptions options,
        ClassDefinition classDefinition,
        string classFilename);
}
