using System.Globalization;
using System.Text;
using CsCheck;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Tests.Differential;

/// <summary>The identifiers, tenants and attribution one run sequence draws from.</summary>
/// <param name="RunIds">Run identifiers.</param>
/// <param name="HomeTenants">For each run, the index of the tenant that starts it (a run keeps one tenant).</param>
/// <param name="Tenants">Tenant spellings, some of them twins of others.</param>
/// <param name="Agents">Agent names.</param>
/// <param name="Users">User identities.</param>
/// <param name="Sessions">Session identifiers.</param>
/// <param name="LabelSets">Label sets, each a list of key and value.</param>
/// <param name="Models">Model identifiers.</param>
/// <param name="Texts">Event texts.</param>
internal sealed record RunUniverse(
    Guid[] RunIds,
    int[] HomeTenants,
    string[] Tenants,
    string[] Agents,
    string[] Users,
    string[] Sessions,
    KeyValuePair<string, string>[][] LabelSets,
    string[] Models,
    string[] Texts);

/// <summary>A generated run sequence.</summary>
/// <param name="Universe">The values the steps draw from.</param>
/// <param name="Steps">The steps.</param>
internal sealed record RunPlan(RunUniverse Universe, IReadOnlyList<RunStep> Steps);

/// <summary>Which sequence number an appended event carries.</summary>
internal enum SequenceHint
{
    /// <summary>One past the highest written, or zero for a run without events.</summary>
    Next,

    /// <summary>The highest written (a repeat).</summary>
    Repeat,

    /// <summary>Five past the highest written (a gap).</summary>
    Gap,

    /// <summary>Zero.</summary>
    Zero,

    /// <summary>Minus one.</summary>
    Negative,
}

/// <summary>One call on a run store.</summary>
/// <param name="Ambient">The ambient tenant the call runs under.</param>
internal abstract record RunStep(int Ambient);

/// <summary>A step that does nothing; the model puts it in place of a call whose result the interface leaves open.</summary>
internal sealed record NoopStep(int Ambient) : RunStep(Ambient);

/// <summary>The fields of a run start.</summary>
/// <param name="Agent">Index into the agent names.</param>
/// <param name="Status">Running or queued.</param>
/// <param name="StartedAt">The start time.</param>
/// <param name="InfoTenant">Whether the start names its tenant (otherwise the ambient one applies).</param>
/// <param name="User">Index into the users, or <see langword="null"/>.</param>
/// <param name="Labels">Index into the label sets, or <see langword="null"/>.</param>
/// <param name="Session">Index into the sessions, or <see langword="null"/>.</param>
/// <param name="Model">Index into the models, or <see langword="null"/>.</param>
/// <param name="Streaming">Whether the run streams.</param>
internal sealed record RunStartSpec(int Agent, RunStatus Status, DateTimeOffset StartedAt, bool InfoTenant, int? User, int? Labels, int? Session, int? Model, bool Streaming);

/// <summary>The fields of a run completion.</summary>
/// <param name="Status">The terminal status.</param>
/// <param name="CompletedAt">The completion time.</param>
/// <param name="EventCount">The event count the writer reports.</param>
/// <param name="Usage">The token counts (input, output, total), or <see langword="null"/>.</param>
/// <param name="Error">The error type and message, or <see langword="null"/>.</param>
/// <param name="Model">Index into the models for a fallback override, or <see langword="null"/>.</param>
/// <param name="ExpectedTenant">Whether the completion names the run's tenant.</param>
internal sealed record RunCompleteSpec(RunStatus Status, DateTimeOffset CompletedAt, long EventCount, long?[]? Usage, string[]? Error, int? Model, int ExpectedTenant);

/// <summary>A run start (an upsert).</summary>
internal sealed record StartRunStep(int Ambient, int Run, RunStartSpec Spec) : RunStep(Ambient);

/// <summary>An event append.</summary>
internal sealed record AppendEventStep(int Ambient, int Run, SequenceHint Hint, long Sequence, int Text, RunEventType Type, int ExpectedTenant) : RunStep(Ambient);

/// <summary>A read of the highest event sequence.</summary>
internal sealed record LastSequenceStep(int Ambient, int Run) : RunStep(Ambient);

/// <summary>A read of a run's events.</summary>
internal sealed record ReadEventsStep(int Ambient, int Run, long From) : RunStep(Ambient);

/// <summary>A run completion.</summary>
internal sealed record CompleteRunStep(int Ambient, int Run, RunCompleteSpec Spec) : RunStep(Ambient);

/// <summary>A read of one run.</summary>
internal sealed record GetRunStep(int Ambient, int Run) : RunStep(Ambient);

