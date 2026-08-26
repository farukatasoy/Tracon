using System.Collections.Concurrent;

namespace AgentPrism;

/// <summary>
/// A store that keeps run records and their events in process memory.
/// </summary>
/// <remarks>
/// <para>
/// Events are append-only and stored by sequence number. Replay
/// (<see cref="ReadEventsAsync"/>) behaves the same as with persistent stores.
/// </para>
/// <para>
/// <strong>Limits:</strong> process lifetime, single node, and unbounded
/// memory growth. <see cref="MaxRuns"/> automatically drops the oldest runs.
/// Use <c>AgentPrism.PostgreSql</c> in production.
/// </para>
/// </remarks>
internal sealed partial class InMemoryRunStore : IRunStore
{
    private readonly ConcurrentDictionary<Guid, RunRecord> _runs = new();
    private readonly ConcurrentDictionary<Guid, List<RunEvent>> _events = new();
    private readonly ConcurrentDictionary<Guid, List<ToolInvocationRecord>> _toolInvocations = new();
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _heartbeats = new();
    private readonly ConcurrentQueue<Guid> _insertionOrder = new();
    private readonly IRunScoreStore _scores;
    private readonly ITenantContext _tenantContext;

    /// <summary>Creates a new in-memory run store.</summary>
    /// <param name="scores">
    /// The score store used in summary computation (<see cref="GetStatisticsAsync"/>).
    /// If not given, creates a private instance of its own -- this is to
    /// avoid breaking tests that use the parameterless <c>new InMemoryRunStore()</c>.
    /// When resolved through DI, it gets the shared singleton instance
    /// registered by <c>AddAgentPrism()</c>, so scores written by the HTTP
    /// layer appear in the summary.
    /// </param>
    /// <param name="tenantContext">
    /// The current tenant's context. If not given, the store behaves as
    /// single-tenant.
    /// </param>
    public InMemoryRunStore(IRunScoreStore? scores = null, ITenantContext? tenantContext = null)
    {
        _scores = scores ?? new InMemoryRunScoreStore();
        _tenantContext = tenantContext ?? FixedTenantContext.Default;
    }

    /// <summary>
    /// The upper bound on the number of runs kept in memory. When exceeded,
    /// the oldest run and its events are dropped.
    /// </summary>
    public int MaxRuns { get; init; } = 1_000;

    /// <summary>
    /// Whether the write's target run belongs to the expected tenant.
    /// </summary>
    /// <param name="runId">The run's identity.</param>
    /// <param name="expectedTenantId">The expected tenant. No check is performed if <see langword="null"/>.</param>
    /// <param name="suffix">Text appended to the end of the error message.</param>
    /// <exception cref="AgentPrismException">The tenant does not match.</exception>
    private void EnsureExpectedTenant(Guid runId, string? expectedTenantId, string suffix)
    {
        if (expectedTenantId is null)
        {
            return;
        }

        if (_runs.TryGetValue(runId, out var run)
            && !string.Equals(run.TenantId, expectedTenantId, StringComparison.Ordinal))
        {
            throw new AgentPrismException(
                $"Run '{runId}' does not belong to the expected tenant ('{expectedTenantId}'). {suffix}");
        }
    }
}
