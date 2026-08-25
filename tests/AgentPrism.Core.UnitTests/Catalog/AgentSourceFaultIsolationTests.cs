using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Catalog;

/// <summary>
/// Verifies the two source-failure policies from 101.3: <c>ListAsync</c> is
/// fail-open (a broken source is skipped, the rest of the catalog stays usable),
/// <c>ResolveAsync</c> is fail-closed (a broken source stops the call instead of
/// silently falling through to a lower-priority source that might resolve a
/// DIFFERENT agent under the same name).
/// </summary>
public sealed class AgentSourceFaultIsolationTests
{
    [Fact]
    public async Task Listing_skips_a_throwing_source_and_returns_the_healthy_ones()
    {
        var broken = new ThrowingSource("broken", priority: 0, listException: new InvalidOperationException("raw upstream failure"));
        var healthy = new HealthySource("healthy", priority: 100, "alpha");

        var catalog = new CompositeAgentCatalog([broken, healthy], [], NullLogger<CompositeAgentCatalog>.Instance);

        var descriptors = await catalog.ListAsync();

        descriptors.Select(static d => d.Name).ShouldBe(["alpha"]);
    }

    [Fact]
    public async Task Listing_failure_is_logged_and_recorded_as_a_metric()
    {
        var broken = new ThrowingSource("broken", priority: 0, listException: new InvalidOperationException("raw upstream failure"));
        var logger = new SpyLogger();
        using var meterFactory = new TestMeterFactory();
        using var metrics = new AgentPrismMetrics(meterFactory);
        using var collector = new MetricCollector(meterFactory.Meter);

        var catalog = new CompositeAgentCatalog([broken], [], logger, metrics);

        await catalog.ListAsync();

        collector.LongValues(AgentPrismDiagnostics.AgentSourceFailureCounterName).ShouldBe([1]);
        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error && entry.Exception is InvalidOperationException);
    }

    [Fact]
    public async Task A_higher_priority_source_that_throws_on_resolve_stops_the_call_and_a_lower_priority_source_is_never_tried()
    {
        var broken = new ThrowingSource("broken", priority: 0, resolveException: new InvalidOperationException("raw upstream failure"));
        var lowerPriority = new HealthySource("lower", priority: 100, "shared");

        var catalog = new CompositeAgentCatalog([broken, lowerPriority], [], NullLogger<CompositeAgentCatalog>.Instance);

        await Should.ThrowAsync<AgentPrismAgentSourceException>(
            async () => await catalog.ResolveAsync("shared", culture: null, CancellationToken.None));

        lowerPriority.ResolveCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Resolve_failure_is_normalized_the_raw_message_does_not_reach_the_exception_text()
    {
        var broken = new ThrowingSource("broken", priority: 0, resolveException: new InvalidOperationException("raw upstream failure with a secret"));
        var catalog = new CompositeAgentCatalog([broken], [], NullLogger<CompositeAgentCatalog>.Instance);

        var exception = await Should.ThrowAsync<AgentPrismAgentSourceException>(
            async () => await catalog.ResolveAsync("shared", culture: null, CancellationToken.None));

        exception.SourceName.ShouldBe("broken");
        exception.ErrorType.ShouldBe(AgentPrismAgentSourceException.SourceFailedErrorType);
        exception.Message.ShouldNotContain("raw upstream failure");
        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task A_real_cancellation_during_listing_is_not_normalized()
    {
        var broken = new ThrowingSource("broken", priority: 0, listException: new OperationCanceledException());
        var catalog = new CompositeAgentCatalog([broken], [], NullLogger<CompositeAgentCatalog>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(async () => await catalog.ListAsync());
    }

    [Fact]
    public async Task A_real_cancellation_during_resolve_is_not_normalized()
    {
        var broken = new ThrowingSource("broken", priority: 0, resolveException: new OperationCanceledException());
        var catalog = new CompositeAgentCatalog([broken], [], NullLogger<CompositeAgentCatalog>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await catalog.ResolveAsync("shared", culture: null, CancellationToken.None));
    }

    private sealed class ThrowingSource(string name, int priority, Exception? listException = null, Exception? resolveException = null) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => listException is null
                ? new ValueTask<IReadOnlyList<AgentDescriptor>>((IReadOnlyList<AgentDescriptor>)[])
                : throw listException;

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => resolveException is null
                ? new ValueTask<AIAgent?>((AIAgent?)null)
                : throw resolveException;
    }

    private sealed class HealthySource(string name, int priority, params string[] agentNames) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public int ResolveCalls { get; private set; }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new((IReadOnlyList<AgentDescriptor>)
            [
                .. agentNames.Select(agentName => new AgentDescriptor { Name = agentName, Origin = AgentDefinitionOrigin.Code, SourceName = Name }),
            ]);

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
        {
            if (!agentNames.Contains(agentName, StringComparer.Ordinal))
            {
                return new ValueTask<AIAgent?>((AIAgent?)null);
            }

            ResolveCalls++;

            var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
            return new ValueTask<AIAgent?>(compiler.Compile(TestData.Definition(agentName)));
        }
    }

    /// <summary>Records every <see cref="ILogger"/> call it receives.</summary>
    private sealed class SpyLogger : ILogger<CompositeAgentCatalog>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));
    }
}