/// <summary>A run listing.</summary>
internal sealed record QueryRunsStep(int Ambient, int? Agent, RunStatus? Status, int? QueryTenant, int? User, int? Session, int? LabelSet, bool LabelValueToo, DateTimeOffset? After, int Skip, int Take) : RunStep(Ambient);

/// <summary>
/// The run store state machine: start (the upsert and its attribution rule), event append and read, completion, read and
/// list, over runs that each keep one tenant.
/// </summary>
internal sealed class RunStateMachine : StateMachine<RunPlan, RunStep>
{
    private static readonly int[] Limits = [0, 1, 2, 3, 5, 100, int.MaxValue, -1, int.MinValue];

    private static readonly DateTimeOffset Origin = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private const int RaceRunCount = 3;

    /// <inheritdoc />
    public override string Name => "IRunStore";

    /// <inheritdoc />
    public override Gen<RunPlan> Plans(int maxSteps, ProviderTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        // Label keys and values come from small pools that contain twins of each other, so a filter on one key meets
        // another that differs only in a way a lenient comparison ignores.
        var labelSet =
            from keys in BoundaryValues.Pool(3, BoundaryValues.Trimmed(64), 64)
            from values in BoundaryValues.Pool(3, BoundaryValues.Text(256), 256)
            from count in Gen.Int[1, 2]
            from picks in Gen.Int[0, 2].Array[count]
            from valuePicks in Gen.Int[0, 2].Array[count]
            select picks.Select((pick, index) => KeyValuePair.Create(keys[pick], values[valuePicks[index]]))
                .DistinctBy(static pair => pair.Key, StringComparer.Ordinal)
                .ToArray();

        var universe =
            from tenants in BoundaryValues.TenantPool(3)
            from home in Gen.Int[0, 2].Array[3]
            from agents in BoundaryValues.Pool(3, BoundaryValues.Trimmed(200), 200)
            from users in BoundaryValues.Pool(3, BoundaryValues.Trimmed(200), 200)
            from sessions in BoundaryValues.Pool(2, BoundaryValues.Trimmed(200), 200)
            from labels in labelSet.Array[3]
            from models in BoundaryValues.Trimmed(100).Array[2]
            from texts in BoundaryValues.Text(60).Array[3]
            from ids in Gen.Guid.Array[3]
            select new RunUniverse(ids, home, tenants, agents, users, sessions, labels, models, texts);

        return universe.SelectMany(
            u => Step(u).List[1, maxSteps],
            static (u, steps) => new RunPlan(u, steps));
    }

    /// <inheritdoc />
    public override IReadOnlyList<RunStep> Steps(RunPlan plan) => plan.Steps;

    /// <inheritdoc />
    public override IStepModel<RunStep> NewModel(RunPlan plan, ProviderTraits traits) => new RunModel(plan.Universe, traits);

    /// <inheritdoc />
    public override IStepDriver<RunStep> NewDriver(RunPlan plan, DifferentialStores stores, DifferentialScope scope)
        => new RunDriver(DifferentialStores.Require(stores.Runs), tenant => stores.Tenant.TenantId = tenant, scope, plan.Universe);

    /// <inheritdoc />
    public override IStepDriver<RunStep> NewRaceDriver(RunPlan plan, DifferentialStores stores, DifferentialScope scope)
    {
        // The calls of a race run in several tasks and name different ambient tenants: the store of a race reads the tenant
        // its own flow set, not one value every task shares.
        var flow = stores.FlowTenant ?? throw new InvalidOperationException("The provider did not supply a flow tenant context.");

        return new RunDriver(DifferentialStores.Require(stores.FlowRuns), flow.Set, scope, plan.Universe);
    }

    /// <inheritdoc />
    public override IReadOnlyList<RunStep> RaceSteps(RunPlan plan)
    {
        // Three runs are started, one after the other, before the tasks start. The tasks then start them again, append
        // events, complete, read and list, each under an ambient tenant of its own. A start that races a completion of the
        // same run is left out: what a start does to a finished run is open, and the order of the two is what a race decides.
        var racing = plan.Steps
            .Where(static step => step is not (NoopStep or QueryRunsStep))
            .Take(6)
            .Select(static step => step switch
            {
                StartRunStep s => s with { Run = s.Run % RaceRunCount },
                AppendEventStep s => s with { Run = s.Run % RaceRunCount },
                LastSequenceStep s => s with { Run = s.Run % RaceRunCount },
                ReadEventsStep s => s with { Run = s.Run % RaceRunCount },
                CompleteRunStep s => s with { Run = s.Run % RaceRunCount },
                GetRunStep s => s with { Run = s.Run % RaceRunCount },
                _ => step,
            })
            .ToList();
        var completed = racing.OfType<CompleteRunStep>().Select(static step => step.Run).ToHashSet();

        return
        [
            .. Enumerable.Range(0, RaceRunCount).Select(run => (RunStep)new StartRunStep(plan.Universe.HomeTenants[run], run, new RunStartSpec(0, RunStatus.Running, Origin, false, null, null, null, null, false))),
            .. racing.Where(step => step is not StartRunStep start || !completed.Contains(start.Run)),
        ];
    }

