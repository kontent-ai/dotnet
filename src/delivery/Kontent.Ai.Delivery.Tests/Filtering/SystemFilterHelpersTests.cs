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
    public void AddGenericTypeFilter_DynamicModelsAndUnmappedObject_DoNotRequireFilters()
    {
        var filters = new SerializedFilterCollection();
        var missingCodenameProvider = new StubTypeProvider(codename: null);
        var resolvedCodenameProvider = new StubTypeProvider(codename: "article");

        SystemFilterHelpers.AddGenericTypeFilter<IDynamicElements>(filters, resolvedCodenameProvider, logger: null);
        SystemFilterHelpers.AddGenericTypeFilter<DynamicElements>(filters, resolvedCodenameProvider, logger: null);
        SystemFilterHelpers.AddGenericTypeFilter<object>(filters, missingCodenameProvider, logger: null);
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

    [Theory]
    [InlineData("system.type[eq]", "article", true)]
    [InlineData("System.Type[EQ]", "article", true)]
    [InlineData("system.type[in]", "article", true)]
    [InlineData("system.type[in]", "article,product", true)]
    [InlineData("system.type[eq]", "", false)]
    [InlineData("system.type[eq]", " ", false)]
    [InlineData("system.type[in]", "", false)]
    [InlineData("system.type[in]", "article, ", false)]
    [InlineData("system.type[in]", ",article", false)]
    [InlineData("system.type[neq]", "article", false)]
    [InlineData("system.type[nin]", "article", false)]
    [InlineData("system.type[contains]", "article", false)]
    [InlineData("system.type[nempty]", "", false)]
    [InlineData("system.type[eq][eq]", "article", false)]
    [InlineData("elements.type[eq]", "article", false)]
    [InlineData("system.codename[eq]", "my_article", false)]
    public void AddGenericTypeFilter_OnlyPositiveTypeFiltersPermitProjections(string key, string value, bool permitted)
    {
        var filters = new SerializedFilterCollection { new(key, value) };
        var logger = new CollectingLogger();
        var provider = new StubTypeProvider(codename: null);

        if (permitted)
        {
            SystemFilterHelpers.AddGenericTypeFilter<TestModel>(filters, provider, logger);
            Assert.Empty(logger.Entries);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() =>
                SystemFilterHelpers.AddGenericTypeFilter<TestModel>(filters, provider, logger));
            Assert.Equal((LogLevel.Warning, 1410), Assert.Single(logger.Entries));
        }

        Assert.Equal(new KeyValuePair<string, string>(key, value), Assert.Single(filters));
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
    public void AddGenericTypeFilter_MappedObject_PreservesAutomaticFilter()
    {
        var filters = new SerializedFilterCollection();

        SystemFilterHelpers.AddGenericTypeFilter<object>(filters, new StubTypeProvider("article"), logger: null);

        Assert.Equal(new KeyValuePair<string, string>("system.type[eq]", "article"), Assert.Single(filters));
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
