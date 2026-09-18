using Kontent.Ai.Delivery.Abstractions;
using Kontent.Ai.Delivery.Api.Filtering;
using Kontent.Ai.Delivery.ContentItems;
using Kontent.Ai.Delivery.Generated;
using Microsoft.Extensions.Logging;

namespace Kontent.Ai.Delivery.Tests.Filtering;

public class SystemFilterHelpersTests
{
    [Fact]
    public void SystemFilterHelpers_AddSystemLanguageFilter_AddsSystemLanguageEq()
    {
        var filters = new SerializedFilterCollection();

        SystemFilterHelpers.AddSystemLanguageFilter(filters, "es-ES");

        Assert.Contains(new KeyValuePair<string, string>("system.language[eq]", "es-ES"), filters);
    }

    [Fact]
    public void AddGenericTypeFilter_DynamicModels_DoNotRequireFilters()
    {
        var filters = new SerializedFilterCollection();
        var resolvedCodenameProvider = new StubTypeProvider(codename: "article");

        SystemFilterHelpers.AddGenericTypeFilter<IDynamicElements>(filters, resolvedCodenameProvider, logger: null);
        SystemFilterHelpers.AddGenericTypeFilter<DynamicElements>(filters, resolvedCodenameProvider, logger: null);
        Assert.Empty(filters);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public void AddGenericTypeFilter_UnresolvedModel_ThrowsWithoutLogging(string? codename)
    {
        var filters = new SerializedFilterCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SystemFilterHelpers.AddGenericTypeFilter<TestModel>(filters, new StubTypeProvider(codename), logger: null));

        Assert.Contains(typeof(TestModel).FullName!, exception.Message);
        Assert.Contains("ITypeProvider", exception.Message);
        Assert.Contains("SourceGeneration", exception.Message);
        Assert.Empty(filters);
    }

    [Fact]
    public void AddGenericTypeFilter_ProviderKnowsOtherModels_StillRejectsUnknownModel()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SystemFilterHelpers.AddGenericTypeFilter<TestModel>([], new GeneratedTypeProvider(), logger: null));
        Assert.Throws<InvalidOperationException>(() =>
            SystemFilterHelpers.AddGenericTypeFilter<TestModel>([], new TypeProvider(generatedProvider: null), logger: null));
    }

    [Fact]
    public void AddGenericTypeFilter_UnresolvedModel_LogsWarningAndIgnoresExplicitTypeFilter()
    {
        var filters = new SerializedFilterCollection { new("system.type[eq]", "article") };
        var logger = new CollectingLogger();

        Assert.Throws<InvalidOperationException>(() =>
            SystemFilterHelpers.AddGenericTypeFilter<TestModel>(filters, new StubTypeProvider(codename: null), logger));

        Assert.Equal((LogLevel.Warning, 1410), Assert.Single(logger.Entries));
    }

    [Fact]
    public void AddGenericTypeFilter_KnownModel_PreservesAutomaticFilterAndConflictWarning()
    {
        var filters = new SerializedFilterCollection { new("system.type[in]", "article,product") };
        var logger = new CollectingLogger();

        SystemFilterHelpers.AddGenericTypeFilter<TestModel>(filters, new StubTypeProvider("article"), logger);

        Assert.Contains(new KeyValuePair<string, string>("system.type[eq]", "article"), filters);
        Assert.Equal((LogLevel.Warning, 1409), Assert.Single(logger.Entries));
    }

    [Fact]
    public void SystemFilterHelpers_AddGenericTypeFilter_DoesNotDuplicateExistingAutoFilter()
    {
        var filters = new SerializedFilterCollection
        {
            new KeyValuePair<string, string>("system.type[eq]", "article")
        };

        SystemFilterHelpers.AddGenericTypeFilter<TestModel>(filters, new StubTypeProvider(codename: "article"), logger: null);

        Assert.Single(filters, f => f.Key == "system.type[eq]" && f.Value == "article");
    }

    private sealed class TestModel;

    private sealed class StubTypeProvider(string? codename) : ITypeProvider
    {
        public Type? GetType(string contentType) => null;

        public string? GetCodename(Type contentType) => codename;
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<(LogLevel Level, int EventId)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, eventId.Id));
    }
}
