using System.Globalization;
using System.Text;
using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>The tenants, agent names and times one quota sequence draws from.</summary>
/// <param name="Tenants">Tenant spellings, some of them twins of others.</param>
/// <param name="Agents">Agent names, some of them twins of others (a name is compared by its characters).</param>
/// <param name="Instants">Times a rule is created and updated at.</param>
/// <param name="SnapshotReads">Whether a read of several counters sees them all before or all after a concurrent write.</param>
internal sealed record QuotaUniverse(string[] Tenants, string[] Agents, DateTimeOffset[] Instants, bool SnapshotReads);

/// <summary>A generated quota sequence.</summary>
/// <param name="Universe">The values the steps draw from.</param>
/// <param name="Steps">The steps.</param>
internal sealed record QuotaPlan(QuotaUniverse Universe, IReadOnlyList<QuotaStep> Steps);

/// <summary>The first days of the periods a call names: the daily one, the monthly one, or neither.</summary>
/// <param name="Daily">The day offset of the daily period (from 2026-08-03), or <see langword="null"/> to leave the period out.</param>
/// <param name="Monthly">The month offset of the monthly period (from 2026-08), or <see langword="null"/> to leave the period out.</param>
internal sealed record PeriodStartsSpec(int? Daily, int? Monthly);

/// <summary>One call on a quota store.</summary>
internal abstract record QuotaStep;

/// <summary>The saving of a rule; the identifier is decided when the step is concretized.</summary>
/// <param name="Tenant">The tenant spelling.</param>
/// <param name="Agent">The agent name, or -1 for a rule that covers every agent.</param>
/// <param name="Period">The period.</param>
/// <param name="MaxRuns">The run limit.</param>
/// <param name="MaxTokens">The token limit.</param>
/// <param name="MaxCost">The cost limit.</param>
/// <param name="Enabled">Whether the rule is on.</param>
/// <param name="FreshId">Whether a rule that already holds the scope is saved under an identifier of its own.</param>
/// <param name="IdSlot">The identifier slot the rule is saved under; -1 until the model decides.</param>
/// <param name="Created">The creation time.</param>
/// <param name="Updated">The update time.</param>
internal sealed record SaveQuotaStep(int Tenant, int Agent, QuotaPeriod Period, long? MaxRuns, long? MaxTokens, decimal? MaxCost, bool Enabled, bool FreshId, int IdSlot, int Created, int Updated) : QuotaStep;

/// <summary>The read of a rule.</summary>
internal sealed record GetQuotaStep(int Tenant, int IdSlot) : QuotaStep;

/// <summary>The listing of a tenant's rules.</summary>
internal sealed record ListQuotaStep(int Tenant) : QuotaStep;

/// <summary>The removal of a rule.</summary>
internal sealed record DeleteQuotaStep(int Tenant, int IdSlot) : QuotaStep;

/// <summary>The recording of a consumption.</summary>
/// <param name="Tenant">The tenant spelling.</param>
/// <param name="Agent">The agent name, or -1 for the empty name.</param>
/// <param name="Runs">The runs added.</param>
/// <param name="Tokens">The tokens added.</param>
/// <param name="Cost">The cost added, or <see langword="null"/> when the price is not known.</param>
/// <param name="Starts">The periods the consumption falls into.</param>
internal sealed record AddUsageStep(int Tenant, int Agent, long Runs, long Tokens, decimal? Cost, PeriodStartsSpec Starts) : QuotaStep;

/// <summary>The read of the counters.</summary>
/// <param name="Tenant">The tenant spelling.</param>
/// <param name="Agent">The agent filter: <see langword="null"/> for none, -1 for the empty name.</param>
/// <param name="Period">The period filter.</param>
/// <param name="Starts">The period starts filter, or <see langword="null"/> for none.</param>
internal sealed record GetUsageStep(int Tenant, int? Agent, QuotaPeriod? Period, PeriodStartsSpec? Starts) : QuotaStep;