    /// <inheritdoc />
    public override int? RaceSetupLength(RunPlan plan) => RaceRunCount;

    /// <inheritdoc />
    public override IReadOnlyList<RunStep> Snapshot(RunPlan plan)
        => [.. Enumerable.Range(0, RaceRunCount).SelectMany(run => new RunStep[] { new GetRunStep(plan.Universe.HomeTenants[run], run), new ReadEventsStep(plan.Universe.HomeTenants[run], run, 0), new LastSequenceStep(plan.Universe.HomeTenants[run], run) })];

    /// <inheritdoc />
    public override string Describe(RunStep step) => step switch
    {
        NoopStep => "(no-op: the interface leaves the outcome of this call open)",
        StartRunStep s => $"Start ambient={s.Ambient} run={s.Run} {s.Spec}",
        AppendEventStep s => $"Append ambient={s.Ambient} run={s.Run} seq={s.Sequence} ({s.Hint}) text={s.Text} type={s.Type} expectedTenant={s.ExpectedTenant}",
        LastSequenceStep s => $"LastSequence ambient={s.Ambient} run={s.Run}",
        ReadEventsStep s => $"ReadEvents ambient={s.Ambient} run={s.Run} from={s.From}",
        CompleteRunStep s => $"Complete ambient={s.Ambient} run={s.Run} {s.Spec.Status} at={BoundaryValues.Show(s.Spec.CompletedAt)} events={s.Spec.EventCount} usage={(s.Spec.Usage is { } u ? string.Join("/", u.Select(static v => v?.ToString(CultureInfo.InvariantCulture) ?? "-")) : "-")} error={(s.Spec.Error is { } e ? BoundaryValues.Show(e[0]) + ":" + BoundaryValues.Show(e[1]) : "-")} model={s.Spec.Model?.ToString(CultureInfo.InvariantCulture) ?? "-"} expectedTenant={s.Spec.ExpectedTenant}",
        GetRunStep s => $"Get ambient={s.Ambient} run={s.Run}",
        QueryRunsStep s => $"Query ambient={s.Ambient} agent={s.Agent?.ToString(CultureInfo.InvariantCulture) ?? "-"} status={s.Status?.ToString() ?? "-"} tenant={s.QueryTenant?.ToString(CultureInfo.InvariantCulture) ?? "-"} user={s.User?.ToString(CultureInfo.InvariantCulture) ?? "-"} session={s.Session?.ToString(CultureInfo.InvariantCulture) ?? "-"} labelSet={s.LabelSet?.ToString(CultureInfo.InvariantCulture) ?? "-"}(value={s.LabelValueToo}) after={(s.After is { } at ? BoundaryValues.Show(at) : "-")} skip={s.Skip} take={s.Take}",
        _ => step.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public override string Print(RunPlan plan)
    {
        var builder = new StringBuilder("\n");
        SessionStateMachine.Pool(builder, "tenants", plan.Universe.Tenants);
        SessionStateMachine.Pool(builder, "agents", plan.Universe.Agents);
        SessionStateMachine.Pool(builder, "users", plan.Universe.Users);
        SessionStateMachine.Pool(builder, "sessions", plan.Universe.Sessions);
        SessionStateMachine.Pool(builder, "models", plan.Universe.Models);
        SessionStateMachine.Pool(builder, "texts", plan.Universe.Texts);
        builder.Append("  home tenants = ").AppendLine(string.Join(",", plan.Universe.HomeTenants));

        for (var index = 0; index < plan.Universe.LabelSets.Length; index++)
        {
            builder.Append("  labelSets[").Append(index).Append("] = ").AppendLine(RunModel.RenderLabels(plan.Universe.LabelSets[index]));
        }

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            builder.Append("  ").Append(index + 1).Append(". ").AppendLine(Describe(plan.Steps[index]));
        }

        return builder.ToString();
    }

