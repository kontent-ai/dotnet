using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit.Abstractions;

namespace Kontent.Ai.Delivery.Abstractions;

public class CheckNamespaces(ITestOutputHelper output)
{
    private readonly ITestOutputHelper output = output;

    /// <summary>
    /// See Kontent.Ai.Delivery.Abstractions Readme for more information.
    /// </summary>
    [Fact]
    public void AllNamespacesAreCorrect()
    {
        var abstractionTypes = Assembly.LoadFrom("Kontent.Ai.Delivery.Abstractions.dll");

        var typesToCheck = abstractionTypes
            .GetTypes()
            // Exclude compiler-generated artifacts and synthesized types
            .Where(t => t.GetCustomAttribute(typeof(CompilerGeneratedAttribute), inherit: true) is null)
            .Where(t => !t.Name.StartsWith("<>"))
            // Only consider types that actually have a namespace
            .Where(t => t.Namespace is not null)
            // Coverage instrumentation weaves a tracker type into the assembly when the build
            // collects coverage. Like the compiler-generated types above it is a build artifact,
            // not part of the shipped surface.
            .Where(t => !t.Namespace!.StartsWith("Coverlet.", StringComparison.Ordinal));

        Assert.All(
            typesToCheck,
            t => Assert.StartsWith("Kontent.Ai.Delivery.Abstractions", t.Namespace));
    }
}
