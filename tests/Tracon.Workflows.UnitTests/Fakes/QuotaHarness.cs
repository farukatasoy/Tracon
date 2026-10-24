using Microsoft.Extensions.Options;

namespace Tracon.Workflows.UnitTests.Fakes;

/// <summary>
/// The tenant's quota as a workflow test sees it: an in-memory counter store, the real enforcer over it, and
/// a clock the runner and the enforcer share, so the day a counter was written on is the day it is read on.
/// </summary>
internal sealed class QuotaHarness
{
    /// <summary>The tenant every rule and counter belongs to unless a test names another.</summary>
    public const string Tenant = "test";

    /// <summary>Creates a harness over a counter store.</summary>
    /// <param name="start">What the shared clock reads.</param>
    /// <param name="configure">Changes the quota options.</param>
    /// <param name="store">The counter store; an in-memory one when omitted.</param>
    public QuotaHarness(DateTimeOffset start, Action<TraconQuotaOptions>? configure = null, IQuotaStore? store = null)
    {
        Clock = new AdjustableClock(start);
        Store = store ?? new InMemoryQuotaStore();

        var options = new TraconQuotaOptions();
        configure?.Invoke(options);

        Enforcer = new QuotaEnforcer(Store, new FixedOptions(options), webhookPublisher: null, Clock);
    }

    /// <summary>Gets the clock the enforcer reads; hand it to the runner too.</summary>
    public AdjustableClock Clock { get; }

    /// <summary>Gets the counter store.</summary>
    public IQuotaStore Store { get; }

    /// <summary>Gets the enforcer; hand it to the runner.</summary>
    public QuotaEnforcer Enforcer { get; }

    /// <summary>Defines a daily rule that applies to the whole tenant.</summary>
    /// <param name="maxCost">The amount a day may hold.</param>
    /// <param name="maxTokens">The tokens a day may hold.</param>
    /// <param name="tenant">The tenant the rule belongs to.</param>
    /// <param name="agentName">The name the rule is bound to; <see langword="null"/> for the whole tenant.</param>
    /// <returns>A task that completes when the rule is saved.</returns>
    public async Task RuleAsync(decimal? maxCost = null, long? maxTokens = null, string tenant = Tenant, string? agentName = null)
        => await Store.SaveAsync(new QuotaDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            AgentName = agentName,
            Period = QuotaPeriod.Daily,
            MaxCost = maxCost,
            MaxTokens = maxTokens,
            CreatedAt = Clock.GetUtcNow(),
            UpdatedAt = Clock.GetUtcNow(),
        });

    /// <summary>Writes what earlier runs of the tenant spent into today's counter.</summary>
    /// <param name="cost">The amount.</param>
    /// <param name="tokens">The tokens.</param>
    /// <param name="tenant">The tenant.</param>
    /// <param name="agentName">The name the counter is kept under.</param>
    /// <returns>A task that completes when the counter has taken it.</returns>
    public async Task SpentAsync(decimal cost = 0m, long tokens = 0, string tenant = Tenant, string agentName = "other-workflow")
        => await Enforcer.RecordAsync(new QuotaConsumption
        {
            TenantId = tenant,
            AgentName = agentName,
            Runs = 1,
            Tokens = tokens,
            Cost = cost == 0m ? null : cost,
            OccurredAt = Clock.GetUtcNow(),
        });

    /// <summary>Reads the tenant-wide counter of today.</summary>
    /// <param name="tenant">The tenant.</param>
    /// <returns>The counter.</returns>
    public async Task<QuotaUsageRecord> TodayAsync(string tenant = Tenant)
        => (await Store.GetUsageAsync(new QuotaUsageQuery { TenantId = tenant, AgentName = string.Empty, Period = QuotaPeriod.Daily }))
            .Single();

    private sealed class FixedOptions(TraconQuotaOptions value) : IOptionsMonitor<TraconQuotaOptions>
    {
        public TraconQuotaOptions CurrentValue => value;

        public TraconQuotaOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconQuotaOptions, string?> listener) => null;
    }
}
