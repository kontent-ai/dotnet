namespace Kontent.Ai.ModelGenerator.Tests;

public class ProgramTests
{
    private const string EnvironmentId = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public async Task CreateCodeGeneratorOptions_NoEnvironmentId_ReturnsError()
    {
        var result = await Program.Main(Array.Empty<string>());
        result.Should().Be(1);
    }

    // Both of these used to be accepted: the argument that names the mode was dropped in binding, so the
    // run continued in the other mode. The first generated a full set of Delivery models over the output
    // directory and exited 0, which is a successful-looking run that wrote the wrong files.
    [Fact]
    public async Task Main_ManagementArgWithoutTheModeSwitch_FailsRatherThanGeneratingDeliveryModels()
    {
        var result = await Program.Main(["-i", EnvironmentId, "-k", "some-key"]);

        result.Should().Be(1);
    }

    [Fact]
    public async Task Main_DeliveryArgInManagementMode_FailsRatherThanDroppingIt()
    {
        var result = await Program.Main(["-m", "-p", EnvironmentId, "-k", "some-key"]);

        result.Should().Be(1);
    }
}