/// <summary>The claim of a threshold notification.</summary>
/// <param name="Tenant">The tenant spelling.</param>
/// <param name="Agent">The agent name, or -1 for the empty name.</param>
/// <param name="Period">The period.</param>
/// <param name="Start">The first day of the period.</param>
/// <param name="Metric">The metric.</param>
/// <param name="Percent">The threshold.</param>
internal sealed record ClaimQuotaStep(int Tenant, int Agent, QuotaPeriod Period, PeriodStartsSpec Start, QuotaMetric Metric, int Percent) : QuotaStep;

/// <summary>
/// The quota store state machine: rules (scope uniqueness, overwrite, listing, removal), counters (the agent counter and
/// the tenant-wide counter, filters, period starts) and the one-time threshold claim.
/// </summary>
internal sealed class QuotaStateMachine : StateMachine<QuotaPlan, QuotaStep>
{
    private const int SlotCount = 6;

    private static readonly Gen<long?> Limit = Gen.Frequency(
        (4, Gen.Const((long?)null)),
        (1, Gen.Const((long?)0)),
        (5, Gen.Int[1, 100_000].Select(static value => (long?)value)),
        (1, Gen.Const((long?)9_000_000_000_000)));

    private static readonly Gen<decimal?> LimitCost = Gen.Frequency(
        (4, Gen.Const((decimal?)null)),
        (1, Gen.Const((decimal?)0m)),
        (5, Gen.Int[1, 10_000_000].Select(static cents => (decimal?)(cents / 100m))),
        (1, Gen.OneOf(0.0000000001m, 0.00000000005m, 123456.1234567891m).Select(static value => (decimal?)value)));

    private static readonly Gen<decimal?> Consumed = Gen.Frequency(
        (2, Gen.Const((decimal?)null)),
        (1, Gen.Const((decimal?)0m)),
        (5, Gen.Int[1, 100_000].Select(static cents => (decimal?)(cents / 100m))),
        (2, Gen.OneOf(0.125m, 0.25m, 0.0000000001m, 0.00000000005m, 0.1234567890m).Select(static value => (decimal?)value)));

    /// <inheritdoc />
    public override string Name => "IQuotaStore";

    /// <inheritdoc />
    public override Gen<QuotaPlan> Plans(int maxSteps, ProviderTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        var universe =
            from tenants in BoundaryValues.TenantPool(3)
            from agents in BoundaryValues.Pool(3, BoundaryValues.Trimmed(100), 100, traits.TrailingSpaceInsensitiveKeys)
            from instants in BoundaryValues.Instant.Array[3]
            select new QuotaUniverse(tenants, agents, instants, !traits.MultiRowReadIsNotASnapshot);

        return universe.SelectMany(
            u => Step(u).List[1, maxSteps],
            static (u, steps) => new QuotaPlan(u, steps));
    }

    /// <inheritdoc />
    public override IReadOnlyList<QuotaStep> Steps(QuotaPlan plan) => plan.Steps;

    /// <inheritdoc />
    public override IReadOnlyList<QuotaStep> RaceSteps(QuotaPlan plan)
    {
        // One counter exists before the tasks start, so that a claim has something to claim; the tasks then add usage,
        // claim, save, delete and read it.
        var setup = new AddUsageStep(0, 0, 1, 10, 0.5m, new PeriodStartsSpec(0, 0));

        // A store that reads several rows without a snapshot can show one counter before an AddUsage and its twin after it: the read
        // of the counters is left out of the tasks there (the read-back is exact).
        return
        [
            setup,
            .. plan.Steps.Where(step => step is not AddUsageStep { Starts: { Daily: null, Monthly: null } } && (plan.Universe.SnapshotReads || step is not GetUsageStep)),
        ];
    }

    /// <inheritdoc />
    public override int? RaceSetupLength(QuotaPlan plan) => 1;

    /// <inheritdoc />
    public override IReadOnlyList<QuotaStep> Snapshot(QuotaPlan plan)
        => [.. Enumerable.Range(0, plan.Universe.Tenants.Length).SelectMany(static tenant => new QuotaStep[]
        {
            new ListQuotaStep(tenant),
            new GetUsageStep(tenant, null, null, null),
        })];

    /// <inheritdoc />
    public override IStepModel<QuotaStep> NewModel(QuotaPlan plan, ProviderTraits traits) => new QuotaModel(plan.Universe, traits);

