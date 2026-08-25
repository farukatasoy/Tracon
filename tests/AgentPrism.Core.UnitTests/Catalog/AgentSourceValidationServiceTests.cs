using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism.Core.UnitTests.Catalog;

/// <summary>
/// 101.4: <see cref="AgentSourceValidationService"/> checks the structural agent-source
/// contract while the host starts, cheaply — it must never call <c>ListAsync</c>, because
/// that call can do I/O (see the rationale on <c>ExternalSurfaceGuard</c> for why a
/// blocking startup call is unacceptable for a source backed by a database).
/// </summary>
public sealed class AgentSourceValidationServiceTests
{
    [Fact]
    public async Task Two_sources_with_the_same_name_fail_startup()
    {
        var service = new AgentSourceValidationService(
            [new StubSource("duplicate", 0), new StubSource("duplicate", 100)],
            NullLogger<AgentSourceValidationService>.Instance);

        var exception = await Should.ThrowAsync<AgentPrismAgentSourceException>(
            async () => await service.StartAsync(CancellationToken.None));

        exception.Message.ShouldContain("duplicate");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_source_name_fails_startup(string blankName)
    {
        var service = new AgentSourceValidationService(
            [new StubSource(blankName, 0)],
            NullLogger<AgentSourceValidationService>.Instance);

        await Should.ThrowAsync<AgentPrismAgentSourceException>(async () => await service.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Distinct_names_and_priorities_start_cleanly()
    {
        var service = new AgentSourceValidationService(
            [new StubSource("code", 0), new StubSource("database", 100)],
            NullLogger<AgentSourceValidationService>.Instance);

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Startup_validation_never_calls_ListAsync()
    {
        var source = new CountingSource("code", 0);
        var service = new AgentSourceValidationService([source], NullLogger<AgentSourceValidationService>.Instance);

        await service.StartAsync(CancellationToken.None);

        source.ListAsyncCalls.ShouldBe(0);
    }

    /// <summary>
    /// Sharing a priority — built-in or not — is not an error (101.9: nothing bars a
    /// custom source from choosing <see cref="AgentSourcePriority.Code"/> or
    /// <see cref="AgentSourcePriority.Database"/>); it just ties, and DI order breaks the
    /// tie. Startup still warns so the operator can see the tie exists.
    /// </summary>
    [Fact]
    public async Task Two_sources_sharing_a_priority_start_cleanly_but_log_a_warning()
    {
        var logger = new SpyLogger();
        var service = new AgentSourceValidationService(
            [new StubSource("first", 0), new StubSource("second", 0)],
            logger);

        await service.StartAsync(CancellationToken.None);

        logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning);
    }

    private sealed class SpyLogger : ILogger<AgentSourceValidationService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>A source with no descriptors, used purely to exercise name/priority validation.</summary>
    private sealed class StubSource(string name, int priority) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Startup validation must not call ListAsync.");

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Startup validation must not call ResolveAsync.");
    }

    private sealed class CountingSource(string name, int priority) : IAgentSource
    {
        public string Name { get; } = name;

        public int Priority { get; } = priority;

        public int ListAsyncCalls { get; private set; }

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListAsyncCalls++;
            return new ValueTask<IReadOnlyList<AgentDescriptor>>((IReadOnlyList<AgentDescriptor>)[]);
        }

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }
}