    private static Gen<RunStep> Step(RunUniverse u)
    {
        var run = Gen.Int[0, u.RunIds.Length - 1];
        var tenant = Gen.Int[0, u.Tenants.Length - 1];
        var optional = (int count) => Gen.Frequency((1, Gen.Const((int?)null)), (2, Gen.Int[0, count - 1].Select(static value => (int?)value)));
        var limit = Gen.Frequency((3, Gen.OneOf(Limits)), (2, Gen.Int[0, 6]));
        var startedAt = Gen.Frequency(
            (8, Gen.Int[0, 3_000].Select(static ms => Origin.AddMilliseconds(ms))),
            (3, Gen.Int[0, 3].Select(static ms => Origin.AddMilliseconds(ms))),
            (1, BoundaryValues.Instant));
        var usage = Gen.Frequency(
            (3, Gen.Const((long?[]?)null)),
            (4, Gen.OneOf<long?>(null, 0, 1, 12_345, long.MaxValue).Array[3].Where(static values => values.Any(static value => value is not null)).Select(static values => (long?[]?)values)));
        var error = Gen.Frequency(
            (4, Gen.Const((string[]?)null)),
            (2, BoundaryValues.Trimmed(200).SelectMany(type => BoundaryValues.Text(60).Select(message => (string[]?)new[] { type, message }))));

        var start =
            from ambient in tenant
            from r in run
            from agent in Gen.Int[0, u.Agents.Length - 1]
            from status in Gen.OneOf(RunStatus.Running, RunStatus.Queued)
            from at in startedAt
            from infoTenant in Gen.Bool
            from user in optional(u.Users.Length)
            from labels in optional(u.LabelSets.Length)
            from session in optional(u.Sessions.Length)
            from model in optional(u.Models.Length)
            from streaming in Gen.Bool
            select (RunStep)new StartRunStep(ambient, r, new RunStartSpec(agent, status, at, infoTenant, user, labels, session, model, streaming));

        var append =
            from ambient in tenant
            from r in run
            from hint in Gen.Frequency((12, Gen.Const(SequenceHint.Next)), (2, Gen.Const(SequenceHint.Repeat)), (1, Gen.Const(SequenceHint.Gap)), (1, Gen.Const(SequenceHint.Zero)), (1, Gen.Const(SequenceHint.Negative)))
            from text in Gen.Int[0, u.Texts.Length - 1]
            from type in Gen.OneOf(RunEventType.MessageDelta, RunEventType.ToolInvoking, RunEventType.Custom)
            from expected in Gen.Frequency((4, Gen.Const(-1)), (3, Gen.Const(-3)), (1, Gen.Int[0, u.Tenants.Length - 1]))
            select (RunStep)new AppendEventStep(ambient, r, hint, 0, text, type, expected);

        var complete =
            from ambient in tenant
            from r in run
            from status in Gen.OneOf(RunStatus.Completed, RunStatus.Failed, RunStatus.Canceled)
            from at in startedAt
            from events in Gen.OneOf(0L, 1L, 7L, long.MaxValue)
            from use in usage
            from err in error
            from model in optional(u.Models.Length)
            from expected in Gen.Frequency((4, Gen.Const(-1)), (3, Gen.Const(-3)), (1, Gen.Int[0, u.Tenants.Length - 1]))
            select (RunStep)new CompleteRunStep(ambient, r, new RunCompleteSpec(status, at, events, use, err, model, expected));

        var query =
            from ambient in tenant
            from agent in optional(u.Agents.Length)
            from status in Gen.Frequency((3, Gen.Const((RunStatus?)null)), (2, Gen.OneOf(RunStatus.Running, RunStatus.Queued, RunStatus.Completed, RunStatus.Failed).Select(static value => (RunStatus?)value)))
            from queryTenant in Gen.Frequency((8, Gen.Const((int?)null)), (2, tenant.Select(static value => (int?)value)))
            from user in optional(u.Users.Length)
            from session in optional(u.Sessions.Length)
            from labelSet in optional(u.LabelSets.Length)
            from valueToo in Gen.Bool
            from after in Gen.Frequency((6, Gen.Const((DateTimeOffset?)null)), (2, Gen.Int[0, 3_000].Select(static ms => (DateTimeOffset?)Origin.AddMilliseconds(ms + 0.5))))
            from skip in limit
            from take in limit
            select (RunStep)new QueryRunsStep(ambient, agent, status, queryTenant, user, session, labelSet, valueToo, after, skip, take);

        return Gen.Frequency(
            (22, start),
            (24, append),
            (6, tenant.SelectMany(t => run.Select(r => (RunStep)new LastSequenceStep(t, r)))),
            (12, tenant.SelectMany(t => run.SelectMany(r => Gen.OneOf(0L, 0L, 1L, 3L, -1L).Select(from => (RunStep)new ReadEventsStep(t, r, from))))),
            (12, complete),
            (10, tenant.SelectMany(t => run.Select(r => (RunStep)new GetRunStep(t, r)))),
            (18, query));
    }
}