    /// <inheritdoc />
    public override IStepDriver<QuotaStep> NewDriver(QuotaPlan plan, DifferentialStores stores, DifferentialScope scope)
        => new QuotaDriver(DifferentialStores.Require(stores.Quotas), scope, plan.Universe);

    /// <inheritdoc />
    public override string Describe(QuotaStep step) => step switch
    {
        SaveQuotaStep s => $"Save tenant={s.Tenant} agent={s.Agent} period={s.Period} runs={s.MaxRuns?.ToString(CultureInfo.InvariantCulture) ?? "-"} tokens={s.MaxTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"} cost={s.MaxCost?.ToString(CultureInfo.InvariantCulture) ?? "-"} enabled={s.Enabled} freshId={s.FreshId} id={s.IdSlot} created={s.Created} updated={s.Updated}",
        GetQuotaStep s => $"Get tenant={s.Tenant} id={s.IdSlot}",
        ListQuotaStep s => $"List tenant={s.Tenant}",
        DeleteQuotaStep s => $"Delete tenant={s.Tenant} id={s.IdSlot}",
        AddUsageStep s => $"AddUsage tenant={s.Tenant} agent={s.Agent} runs={s.Runs} tokens={s.Tokens} cost={s.Cost?.ToString(CultureInfo.InvariantCulture) ?? "-"} starts={s.Starts}",
        GetUsageStep s => $"GetUsage tenant={s.Tenant} agent={s.Agent?.ToString(CultureInfo.InvariantCulture) ?? "-"} period={s.Period?.ToString() ?? "-"} starts={(s.Starts is null ? "-" : s.Starts.ToString())}",
        ClaimQuotaStep s => $"Claim tenant={s.Tenant} agent={s.Agent} period={s.Period} start={s.Start} metric={s.Metric} percent={s.Percent}",
        _ => step.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public override string Print(QuotaPlan plan)
    {
        var builder = new StringBuilder("\n");
        SessionStateMachine.Pool(builder, "tenants", plan.Universe.Tenants);
        SessionStateMachine.Pool(builder, "agents", plan.Universe.Agents);

        for (var index = 0; index < plan.Universe.Instants.Length; index++)
        {
            builder.Append("  instants[").Append(index).Append("] = ").AppendLine(BoundaryValues.Show(plan.Universe.Instants[index]));
        }

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            builder.Append("  ").Append(index + 1).Append(". ").AppendLine(Describe(plan.Steps[index]));
        }

