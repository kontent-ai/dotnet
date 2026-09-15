using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.ContentItems;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Tests.ContentItems;

public class ItemTypingStrategyTests
{
    private const int ContentTypeFallbackToDynamic = 1408;

    [Fact]
    public void UnmappedType_WithAGeneratedProvider_LogsAWarningOncePerType()
    {
        var logger = new CollectingLogger();
        var sut = new ItemTypingStrategy(new TypeProvider(new EmptyTypeProvider()), logger);

        Assert.Equal(typeof(DynamicElements), sut.ResolveModelType("tweet"));
        sut.ResolveModelType("tweet");

        var entry = Assert.Single(logger.Entries);
        Assert.Equal((LogLevel.Warning, ContentTypeFallbackToDynamic), entry);
    }

    [Fact]
    public void UnmappedType_WithACustomTypeProvider_LogsAWarning()
    {
        var logger = new CollectingLogger();
        var sut = new ItemTypingStrategy(new EmptyTypeProvider(), logger);

        sut.ResolveModelType("tweet");

        Assert.Equal((LogLevel.Warning, ContentTypeFallbackToDynamic), Assert.Single(logger.Entries));
    }

    [Fact]
    public void UnmappedType_WithoutATypeProvider_LogsAtDebug()
    {
        var logger = new CollectingLogger();
        var sut = new ItemTypingStrategy(new TypeProvider(generatedProvider: null), logger);

        sut.ResolveModelType("tweet");
        sut.ResolveModelType(string.Empty);

        Assert.All(logger.Entries, e => Assert.Equal((LogLevel.Debug, ContentTypeFallbackToDynamic), e));
        Assert.Equal(2, logger.Entries.Count);
    }

    private sealed class EmptyTypeProvider : ITypeProvider
    {
        public Type? GetType(string contentType) => null;
        public string? GetCodename(Type contentType) => null;
    }

    private sealed class CollectingLogger : ILogger<ItemTypingStrategy>
    {
        public List<(LogLevel Level, int EventId)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId.Id));
    }
}