/// <summary>The run specification, written from the interface documentation.</summary>
internal sealed class RunModel(RunUniverse universe, ProviderTraits traits) : IStepModel<RunStep>
{
    private readonly Dictionary<int, Run> _runs = [];

    /// <inheritdoc />
    public RunStep Concretize(RunStep step)
    {
        switch (step)
        {
            case StartRunStep s when _runs.TryGetValue(s.Run, out var existing) && existing.Terminal:
                return new NoopStep(s.Ambient);
            case StartRunStep s when !s.Spec.InfoTenant:
                // A start that does not name its tenant runs under the ambient one, so the ambient one is the run's own.
                return s with { Ambient = universe.HomeTenants[s.Run] };
            case AppendEventStep s:
                var last = _runs.TryGetValue(s.Run, out var run) && run.Events.Count > 0 ? run.Events.Keys.Max() : -1L;

                return s with
                {
                    Sequence = s.Hint switch
                    {
                        SequenceHint.Next => last + 1,
                        SequenceHint.Repeat => Math.Max(last, 0),
                        SequenceHint.Gap => last + 5,
                        SequenceHint.Zero => 0,
                        _ => -1,
                    },
                };
            default:
                return step;
        }
    }

    /// <inheritdoc />
    public Observation Apply(RunStep step)
    {
        try
        {
            return step switch
            {
                NoopStep => Observation.Of("noop"),
                StartRunStep s => Start(s),
                AppendEventStep s => Append(s),
                LastSequenceStep s => Observation.Of(_runs.TryGetValue(s.Run, out var run) && run.Events.Count > 0 ? run.Events.Keys.Max().ToString(CultureInfo.InvariantCulture) : "null"),
                ReadEventsStep s => Read(s),
                CompleteRunStep s => Complete(s),
                GetRunStep s => Get(s),
                QueryRunsStep s => Query(s),
                _ => throw new InvalidOperationException(step.GetType().Name),
            };
        }
        catch (ArgumentException)
        {
            return Observation.Rejected();
        }
    }