        return builder.ToString();
    }

    private static Gen<QuotaStep> Step(QuotaUniverse u)
    {
        var tenant = Gen.Int[0, u.Tenants.Length - 1];
        var agent = Gen.Int[0, u.Agents.Length - 1];
        var anyAgent = Gen.Frequency((7, agent), (1, Gen.Const(-1)));
        var period = Gen.Enum<QuotaPeriod>();
        var idSlot = Gen.Int[0, SlotCount - 1];
        var instant = Gen.Int[0, u.Instants.Length - 1];
        var starts = Gen.Frequency(
            (6, Gen.Int[0, 2].SelectMany(day => Gen.Int[0, 1].Select(month => new PeriodStartsSpec(day, month)))),
            (2, Gen.Int[0, 2].Select(static day => new PeriodStartsSpec(day, null))),
            (2, Gen.Int[0, 1].Select(static month => new PeriodStartsSpec(null, month))),
            (1, Gen.Const(new PeriodStartsSpec(null, null))));
        var save =
            from t in tenant
            from a in Gen.Frequency((3, Gen.Const(-1)), (5, agent))
            from p in period
            from runs in Limit
            from tokens in Limit
            from cost in LimitCost
            from enabled in Gen.Bool
            from fresh in Gen.Frequency((3, Gen.Const(false)), (1, Gen.Const(true)))
            from created in instant
            from updated in instant
            select (QuotaStep)new SaveQuotaStep(t, a, p, runs, tokens, cost, enabled, fresh, -1, created, updated);
        var add =
            from t in tenant
            from a in anyAgent
            from runs in Gen.Frequency((6, Gen.Int[0, 3]), (1, Gen.Const(0)))
            from tokens in Gen.Int[0, 1000]
            from cost in Consumed
            from s in starts
            select (QuotaStep)new AddUsageStep(t, a, runs, tokens, cost, s);
        var usage =
            from t in tenant
            from a in Gen.Frequency((3, Gen.Const((int?)null)), (3, agent.Select(static x => (int?)x)), (1, Gen.Const((int?)-1)))
            from p in Gen.Frequency((3, Gen.Const((QuotaPeriod?)null)), (2, period.Select(static x => (QuotaPeriod?)x)))
            from s in Gen.Frequency((3, Gen.Const((PeriodStartsSpec?)null)), (4, starts.Select(static x => (PeriodStartsSpec?)x)))
            select (QuotaStep)new GetUsageStep(t, a, p, s);
        var claim =
            from t in tenant
            from a in anyAgent
            from p in period
            from s in starts
            from m in Gen.Enum<QuotaMetric>()
            from percent in Gen.Frequency((6, Gen.OneOf(50, 80, 90, 100)), (1, Gen.OneOf(0, 1, 150)))
            select (QuotaStep)new ClaimQuotaStep(t, a, p, s, m, percent);

        return Gen.Frequency(
            (16, save),
            (8, tenant.SelectMany(t => idSlot.Select(s => (QuotaStep)new GetQuotaStep(t, s)))),
            (8, tenant.Select(static t => (QuotaStep)new ListQuotaStep(t))),
            (6, tenant.SelectMany(t => idSlot.Select(s => (QuotaStep)new DeleteQuotaStep(t, s)))),
            (20, add),
            (12, usage),
            (14, claim));
    }
}

/// <summary>The quota specification, written from the interface documentation and the behaviour its contract tests pin.</summary>
internal sealed class QuotaModel(QuotaUniverse universe, ProviderTraits traits) : IStepModel<QuotaStep>
{
    private static readonly DateOnly FirstDay = new(2026, 8, 3);

    private static readonly DateOnly FirstMonth = new(2026, 8, 1);

    private readonly Dictionary<(string Tenant, string Agent, QuotaPeriod Period), Rule> _rules = [];
    private readonly Dictionary<(string Tenant, string Agent, QuotaPeriod Period, DateOnly Start), Counter> _counters = [];
    private int _nextId;

    /// <summary>Renders a rule in the one form both sides use.</summary>
    /// <returns>The text.</returns>
    internal static string RenderRule(int id, string tenant, string? agent, QuotaPeriod period, long? runs, long? tokens, decimal? cost, bool enabled, long created, long updated)
        => $"rule#{id} tenant={BoundaryValues.Show(tenant)} agent={(agent is null ? "-" : BoundaryValues.Show(agent))} period={period} runs={runs?.ToString(CultureInfo.InvariantCulture) ?? "-"} tokens={tokens?.ToString(CultureInfo.InvariantCulture) ?? "-"} cost={Decimal(cost)} enabled={enabled} created={created.ToString(CultureInfo.InvariantCulture)} updated={updated.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Renders a counter in the one form both sides use.</summary>
    /// <returns>The text.</returns>
    internal static string RenderCounter(string tenant, string agent, QuotaPeriod period, DateOnly start, long runs, long tokens, decimal cost)
        => $"counter tenant={BoundaryValues.Show(tenant)} agent={BoundaryValues.Show(agent)} period={period} start={start:yyyy-MM-dd} runs={runs.ToString(CultureInfo.InvariantCulture)} tokens={tokens.ToString(CultureInfo.InvariantCulture)} cost={Decimal(cost)}";

    /// <summary>Gets the first day a period spec names for a period.</summary>
    /// <param name="spec">The spec.</param>
    /// <param name="period">The period.</param>
    /// <returns>The day, or <see langword="null"/> when the spec leaves the period out.</returns>
    internal static DateOnly? StartOf(PeriodStartsSpec spec, QuotaPeriod period)
        => period == QuotaPeriod.Daily
            ? spec.Daily is { } day ? FirstDay.AddDays(day) : null
            : spec.Monthly is { } month ? FirstMonth.AddMonths(month) : null;

