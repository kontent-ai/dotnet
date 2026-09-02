// Shared test source, compiled into each test assembly - see src/testing/README.md.

using AwesomeAssertions;
using Kontent.Ai.Common.Http;

namespace Kontent.Ai.Testing.Http;

/// <summary>
/// Pins the ceiling rule in every product that compiles <see cref="HttpClientTimeouts"/>. Each product's
/// DI tests pin that its registration applies it.
/// </summary>
public class HttpClientTimeoutsTests
{
    private static readonly TimeSpan Fallback = TimeSpan.FromSeconds(100);

    [Fact]
    public void Resolve_DefaultPipeline_LiftsTheCeiling()
    {
        HttpClientTimeouts.Resolve(null, defaultPipelineBoundsAttempts: true, Fallback).Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void Resolve_WithoutTheDefaultPipeline_KeepsTheFallback()
    {
        HttpClientTimeouts.Resolve(null, defaultPipelineBoundsAttempts: false, Fallback).Should().Be(Fallback);
    }

    [Fact]
    public void Resolve_ExplicitCeiling_OutranksTheLift()
    {
        HttpClientTimeouts.Resolve(TimeSpan.FromMinutes(5), defaultPipelineBoundsAttempts: true, Fallback).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Resolve_ExplicitInfinite_RemovesTheCeilingWithoutTheDefaultPipeline()
    {
        HttpClientTimeouts.Resolve(Timeout.InfiniteTimeSpan, defaultPipelineBoundsAttempts: false, Fallback).Should().Be(Timeout.InfiniteTimeSpan);
    }
}