    internal static string RenderLabels(IEnumerable<KeyValuePair<string, string>>? labels)
        => labels is null
            ? "null"
            : "{" + string.Join(";", labels.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => BoundaryValues.Show(pair.Key) + "=" + BoundaryValues.Show(pair.Value))) + "}";

    internal static string RenderOf(string run, string tenant, string agent, RunStatus status, long started, long? completed, string? user, string labels, string? session, string? model, bool streaming, long events, string usage, string error)
        => $"run#{run} tenant={BoundaryValues.Show(tenant)} agent={BoundaryValues.Show(agent)} status={status} started={started} completed={completed?.ToString(CultureInfo.InvariantCulture) ?? "null"} user={BoundaryValues.Show(user)} labels={labels} session={BoundaryValues.Show(session)} model={BoundaryValues.Show(model)} streaming={streaming} events={events} usage={usage} error={error}";

    private static string Fold(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentException("blank tenant", nameof(tenant));
        }

        return tenant.ToLowerInvariant();
    }

    private string TenantOf(int index) => traits.Text(Fold(universe.Tenants[index]));

    private static string Render(int slot, Run run)
        => RenderOf(
            slot.ToString(CultureInfo.InvariantCulture),
            run.Tenant,
            run.Agent,
            run.Status,
            run.Started,
            run.Completed,
            run.User,
            RenderLabels(run.Labels),
            run.Session,
            run.Model,
            run.Streaming,
            run.EventCount,
            run.Usage,
            run.Error);

    private SortedDictionary<string, string>? LabelsOf(int? set)
    {
        if (set is not { } index)
        {
            return null;
        }

        // Two keys that become one after the store's text rule leave the later value: the map is read back from JSON.
        var labels = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in universe.LabelSets[index])
        {
            labels[traits.Text(pair.Key)] = traits.Text(pair.Value);
        }

        return labels;
    }

    private Observation Start(StartRunStep s)
    {
        var spec = s.Spec;
        var tenant = TenantOf(universe.HomeTenants[s.Run]);
        var labels = LabelsOf(spec.Labels);
        var user = spec.User is { } u ? traits.Text(universe.Users[u]) : null;
        Run run;

        if (_runs.TryGetValue(s.Run, out var existing))
        {
            run = existing;
            run.Agent = traits.Text(universe.Agents[spec.Agent]);
            run.Status = spec.Status;
            run.Started = traits.Ticks(spec.StartedAt);
            run.User = user ?? run.User;
            run.Labels = labels ?? run.Labels;
            run.Session = spec.Session is { } es ? traits.Text(universe.Sessions[es]) : null;
            run.Model = spec.Model is { } em ? traits.Text(universe.Models[em]) : null;
            run.Streaming = spec.Streaming;
        }
        else
        {
            run = new Run
            {
                Tenant = tenant,
                Agent = traits.Text(universe.Agents[spec.Agent]),
                Status = spec.Status,
                Started = traits.Ticks(spec.StartedAt),
                User = user,
                Labels = labels,
                Session = spec.Session is { } session ? traits.Text(universe.Sessions[session]) : null,
                Model = spec.Model is { } model ? traits.Text(universe.Models[model]) : null,
                Streaming = spec.Streaming,
            };
            _runs[s.Run] = run;
        }

        if (!traits.CreateEchoesInputText)
        {
            return Observation.Of(Render(s.Run, run));
        }

        // The returned record is built from the call: its own text and time, with the attribution the row kept.
        return Observation.Of(RenderStatic(
            s.Run.ToString(CultureInfo.InvariantCulture),
            Fold(universe.Tenants[universe.HomeTenants[s.Run]]),
            universe.Agents[spec.Agent],
            spec.Status,
            spec.StartedAt.UtcTicks,
            run.User,
            RenderLabels(run.Labels),
            spec.Session is { } sx ? universe.Sessions[sx] : null,
            spec.Model is { } mx ? universe.Models[mx] : null,
            spec.Streaming,
            run));
    }

    private static string RenderStatic(string slot, string tenant, string agent, RunStatus status, long started, string? user, string labels, string? session, string? model, bool streaming, Run run)
        => RenderOf(slot, tenant, agent, status, started, run.Completed, user, labels, session, model, streaming, run.EventCount, run.Usage, run.Error);

    private Observation Append(AppendEventStep s)
    {
        if (!_runs.TryGetValue(s.Run, out var run))
        {
            return Observation.Of("throws TraconException");
        }

        if (ExpectedMismatch(s.ExpectedTenant, s.Run, run))
        {
            return Observation.Of("throws TraconException");
        }

        if (run.Events.ContainsKey(s.Sequence))
        {
            return Observation.Of("throws TraconException");
        }

        run.Events[s.Sequence] = $"{s.Type}:{BoundaryValues.Show(traits.Text(universe.Texts[s.Text]))}";

        return Observation.Of("ok");
    }

    private bool ExpectedMismatch(int reference, int slot, Run run)
    {
        // -1: the call names no tenant (no check). -3: it names the run's own tenant. Otherwise it names the tenant at that index.
        if (reference == -1)
        {
            return false;
        }

        var expected = reference == -3 ? TenantOf(universe.HomeTenants[slot]) : TenantOf(reference);

        return !string.Equals(expected, run.Tenant, StringComparison.Ordinal);
    }

    private Observation Read(ReadEventsStep s)
    {
        var ambient = TenantOf(s.Ambient);

        if (!_runs.TryGetValue(s.Run, out var run) || !string.Equals(run.Tenant, ambient, StringComparison.Ordinal))
        {
            return Observation.List("events", []);
        }

        return Observation.List(
            "events",
            [.. run.Events.Where(pair => pair.Key >= s.From).OrderBy(static pair => pair.Key).Select(static pair => $"{pair.Key} {pair.Value}")]);
    }

    private Observation Complete(CompleteRunStep s)
    {
        if (!_runs.TryGetValue(s.Run, out var run))
        {
            return Observation.Of("throws TraconException");
        }

        var spec = s.Spec;

        if (ExpectedMismatch(spec.ExpectedTenant, s.Run, run))
        {
            return Observation.Of("throws TraconException");
        }

        run.Status = spec.Status;
        run.Completed = traits.Ticks(spec.CompletedAt);
        run.EventCount = spec.EventCount;
        run.Usage = spec.Usage is { } u ? $"{u[0]?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{u[1]?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{u[2]?.ToString(CultureInfo.InvariantCulture) ?? "-"}" : "null";
        run.Error = spec.Error is { } e ? $"{BoundaryValues.Show(traits.Text(e[0]))}:{BoundaryValues.Show(traits.Text(e[1]))}" : "null";

        if (spec.Model is { } model)
        {
            run.Model = traits.Text(universe.Models[model]);
        }

        run.Terminal = true;

        return Observation.Of("ok");
    }

    private Observation Get(GetRunStep s)
    {
        var ambient = TenantOf(s.Ambient);

        return Observation.Of(_runs.TryGetValue(s.Run, out var run) && string.Equals(run.Tenant, ambient, StringComparison.Ordinal) ? Render(s.Run, run) : "null");
    }

    private Observation Query(QueryRunsStep s)
    {
        var tenant = s.QueryTenant is { } q ? TenantOf(q) : TenantOf(s.Ambient);
        var agent = s.Agent is { } a ? traits.Text(universe.Agents[a]) : null;
        var user = s.User is { } u ? traits.Text(universe.Users[u]) : null;
        var session = s.Session is { } se ? traits.Text(universe.Sessions[se]) : null;
        var after = s.After is { } at ? traits.Ticks(at) : (long?)null;

        var sorted = _runs
            .Where(pair => string.Equals(pair.Value.Tenant, tenant, StringComparison.Ordinal))
            .Where(pair => agent is null || string.Equals(pair.Value.Agent, agent, StringComparison.Ordinal))
            .Where(pair => s.Status is null || pair.Value.Status == s.Status)
            .Where(pair => user is null || string.Equals(pair.Value.User, user, StringComparison.Ordinal))
            .Where(pair => session is null || string.Equals(pair.Value.Session, session, StringComparison.Ordinal))
            .Where(pair => after is null || pair.Value.Started > after)
            .Where(pair => LabelMatches(pair.Value.Labels, s))
            .OrderByDescending(static pair => pair.Value.Started)
            .Select(pair => (Key: pair.Value.Started, Rendered: Render(pair.Key, pair.Value)))
            .ToList();

        return TiedPage.Cut("runs", sorted, Math.Max(s.Skip, 0), Math.Max(s.Take, 0));
    }

    private bool LabelMatches(SortedDictionary<string, string>? labels, QueryRunsStep s)
    {
        if (s.LabelSet is not { } set)
        {
            return true;
        }

        // The filter asks for the first pair of the set: its key, and its value too when asked.
        var pair = universe.LabelSets[set][0];
        var key = traits.Text(pair.Key);

        if (labels is null || !labels.TryGetValue(key, out var value))
        {
            return false;
        }

        return !s.LabelValueToo || string.Equals(value, traits.Text(pair.Value), StringComparison.Ordinal);
    }

    private sealed class Run
    {
        public string Tenant { get; init; } = string.Empty;

        public string Agent { get; set; } = string.Empty;

        public RunStatus Status { get; set; }

        public long Started { get; set; }

        public long? Completed { get; set; }

        public string? User { get; set; }

        public SortedDictionary<string, string>? Labels { get; set; }

        public string? Session { get; set; }

        public string? Model { get; set; }

        public bool Streaming { get; set; }

        public long EventCount { get; set; }

        public string Usage { get; set; } = "null";

        public string Error { get; set; } = "null";

        public bool Terminal { get; set; }

        public SortedDictionary<long, string> Events { get; } = [];
    }
}