    private static string Decimal(decimal? value) => value is { } amount ? amount.ToString("0.############################", CultureInfo.InvariantCulture) : "-";

    /// <inheritdoc />
    public QuotaStep Concretize(QuotaStep step)
    {
        if (step is SaveQuotaStep save)
        {
            var key = (Canonical(save.Tenant), RuleAgent(save.Agent), save.Period);

            // A rule saved under a scope that holds none takes a new identifier. A scope that holds one keeps it, unless
            // the step brings an identifier of its own: then the call carries that one and the store decides which stays.
            return _rules.TryGetValue(key, out var held)
                ? save with { IdSlot = save.FreshId ? _nextId++ : held.IdSlot }
                : save with { IdSlot = _nextId++ };
        }

        return step;
    }

    /// <inheritdoc />
    public Observation Apply(QuotaStep step)
    {
        try
        {
            return step switch
            {
                SaveQuotaStep s => Save(s),
                GetQuotaStep s => Get(s),
                ListQuotaStep s => List(s),
                DeleteQuotaStep s => Delete(s),
                AddUsageStep s => Add(s),
                GetUsageStep s => Usage(s),
                ClaimQuotaStep s => Claim(s),
                _ => throw new InvalidOperationException(step.GetType().Name),
            };
        }
        catch (ArgumentException)
        {
            return Observation.Rejected();
        }
    }

    private static string Fold(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentException("blank tenant", nameof(tenant));
        }

