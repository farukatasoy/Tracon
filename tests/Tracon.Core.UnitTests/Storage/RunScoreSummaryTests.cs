namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// Tests for <c>InMemoryRunScoreStore.SummarizeAsync</c>'s agent-name
/// resolution -- the one piece the shared <c>RunScoreStoreContract</c>
/// deliberately does not exercise, because it needs a real run to resolve
/// against and the bare contract has no <see cref="IRunStore"/>.
/// </summary>
public sealed class RunScoreSummaryTests
{
    private const string Tenant = "test";

    [Fact]
    public async Task With_no_resolver_the_AgentName_filter_matches_nothing()
    {
        // Never silently ignored: a filter that cannot be resolved must not
        // quietly behave as "no filter" and leak every score through.
        var scores = new InMemoryRunScoreStore();
        await scores.UpsertAsync(Score(TraconId.NewId()));

        var summary = await scores.SummarizeAsync(new RunScoreQuery { TenantId = Tenant, AgentName = "some-agent" });

        summary.ByName.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_no_resolver_ByAgent_stays_empty()
    {
        var scores = new InMemoryRunScoreStore();
        await scores.UpsertAsync(Score(TraconId.NewId()));

        var summary = await scores.SummarizeAsync(new RunScoreQuery { TenantId = Tenant });

        summary.ByAgent.ShouldBeEmpty();
        summary.ByName.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_resolver_narrows_the_AgentName_filter_and_fills_ByAgent()
    {
        var runOnAgentA = TraconId.NewId();
        var runOnAgentB = TraconId.NewId();

        var agentByRun = new Dictionary<Guid, string?>
        {
            [runOnAgentA] = "agent-a",
            [runOnAgentB] = "agent-b",
        };

        var scores = new InMemoryRunScoreStore(
            agentNameResolver: (runId, _) => new ValueTask<string?>(agentByRun.GetValueOrDefault(runId)));

        await scores.UpsertAsync(Score(runOnAgentA) with { Author = "alice" });
        await scores.UpsertAsync(Score(runOnAgentB) with { Author = "bob" });

        var filtered = await scores.SummarizeAsync(new RunScoreQuery { TenantId = Tenant, AgentName = "agent-a" });
        filtered.ByName.ShouldHaveSingleItem().Count.ShouldBe(1);

        var summary = await scores.SummarizeAsync(new RunScoreQuery { TenantId = Tenant });
        summary.ByAgent.Count.ShouldBe(2);
        summary.ByAgent.Single(aggregate => string.Equals(aggregate.Key, "agent-a", StringComparison.Ordinal)).Count.ShouldBe(1);
        summary.ByAgent.Single(aggregate => string.Equals(aggregate.Key, "agent-b", StringComparison.Ordinal)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_run_the_resolver_cannot_find_is_excluded_from_ByAgent_but_stays_in_ByName()
    {
        var scores = new InMemoryRunScoreStore(agentNameResolver: static (_, _) => new ValueTask<string?>((string?)null));
        await scores.UpsertAsync(Score(TraconId.NewId()));

        var summary = await scores.SummarizeAsync(new RunScoreQuery { TenantId = Tenant });

        summary.ByAgent.ShouldBeEmpty();
        summary.ByName.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task No_ambient_tenant_and_no_explicit_TenantId_throws()
    {
        var scores = new InMemoryRunScoreStore();

        await Should.ThrowAsync<ArgumentException>(async () => await scores.SummarizeAsync(new RunScoreQuery()));
    }

    [Fact]
    public async Task An_ambient_tenant_is_used_when_the_query_gives_none()
    {
        var scores = new InMemoryRunScoreStore(new FixedTenantContext(Tenant));
        await scores.UpsertAsync(Score(TraconId.NewId()));

        var summary = await scores.SummarizeAsync(new RunScoreQuery());

        summary.ByName.ShouldHaveSingleItem();
    }

    private static RunScore Score(Guid runId)
        => new()
        {
            TenantId = Tenant,
            RunId = runId,
            Name = "helpfulness",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "operator@example",
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private sealed class FixedTenantContext(string tenantId) : ITenantContext
    {
        public string TenantId { get; } = tenantId;
    }
}
