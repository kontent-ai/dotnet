// Shared test source, compiled into each test assembly - see src/testing/README.md.

using System.ComponentModel.DataAnnotations;
using AwesomeAssertions;
using Kontent.Ai.Common;
using Kontent.Ai.Common.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Kontent.Ai.Testing.Clients;

/// <summary>
/// Pins the registration sequence's contract in every product that compiles <see cref="ClientRegistration"/>:
/// the name and duplicate checks, options validated on read, the unnamed mirror of the default client's options
/// and the reload that reaches it, startup validation reporting a failure once, the factory registered once and
/// the default alias. The transport step needs a generated Refit client, so its order - resilience outside the
/// product's handlers, recycling last, the consumer's chain after all of it - is pinned by each product's DI
/// tests instead.
/// </summary>
public class ClientRegistrationTests
{
    public sealed class ProbeOptions
    {
        [Required]
        public string? Value { get; set; }

        public int Number { get; set; }
    }

    public interface IProbeClient
    {
        string Name { get; }
    }

    private sealed class ProbeClient(string name) : IProbeClient
    {
        public string Name { get; } = name;
    }

    public interface IProbeFactory;

    private sealed class ProbeFactory : IProbeFactory;

    private sealed class ProbeBuilder(string name, IServiceCollection services, OptionsBuilder<ProbeOptions> options)
        : ClientBuilder<ProbeOptions>(name, services, options);

    private static ProbeBuilder AddProbe(IServiceCollection services, string name = NamedClients.Default)
    {
        var builder = ClientRegistration.AddClient<ProbeOptions, IProbeClient, ProbeBuilder>(
            services,
            name,
            "probe client",
            httpClientName: null,
            static (name, services, options) => new ProbeBuilder(name, services, options));

        ClientRegistration.AddClientServices<IProbeClient, IProbeFactory, ProbeFactory>(
            services,
            name,
            static (_, key) => new ProbeClient((string)key!));

        return builder;
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    public void AddClient_RejectsAnInvalidName(string name)
    {
        var act = () => AddProbe(new ServiceCollection(), name);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AddClient_RejectsADuplicateName()
    {
        var services = new ServiceCollection();
        AddProbe(services, "twice");

        var act = () => AddProbe(services, "twice");

        act.Should().Throw<InvalidOperationException>().WithMessage("*probe client*'twice'*");
    }

    [Fact]
    public void AddClient_ValidatesTheOptionsOnRead()
    {
        var services = new ServiceCollection();
        AddProbe(services).Options.Configure(o => o.Number = 1);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptionsMonitor<ProbeOptions>>().Get(NamedClients.Default);

        act.Should().Throw<OptionsValidationException>().WithMessage("*Value*");
    }

    [Fact]
    public void AddClient_TheUnnamedOptionsMirrorTheDefaultClient()
    {
        var services = new ServiceCollection();
        AddProbe(services).Options.Configure(o => { o.Value = "named"; o.Number = 7; });
        using var provider = services.BuildServiceProvider();

        var unnamed = provider.GetRequiredService<IOptions<ProbeOptions>>().Value;
        var current = provider.GetRequiredService<IOptionsMonitor<ProbeOptions>>().CurrentValue;

        unnamed.Value.Should().Be("named");
        unnamed.Number.Should().Be(7);
        current.Value.Should().Be("named");
    }

    [Fact]
    public void AddClient_ANamedClientHasNoUnnamedMirror()
    {
        var services = new ServiceCollection();
        AddProbe(services, "other").Options.Configure(o => o.Value = "named");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<ProbeOptions>>().CurrentValue.Value.Should().BeNull();
    }

    [Fact]
    public void AddClient_AConfigurationReloadReachesTheUnnamedOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Probe:Value"] = "one" })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        AddProbe(services).Options.BindConfiguration("Probe");
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ProbeOptions>>();
        var changed = new List<string?>();
        using var subscription = monitor.OnChange((_, name) => changed.Add(name));
        monitor.CurrentValue.Value.Should().Be("one");
        monitor.Get(NamedClients.Default).Value.Should().Be("one");

        configuration.Providers.OfType<MemoryConfigurationProvider>().Single().Set("Probe:Value", "two");
        configuration.Reload();

        monitor.Get(NamedClients.Default).Value.Should().Be("two");
        monitor.CurrentValue.Value.Should().Be("two", "the unnamed options follow the default client's reloads");
        changed.Should().Contain(Options.DefaultName).And.Contain(NamedClients.Default);
    }

    [Fact]
    public void AddClient_ConfiguringTheUnnamedOptionsDoesNotReachTheClient()
    {
        var services = new ServiceCollection();
        services.Configure<ProbeOptions>(o => o.Number = 1);      // before: overwritten by the copy
        AddProbe(services).Options.Configure(o => o.Value = "named");
        services.Configure<ProbeOptions>(o => o.Number = 2);      // after: changes only the copy
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<ProbeOptions>>();

        monitor.Get(NamedClients.Default).Number.Should().Be(0, "the client reads the named options");
        monitor.CurrentValue.Number.Should().Be(2);
    }

    [Fact]
    public void AddClient_StartupValidationReportsAnInvalidDefaultOnce()
    {
        var services = new ServiceCollection();
        AddProbe(services).Options.Configure(o => o.Number = 1);
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().ThrowExactly<OptionsValidationException>("one failure is one exception, not an aggregate of the named and unnamed copies");
    }

    [Fact]
    public void AddClientServices_RegistersTheFactoryOnce()
    {
        var services = new ServiceCollection();
        AddProbe(services, "one").Options.Configure(o => o.Value = "1");
        AddProbe(services, "two").Options.Configure(o => o.Value = "2");
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IProbeFactory>().Should().ContainSingle();
        provider.GetRequiredKeyedService<IProbeClient>("one").Name.Should().Be("one");
        provider.GetRequiredKeyedService<IProbeClient>("two").Name.Should().Be("two");
    }

    [Fact]
    public void AddClientServices_TheDefaultClientResolvesUnkeyedToo()
    {
        var services = new ServiceCollection();
        AddProbe(services).Options.Configure(o => o.Value = "default");
        AddProbe(services, "other").Options.Configure(o => o.Value = "other");
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProbeClient>().Should().BeSameAs(provider.GetRequiredKeyedService<IProbeClient>(NamedClients.Default));
        provider.GetServices<IProbeClient>().Should().ContainSingle();
    }
}