        return tenant.ToLowerInvariant();
    }

    private string Canonical(int tenant) => traits.Text(Fold(universe.Tenants[tenant]));

    // A rule that covers every agent has no agent name; a counter that covers every agent has the empty one.
    private string RuleAgent(int agent) => agent < 0 ? string.Empty : traits.Text(universe.Agents[agent]);

    private string CounterAgent(int agent) => agent < 0 ? string.Empty : traits.Text(universe.Agents[agent]);

    private Observation Save(SaveQuotaStep s)
    {
        var tenant = Canonical(s.Tenant);
        var key = (tenant, RuleAgent(s.Agent), s.Period);

        if (_rules.TryGetValue(key, out var existing))
        {
            // The scope is overwritten in place: the rule keeps the identifier and the creation time it had (the creation
            // time says when the scope first got a rule; the contract tests pin it).
            existing.MaxRuns = s.MaxRuns;
            existing.MaxTokens = s.MaxTokens;
            existing.MaxCost = traits.Cost(s.MaxCost);
            existing.Enabled = s.Enabled;
            existing.Updated = traits.Ticks(universe.Instants[s.Updated]);

            return Observation.Of("saved " + Render(existing));
        }

        var rule = new Rule
        {
            IdSlot = s.IdSlot,
            Tenant = tenant,
            Agent = s.Agent < 0 ? null : traits.Text(universe.Agents[s.Agent]),
            Period = s.Period,
            MaxRuns = s.MaxRuns,
            MaxTokens = s.MaxTokens,
            MaxCost = traits.Cost(s.MaxCost),
            Enabled = s.Enabled,
            Created = traits.Ticks(universe.Instants[s.Created]),
            Updated = traits.Ticks(universe.Instants[s.Updated]),
        };

        _rules[key] = rule;

        return Observation.Of("saved " + Render(rule));
    }

    private Observation Get(GetQuotaStep s)
    {
        var tenant = Canonical(s.Tenant);

        return Observation.Of(_rules.Values.FirstOrDefault(rule => rule.IdSlot == s.IdSlot && string.Equals(rule.Tenant, tenant, StringComparison.Ordinal)) is { } found ? Render(found) : "null");
    }

    private Observation List(ListQuotaStep s)
    {
        var tenant = Canonical(s.Tenant);

        return Observation.List("rules", [.. _rules.Values.Where(rule => string.Equals(rule.Tenant, tenant, StringComparison.Ordinal)).Select(Render).Order(StringComparer.Ordinal)]);
    }

    private Observation Delete(DeleteQuotaStep s)
    {
        var tenant = Canonical(s.Tenant);
        var found = _rules.FirstOrDefault(pair => pair.Value.IdSlot == s.IdSlot && string.Equals(pair.Value.Tenant, tenant, StringComparison.Ordinal));

        if (found.Value is null)
        {
            return Observation.Of("false");
        }

        _rules.Remove(found.Key);

        return Observation.Of("true");
    }

    private Observation Add(AddUsageStep s)
    {
        var tenant = Canonical(s.Tenant);
        var agent = CounterAgent(s.Agent);

        foreach (var period in Enum.GetValues<QuotaPeriod>())
        {
            if (QuotaModel.StartOf(s.Starts, period) is not { } start)
            {
                continue;
            }

            // The consumption is added to the agent's counter and to the counter that covers every agent. A consumption
            // that names no agent has one counter, the one that covers every agent, and it is added to once.
            foreach (var name in agent.Length == 0 ? new[] { string.Empty } : new[] { agent, string.Empty })
            {
                var key = (tenant, name, period, start);

                if (!_counters.TryGetValue(key, out var counter))
                {
                    counter = new Counter();
                    _counters[key] = counter;
                }

                counter.Runs += s.Runs;
                counter.Tokens += s.Tokens;
                counter.Cost += traits.Cost(s.Cost) ?? 0m;
            }
        }

        return Observation.Of("ok");
    }

    private Observation Usage(GetUsageStep s)
    {
        var tenant = Canonical(s.Tenant);
        var agent = s.Agent is { } index ? CounterAgent(index) : null;
        var rows = new List<string>();

        foreach (var (key, counter) in _counters)
        {
            if (!string.Equals(key.Tenant, tenant, StringComparison.Ordinal)
                || (agent is not null && !string.Equals(key.Agent, agent, StringComparison.Ordinal))
                || (s.Period is { } period && key.Period != period))
            {
                continue;
            }

            // With period starts, a counter is returned only for a period the filter names, and only for the day it gives.
            if (s.Starts is { } starts && StartOf(starts, key.Period) != key.Start)
            {
                continue;
            }

            rows.Add(RenderCounter(key.Tenant, key.Agent, key.Period, key.Start, counter.Runs, counter.Tokens, counter.Cost));
        }

        rows.Sort(StringComparer.Ordinal);

        return Observation.List("counters", rows);
    }

    private Observation Claim(ClaimQuotaStep s)
    {
        var tenant = Canonical(s.Tenant);
        var agent = CounterAgent(s.Agent);
        var start = StartOf(s.Start, s.Period) ?? FirstDay;

        // The claim lives in the counter row: no row, no claim; a row hands each metric and percentage out once.
        if (!_counters.TryGetValue((tenant, agent, s.Period, start), out var counter) || !counter.Claims.Add((s.Metric, s.Percent)))
        {
            return Observation.Of("false");
        }

        return Observation.Of("true");
    }

    private string Render(Rule rule)
        => RenderRule(rule.IdSlot, rule.Tenant, rule.Agent, rule.Period, rule.MaxRuns, rule.MaxTokens, rule.MaxCost, rule.Enabled, rule.Created, rule.Updated);

    private sealed class Rule
    {
        public required int IdSlot { get; init; }

        public required string Tenant { get; init; }

        public required string? Agent { get; init; }

        public required QuotaPeriod Period { get; init; }

        public required long Created { get; init; }

        public long? MaxRuns { get; set; }

        public long? MaxTokens { get; set; }

        public decimal? MaxCost { get; set; }

        public bool Enabled { get; set; }

        public long Updated { get; set; }
    }

    private sealed class Counter
    {
        public long Runs { get; set; }

        public long Tokens { get; set; }

        public decimal Cost { get; set; }

        public HashSet<(QuotaMetric Metric, int Percent)> Claims { get; } = [];
    }
}

/// <summary>Applies quota steps to a store.</summary>
internal sealed class QuotaDriver(IQuotaStore store, DifferentialScope scope, QuotaUniverse universe) : IStepDriver<QuotaStep>
{
    // Steps of a race run in several tasks: the identifier of a slot is made once, by whichever task needs it first.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Guid> _ids = [];