/// <summary>Applies run steps to a store.</summary>
internal sealed class RunDriver(IRunStore store, Action<string> setAmbientTenant, DifferentialScope scope, RunUniverse universe) : IStepDriver<RunStep>
{
    // A run identifier belongs to the run of one sequence in one tenant namespace: the same plan replays (while a failure
    // shrinks, and once per repeat of a race) must not meet the runs an earlier replay left under another tenant.
    private readonly Guid[] _runIds = [.. universe.RunIds.Select(static _ => Guid.NewGuid())];

    /// <inheritdoc />
    public async ValueTask<Observation> ApplyAsync(RunStep step)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await ApplyOnceAsync(step);
            }
            catch (System.Data.Common.DbException exception) when (attempt < 4 && exception.Message.Contains("deadlock", StringComparison.OrdinalIgnoreCase))
            {
                // OPEN FINDING: on SQL Server a read or an event append that meets a concurrent upsert of the same run can be chosen
                // as the deadlock victim ("Rerun the transaction"), and the store does not run it again (its transactional writes
                // are). The victim's statement is rolled back whole, so the driver runs it again, as the caller of a 503 would; the
                // race is judged on what the calls finally returned.
                await Task.Delay(TimeSpan.FromMilliseconds(10 * attempt));
            }
        }
    }

    private async ValueTask<Observation> ApplyOnceAsync(RunStep step)
    {
        setAmbientTenant(scope.Wrap(universe.Tenants[step.Ambient]));

        switch (step)
        {
            case NoopStep:
                return Observation.Of("noop");
            case StartRunStep s:
                var spec = s.Spec;
                var record = await store.StartRunAsync(new RunStartInfo
                {
                    RunId = _runIds[s.Run],
                    AgentName = universe.Agents[spec.Agent],
                    StartedAt = spec.StartedAt,
                    Status = spec.Status,
                    TenantId = spec.InfoTenant ? scope.Wrap(universe.Tenants[universe.HomeTenants[s.Run]]) : null,
                    UserId = spec.User is { } u ? universe.Users[u] : null,
                    Labels = spec.Labels is { } l ? universe.LabelSets[l].ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal) : null,
                    SessionId = spec.Session is { } se ? universe.Sessions[se] : null,
                    ModelId = spec.Model is { } m ? universe.Models[m] : null,
                    IsStreaming = spec.Streaming,
                });

                return Observation.Of(Render(record));
            case AppendEventStep s:
                await store.AppendEventAsync(new RunEvent
                {
                    RunId = _runIds[s.Run],
                    Sequence = s.Sequence,
                    Type = s.Type,
                    Timestamp = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero),
                    Text = universe.Texts[s.Text],
                    CustomType = s.Type == RunEventType.Custom ? "differential.custom" : null,
                    TenantId = ExpectedTenant(s.ExpectedTenant, s.Run),
                });

                return Observation.Of("ok");
            case LastSequenceStep s:
                return Observation.Of((await store.GetLastEventSequenceAsync(_runIds[s.Run]))?.ToString(CultureInfo.InvariantCulture) ?? "null");
            case ReadEventsStep s:
                var events = new List<string>();

                await foreach (var runEvent in store.ReadEventsAsync(_runIds[s.Run], s.From))
                {
                    events.Add($"{runEvent.Sequence} {runEvent.Type}:{BoundaryValues.Show(runEvent.Text)}");
                }

                return Observation.List("events", events);
            case CompleteRunStep s:
                await store.CompleteRunAsync(new RunCompletion
                {
                    RunId = _runIds[s.Run],
                    Status = s.Spec.Status,
                    CompletedAt = s.Spec.CompletedAt,
                    EventCount = s.Spec.EventCount,
                    Usage = s.Spec.Usage is { } usage ? new RunUsage { InputTokens = usage[0], OutputTokens = usage[1], TotalTokens = usage[2] } : null,
                    Error = s.Spec.Error is { } error ? new RunError { Type = error[0], Message = error[1] } : null,
                    ModelId = s.Spec.Model is { } model ? universe.Models[model] : null,
                    TenantId = ExpectedTenant(s.Spec.ExpectedTenant, s.Run),
                });

                return Observation.Of("ok");
            case GetRunStep s:
                return Observation.Of(await store.GetRunAsync(_runIds[s.Run]) is { } found ? Render(found) : "null");
            case QueryRunsStep s:
                var wanted = s.LabelSet is { } set && universe.LabelSets[set].Length > 0 ? universe.LabelSets[set][0] : (KeyValuePair<string, string>?)null;
                var list = await store.QueryRunsAsync(new RunQuery
                {
                    AgentName = s.Agent is { } a ? universe.Agents[a] : null,
                    Status = s.Status,
                    TenantId = s.QueryTenant is { } q ? scope.Wrap(universe.Tenants[q]) : null,
                    UserId = s.User is { } us ? universe.Users[us] : null,
                    SessionId = s.Session is { } ss ? universe.Sessions[ss] : null,
                    LabelKey = wanted?.Key,
                    LabelValue = wanted is not null && s.LabelValueToo ? wanted.Value.Value : null,
                    StartedAfter = s.After,
                    Skip = s.Skip,
                    Take = s.Take,
                });

                return Observation.List("runs", [.. list.Select(Render)]);
            default:
                throw new InvalidOperationException(step.GetType().Name);
        }
    }

    private string? ExpectedTenant(int reference, int slot) => reference switch
    {
        -1 => null,
        -3 => scope.Wrap(universe.Tenants[universe.HomeTenants[slot]]),
        _ => scope.Wrap(universe.Tenants[reference]),
    };

    private string Render(RunRecord record)
    {
        var slot = Array.IndexOf(_runIds, record.Id);

        return RunModel.RenderOf(
            slot.ToString(CultureInfo.InvariantCulture),
            scope.Unwrap(record.TenantId) ?? "<null>",
            record.AgentName,
            record.Status,
            record.StartedAt.UtcTicks,
            record.CompletedAt?.UtcTicks,
            record.UserId,
            RunModel.RenderLabels(record.Labels),
            record.SessionId,
            record.ModelId,
            record.IsStreaming,
            record.EventCount,
            record.Usage is { } usage
                ? $"{usage.InputTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{usage.OutputTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"}/{usage.TotalTokens?.ToString(CultureInfo.InvariantCulture) ?? "-"}"
                : "null",
            record.Error is { } error ? $"{BoundaryValues.Show(error.Type)}:{BoundaryValues.Show(error.Message)}" : "null");
    }
}