    /// <inheritdoc />
    public async ValueTask<Observation> ApplyAsync(QuotaStep step)
    {
        switch (step)
        {
            case SaveQuotaStep s:
                var saved = await store.SaveAsync(new QuotaDefinition
                {
                    Id = Id(s.IdSlot),
                    TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                    AgentName = s.Agent < 0 ? null : universe.Agents[s.Agent],
                    Period = s.Period,
                    MaxRuns = s.MaxRuns,
                    MaxTokens = s.MaxTokens,
                    MaxCost = s.MaxCost,
                    Enabled = s.Enabled,
                    CreatedAt = universe.Instants[s.Created],
                    UpdatedAt = universe.Instants[s.Updated],
                });

                return Observation.Of("saved " + Render(saved));
            case GetQuotaStep s:
                var found = await store.GetAsync(scope.Wrap(universe.Tenants[s.Tenant]), Id(s.IdSlot));

                return Observation.Of(found is null ? "null" : Render(found));
            case ListQuotaStep s:
                var listed = await store.ListAsync(scope.Wrap(universe.Tenants[s.Tenant]));

                return Observation.List("rules", [.. listed.Select(Render).Order(StringComparer.Ordinal)]);
            case DeleteQuotaStep s:
                return Observation.Of(await store.DeleteAsync(scope.Wrap(universe.Tenants[s.Tenant]), Id(s.IdSlot)) ? "true" : "false");
            case AddUsageStep s:
                await store.AddUsageAsync(
                    new QuotaConsumption
                    {
                        TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                        AgentName = s.Agent < 0 ? string.Empty : universe.Agents[s.Agent],
                        Runs = s.Runs,
                        Tokens = s.Tokens,
                        Cost = s.Cost,
                        OccurredAt = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
                    },
                    Starts(s.Starts));

                return Observation.Of("ok");
            case GetUsageStep s:
                var counters = await store.GetUsageAsync(new QuotaUsageQuery
                {
                    TenantId = scope.Wrap(universe.Tenants[s.Tenant]),
                    AgentName = s.Agent is { } agent ? (agent < 0 ? string.Empty : universe.Agents[agent]) : null,
                    Period = s.Period,
                    PeriodStarts = s.Starts is { } starts ? Starts(starts) : null,
                });

                return Observation.List("counters", [.. counters.Select(RenderCounter).Order(StringComparer.Ordinal)]);
            case ClaimQuotaStep s:
                var start = QuotaModel.StartOf(s.Start, s.Period) ?? new DateOnly(2026, 8, 3);

                return Observation.Of(await store.TryClaimThresholdNotificationAsync(
                    scope.Wrap(universe.Tenants[s.Tenant]),
                    s.Agent < 0 ? string.Empty : universe.Agents[s.Agent],
                    s.Period,
                    start,
                    s.Metric,
                    s.Percent) ? "true" : "false");
            default:
                throw new InvalidOperationException(step.GetType().Name);
        }
    }

    private static Dictionary<QuotaPeriod, DateOnly> Starts(PeriodStartsSpec spec)
    {
        var starts = new Dictionary<QuotaPeriod, DateOnly>();

        foreach (var period in Enum.GetValues<QuotaPeriod>())
        {
            if (QuotaModel.StartOf(spec, period) is { } start)
            {
                starts[period] = start;
            }
        }

        return starts;
    }

    private Guid Id(int slot) => _ids.GetOrAdd(slot, static _ => Guid.NewGuid());

    private string Render(QuotaDefinition rule)
        => QuotaModel.RenderRule(
            _ids.FirstOrDefault(pair => pair.Value == rule.Id).Key,
            scope.Unwrap(rule.TenantId) ?? "<null>",
            rule.AgentName,
            rule.Period,
            rule.MaxRuns,
            rule.MaxTokens,
            rule.MaxCost,
            rule.Enabled,
            rule.CreatedAt.UtcTicks,
            rule.UpdatedAt.UtcTicks);

    private string RenderCounter(QuotaUsageRecord counter)
        => QuotaModel.RenderCounter(scope.Unwrap(counter.TenantId) ?? "<null>", counter.AgentName, counter.Period, counter.PeriodStart, counter.Runs, counter.Tokens, counter.Cost);
}
