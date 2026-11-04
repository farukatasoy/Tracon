using System.Globalization;
using System.Text;
using System.Text.Json;
using CsCheck;
using Tracon.Tests.Common;

namespace Tracon.Tests.Differential;

/// <summary>The tenants, workers and texts one job sequence draws from.</summary>
/// <param name="Tenants">Tenant spellings, some of them twins of others.</param>
/// <param name="Owners">Worker names, some of them twins of others (differing in case or in a normalization form).</param>
/// <param name="Targets">Target names.</param>
/// <param name="Inputs">Item inputs.</param>
/// <param name="Payloads">Job payloads (JSON documents).</param>
/// <param name="Errors">Error messages.</param>
internal sealed record JobUniverse(string[] Tenants, string[] Owners, string[] Targets, string[] Inputs, string[] Payloads, string[] Errors);

/// <summary>A generated job sequence.</summary>
/// <param name="Universe">The values the steps draw from.</param>
/// <param name="Steps">The steps.</param>
internal sealed record JobPlan(JobUniverse Universe, IReadOnlyList<JobStep> Steps);

/// <summary>What a worker claims about the lease it holds.</summary>
internal enum LeaseHint
{
    /// <summary>The lease the job holds now (the worker and the attempt the job names).</summary>
    Current,

    /// <summary>The right worker with an earlier attempt, or a later one when there is none before.</summary>
    OtherAttempt,

    /// <summary>The right attempt with another worker.</summary>
    OtherOwner,

    /// <summary>A job that was never queued.</summary>
    UnknownJob,
}

/// <summary>One call on a job store.</summary>
internal abstract record JobStep;

/// <summary>The queuing of a job in a slot.</summary>
/// <param name="Slot">The slot (the job's identity in the sequence).</param>
/// <param name="Tenant">The tenant spelling.</param>
/// <param name="Handler">The handler key (an index into the fixed list).</param>
/// <param name="Lane">The lane the record carries; it can be an invalid name.</param>
/// <param name="ScheduledSeconds">Whole seconds from the start of the sequence at which the job is due (the sequence adds a fraction that is unique to the slot).</param>
/// <param name="ScheduledFlavor">How the due time is written: plain, with a sub-microsecond tick, or in another offset.</param>
/// <param name="CreatedSeconds">Whole seconds from the start of the sequence at which the job was created; equal values are common.</param>
/// <param name="CreatedFlavor">How the creation time is written.</param>
/// <param name="MaxDurationMs">The job's own time limit in whole milliseconds, or <see langword="null"/>.</param>
/// <param name="MaxAttempts">The job's own attempt limit, or <see langword="null"/>.</param>
/// <param name="Payload">The payload (an index).</param>
/// <param name="Inputs">The item inputs (indexes).</param>
internal sealed record EnqueueJobStep(int Slot, int Tenant, int Handler, string Lane, int ScheduledSeconds, TimeFlavor ScheduledFlavor, int CreatedSeconds, TimeFlavor CreatedFlavor, long? MaxDurationMs, int? MaxAttempts, int Payload, int[] Inputs) : JobStep;

/// <summary>How an instant is written to the store.</summary>
internal enum TimeFlavor
{
    /// <summary>UTC, whole microseconds.</summary>
    Plain,

    /// <summary>UTC with seven extra ticks, which a microsecond column cannot hold.</summary>
    SubMicrosecond,

    /// <summary>The same instant in another offset.</summary>
    OtherOffset,
}

/// <summary>The tenant ceiling of a lease request.</summary>
/// <param name="Handlers">The handler keys it counts (indexes).</param>
/// <param name="Max">The number of live leases a tenant may hold on them.</param>
internal sealed record CapSpec(int[] Handlers, int Max);

/// <summary>The request of a lease.</summary>
/// <param name="Owner">The worker.</param>
/// <param name="DurationMs">The lease duration in milliseconds.</param>
/// <param name="Lanes">The lanes the worker serves; <see langword="null"/> or empty applies no filter.</param>
/// <param name="Cap">The tenant ceiling, or <see langword="null"/>.</param>
internal sealed record LeaseJobStep(int Owner, long DurationMs, string[]? Lanes, CapSpec? Cap) : JobStep;

/// <summary>A step that names a lease.</summary>
internal abstract record LeaseHolderStep(int Slot, LeaseHint Hint, int Owner, int Attempt) : JobStep;

/// <summary>The extension of a lease.</summary>
internal sealed record RenewJobStep(int Slot, LeaseHint Hint, int Owner, int Attempt, long DurationMs) : LeaseHolderStep(Slot, Hint, Owner, Attempt);

/// <summary>The move of a leased job to running.</summary>
internal sealed record MarkRunningJobStep(int Slot, LeaseHint Hint, int Owner, int Attempt) : LeaseHolderStep(Slot, Hint, Owner, Attempt);

/// <summary>The finalizing of a job.</summary>
internal sealed record CompleteJobStep(int Slot, LeaseHint Hint, int Owner, int Attempt, JobStatus Status, int CompletedSeconds, int? Error) : LeaseHolderStep(Slot, Hint, Owner, Attempt);

/// <summary>The return of a job to the queue.</summary>
internal sealed record ReleaseJobStep(int Slot, LeaseHint Hint, int Owner, int Attempt, int Error, int? DelaySeconds) : LeaseHolderStep(Slot, Hint, Owner, Attempt);

/// <summary>The cancellation of a job on behalf of a tenant.</summary>
internal sealed record CancelJobStep(int Tenant, int Slot) : JobStep;

/// <summary>The read of a job on behalf of a tenant; a tenant of -1 is the tenant the job belongs to.</summary>
internal sealed record GetJobStep(int Tenant, int Slot) : JobStep;

/// <summary>The listing of jobs.</summary>
internal sealed record QueryJobsStep(int? Tenant, int? Handler, string? Lane, JobStatus? Status, bool UnknownSchedule, int Skip, int Take) : JobStep;

/// <summary>The listing of a job's items.</summary>
internal sealed record ListItemsStep(int Slot) : JobStep;

/// <summary>The report of an item's result.</summary>
internal sealed record ReportItemStep(int Slot, int Seq, JobItemStatus Status, int? Run, int? Error) : JobStep;

/// <summary>The count of the open jobs.</summary>
internal sealed record QueueDepthStep : JobStep;

/// <summary>The move of the clock.</summary>
internal sealed record AdvanceClockStep(int Seconds) : JobStep;

/// <summary>
/// The job store state machine: queuing, leasing (with lane and tenant-cap filters), the fenced writes of a lease
/// holder, cancellation, listing, item reports and the queue depth, with a clock the sequence moves.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Time.</strong> A lease is stamped and compared with ONE clock, and the sequence moves that clock, so an expired
/// lease is something a step does, not something a test waits for. Which clock it is depends on the store: the in-memory
/// store and SQLite read the clock of the node that calls them (they are built with a <see cref="TimeProvider"/>), while
/// the PostgreSQL and SQL Server stores read the database server's clock inside the statement and never the caller's. A
/// test cannot move a server's clock, so the provider's test project stands a one-row clock in the schema in its place
/// (<see cref="DifferentialClock"/>); the model does not know the difference, which is the point: the same sequence must
/// give the same answers whichever clock decides. The server's own function is covered where real time is the clock (the
/// store contracts and the clock-skew contract).
/// </para>
/// <para>
/// <strong>Order.</strong> The oldest ready job is the one with the earliest due time. The generator gives every job a
/// due time that no other job has (a fraction unique to the slot, and a retry delay unique to the slot), so the lease
/// never has to choose between equals; a listing is ordered by creation time, newest first, and the model leaves the
/// order inside a group of equal creation times open.
/// </para>
/// </remarks>
internal sealed class JobStateMachine : StateMachine<JobPlan, JobStep>
{
    internal const int SlotCount = 6;

    internal static readonly string[] Handlers = ["tracon.agent-batch", "tracon.workflow", "tracon.workflow-run", "tracon.webhook-delivery", "contoso.report"];

    internal static readonly string[] ValidLanes = ["default", "media", "a.b-c_1"];

    private static readonly string[] InvalidLanes = ["Media", string.Empty, "has space", "-lead", "media\n", "café"];

    private const int RaceJobCount = 3;

    private static readonly Gen<TimeFlavor> Flavor = Gen.Frequency(
        (8, Gen.Const(TimeFlavor.Plain)),
        (2, Gen.Const(TimeFlavor.SubMicrosecond)),
        (1, Gen.Const(TimeFlavor.OtherOffset)));

    /// <inheritdoc />
    public override string Name => "IJobStore";

    /// <inheritdoc />
    public override Gen<JobPlan> Plans(int maxSteps, ProviderTraits traits)
    {
        ArgumentNullException.ThrowIfNull(traits);

        var universe =
            from tenants in BoundaryValues.TenantPool(3)
            from owners in BoundaryValues.Pool(3, BoundaryValues.Trimmed(60), 60)
            from targets in BoundaryValues.Trimmed(100).Array[2]
            from inputs in Gen.Frequency((6, BoundaryValues.Text(120)), (1, Gen.Const(string.Empty))).Array[4]
            from payloads in BoundaryValues.JsonDocument.Array[2]
            from errors in BoundaryValues.Text(120).Array[3]
            select new JobUniverse(tenants, owners, targets, inputs, payloads, errors);

        return universe.SelectMany(
            u => Step(u).List[1, maxSteps],
            static (u, steps) => new JobPlan(u, steps));
    }

    /// <inheritdoc />
    public override IReadOnlyList<JobStep> Steps(JobPlan plan) => plan.Steps;

    /// <inheritdoc />
    public override IReadOnlyList<JobStep> RaceSteps(JobPlan plan)
    {
        // Three jobs are queued and one is leased before the tasks start; the tasks then lease, finalize, cancel and
        // report against them. The clock does not move while they run (a step that moves it has no serial order to
        // be compared with).
        var enqueues = plan.Steps.OfType<EnqueueJobStep>().Take(RaceJobCount).Select(static (step, index) => step with { Slot = index }).ToList();

        while (enqueues.Count < RaceJobCount)
        {
            enqueues.Add(new EnqueueJobStep(enqueues.Count, 0, 0, "default", enqueues.Count * 3, TimeFlavor.Plain, 0, TimeFlavor.Plain, null, null, 0, [0, 1]));
        }

        var racing = plan.Steps
            .Where(static step => step is not (EnqueueJobStep or AdvanceClockStep or QueryJobsStep or QueueDepthStep))
            .Select(static step => step switch
            {
                LeaseHolderStep holder => holder with { Slot = holder.Slot % RaceJobCount },
                CancelJobStep cancel => cancel with { Slot = cancel.Slot % RaceJobCount },
                GetJobStep get => get with { Slot = get.Slot % RaceJobCount },
                ListItemsStep items => items with { Slot = items.Slot % RaceJobCount },
                ReportItemStep report => report with { Slot = report.Slot % RaceJobCount, Seq = report.Seq % 3 },
                _ => step,
            });

        return [.. enqueues, new LeaseJobStep(0, 300_000, null, null), .. racing];
    }

    /// <inheritdoc />
    public override int? RaceSetupLength(JobPlan plan) => RaceJobCount + 1;

    /// <inheritdoc />
    public override IReadOnlyList<JobStep> Snapshot(JobPlan plan)
        => [.. Enumerable.Range(0, RaceJobCount).SelectMany(static slot => new JobStep[] { new GetJobStep(-1, slot), new ListItemsStep(slot) }), new QueueDepthStep()];

    /// <inheritdoc />
    public override IStepModel<JobStep> NewModel(JobPlan plan, ProviderTraits traits) => new JobModel(plan.Universe, traits);

    /// <inheritdoc />
    public override IStepDriver<JobStep> NewDriver(JobPlan plan, DifferentialStores stores, DifferentialScope scope)
        => new JobDriver(DifferentialStores.Require(stores.Jobs), DifferentialStores.Require(stores.Clock), scope, plan.Universe);

    /// <inheritdoc />
    public override string Describe(JobStep step) => step switch
    {
        EnqueueJobStep s => $"Enqueue slot={s.Slot} tenant={s.Tenant} handler={s.Handler} lane={BoundaryValues.Show(s.Lane)} due={s.ScheduledSeconds}s/{s.ScheduledFlavor} created={s.CreatedSeconds}s/{s.CreatedFlavor} maxDuration={s.MaxDurationMs?.ToString(CultureInfo.InvariantCulture) ?? "-"}ms maxAttempts={s.MaxAttempts?.ToString(CultureInfo.InvariantCulture) ?? "-"} payload={s.Payload} items=[{string.Join(",", s.Inputs)}]",
        LeaseJobStep s => $"Lease owner={s.Owner} for={s.DurationMs}ms lanes={(s.Lanes is null ? "null" : "[" + string.Join(",", s.Lanes.Select(BoundaryValues.Show)) + "]")} cap={(s.Cap is null ? "-" : $"{s.Cap.Max} on [{string.Join(",", s.Cap.Handlers)}]")}",
        RenewJobStep s => $"Renew slot={s.Slot} lease={s.Hint}(owner={s.Owner} attempt={s.Attempt}) for={s.DurationMs}ms",
        MarkRunningJobStep s => $"MarkRunning slot={s.Slot} lease={s.Hint}(owner={s.Owner} attempt={s.Attempt})",
        CompleteJobStep s => $"Complete slot={s.Slot} lease={s.Hint}(owner={s.Owner} attempt={s.Attempt}) status={s.Status} at={s.CompletedSeconds}s error={s.Error?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
        ReleaseJobStep s => $"Release slot={s.Slot} lease={s.Hint}(owner={s.Owner} attempt={s.Attempt}) error={s.Error} delay={s.DelaySeconds?.ToString(CultureInfo.InvariantCulture) ?? "-"}s",
        CancelJobStep s => $"Cancel tenant={s.Tenant} slot={s.Slot}",
        GetJobStep s => $"Get tenant={s.Tenant} slot={s.Slot}",
        QueryJobsStep s => $"Query tenant={s.Tenant?.ToString(CultureInfo.InvariantCulture) ?? "-"} handler={s.Handler?.ToString(CultureInfo.InvariantCulture) ?? "-"} lane={(s.Lane is null ? "-" : BoundaryValues.Show(s.Lane))} status={s.Status?.ToString() ?? "-"} unknownSchedule={s.UnknownSchedule} skip={s.Skip} take={s.Take}",
        ListItemsStep s => $"ListItems slot={s.Slot}",
        ReportItemStep s => $"Report slot={s.Slot} seq={s.Seq} status={s.Status} run={s.Run?.ToString(CultureInfo.InvariantCulture) ?? "-"} error={s.Error?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
        QueueDepthStep => "QueueDepth",
        AdvanceClockStep s => $"Advance {s.Seconds}s",
        _ => step.ToString() ?? string.Empty,
    };

    /// <inheritdoc />
    public override string Print(JobPlan plan)
    {
        var builder = new StringBuilder("\n");
        SessionStateMachine.Pool(builder, "tenants", plan.Universe.Tenants);
        SessionStateMachine.Pool(builder, "owners", plan.Universe.Owners);
        SessionStateMachine.Pool(builder, "targets", plan.Universe.Targets);
        SessionStateMachine.Pool(builder, "inputs", plan.Universe.Inputs);
        SessionStateMachine.Pool(builder, "payloads", plan.Universe.Payloads);
        SessionStateMachine.Pool(builder, "errors", plan.Universe.Errors);

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            builder.Append("  ").Append(index + 1).Append(". ").AppendLine(Describe(plan.Steps[index]));
        }

        return builder.ToString();
    }

    private static Gen<JobStep> Step(JobUniverse u)
    {
        var tenant = Gen.Int[0, u.Tenants.Length - 1];
        var slot = Gen.Int[0, SlotCount - 1];
        var owner = Gen.Int[0, u.Owners.Length - 1];
        var handler = Gen.Int[0, Handlers.Length - 1];
        var lane = Gen.Frequency((10, Gen.OneOf(ValidLanes)), (1, Gen.OneOf(InvalidLanes)));
        var hint = Gen.Frequency(
            (9, Gen.Const(LeaseHint.Current)),
            (2, Gen.Const(LeaseHint.OtherAttempt)),
            (2, Gen.Const(LeaseHint.OtherOwner)),
            (1, Gen.Const(LeaseHint.UnknownJob)));
        var laneFilter = Gen.Frequency(
            (4, Gen.Const((string[]?)null)),
            (1, Gen.Const((string[]?)[])),
            (4, Gen.OneOf(ValidLanes).Array[1, 2].Select(static lanes => (string[]?)lanes)),
            (1, Gen.Const((string[]?)["unused"])));
        var cap = Gen.Frequency(
            (6, Gen.Const((CapSpec?)null)),
            (3, Gen.Int[0, Handlers.Length - 1].Array[1, 3].SelectMany(handlers => Gen.Int[1, 3].Select(max => (CapSpec?)new CapSpec(handlers, max)))),
            (1, Gen.Int[0, 0].Select(static _ => (CapSpec?)new CapSpec([], 1))),
            (1, Gen.Int[0, 2].Select(static handler => (CapSpec?)new CapSpec([handler], 0))));
        var duration = Gen.Frequency(
            (1, Gen.Const(0L)),
            (8, Gen.Int[1, 600].Select(static seconds => seconds * 1000L)),
            (1, Gen.Const(86_400_000L * 40)));
        var lease = owner.SelectMany(o => duration.SelectMany(d => laneFilter.SelectMany(l => cap.Select(c => (JobStep)new LeaseJobStep(o, d, l, c)))));
        var inputs = Gen.Int[0, u.Inputs.Length - 1].Array[0, 3];
        var maxDuration = Gen.Frequency(
            (6, Gen.Const((long?)null)),
            (1, Gen.Const((long?)5_400_000)),
            (1, Gen.Const((long?)2_592_000_000)),
            (2, Gen.Int[1, 3_600_000].Select(static ms => (long?)ms)));
        var maxAttempts = Gen.Frequency((6, Gen.Const((int?)null)), (3, Gen.Int[1, 10].Select(static n => (int?)n)), (1, Gen.Const((int?)1000)));
        var enqueue =
            from s in slot
            from t in tenant
            from h in handler
            from l in lane
            from scheduled in Gen.Int[-30, 120]
            from scheduledFlavor in Flavor
            from created in Gen.Int[0, 3]
            from createdFlavor in Flavor
            from md in maxDuration
            from ma in maxAttempts
            from payload in Gen.Int[0, u.Payloads.Length - 1]
            from items in inputs
            select (JobStep)new EnqueueJobStep(s, t, h, l, scheduled, scheduledFlavor, created, createdFlavor, md, ma, payload, items);

        // A listing is always bounded to the sequence's own jobs: by its tenant or by its (prefixed) handler key.
        var query =
            from t in Gen.Frequency((6, tenant.Select(static x => (int?)x)), (1, Gen.Const((int?)null)))
            from h in Gen.Frequency((2, Gen.Const((int?)null)), (1, handler.Select(static x => (int?)x)))
            from l in Gen.Frequency((3, Gen.Const((string?)null)), (1, Gen.OneOf(ValidLanes).Select(static x => (string?)x)))
            from st in Gen.Frequency((3, Gen.Const((JobStatus?)null)), (1, Gen.Enum<JobStatus>().Select(static x => (JobStatus?)x)))
            from skip in Gen.Frequency((6, Gen.Const(0)), (2, Gen.Int[1, 4]), (1, Gen.Int[-3, -1]))
            from take in Gen.Frequency((6, Gen.Const(50)), (2, Gen.Int[0, 4]), (1, Gen.Int[-3, -1]), (1, Gen.Const(int.MaxValue)))
            from unknownSchedule in Gen.Frequency((9, Gen.Const(false)), (1, Gen.Const(true)))
            select (JobStep)new QueryJobsStep(t ?? (h is null ? 0 : null), h, l, st, unknownSchedule, skip, take);
        var itemStatus = Gen.Frequency(
            (1, Gen.Const(JobItemStatus.Pending)),
            (3, Gen.Const(JobItemStatus.Completed)),
            (2, Gen.Const(JobItemStatus.Failed)));
        var report =
            from s in slot
            from seq in Gen.Frequency((8, Gen.Int[0, 3]), (1, Gen.Int[-2, -1]), (1, Gen.Int[4, 9]))
            from status in itemStatus
            from run in Gen.Frequency((1, Gen.Const((int?)null)), (3, Gen.Int[0, 2].Select(static x => (int?)x)))
            from error in Gen.Int[0, u.Errors.Length - 1]
            select (JobStep)new ReportItemStep(s, seq, status, run, status == JobItemStatus.Failed ? error : null);

        return Gen.Frequency(
            (18, enqueue),
            (18, lease),
            (6, slot.SelectMany(s => hint.SelectMany(h => owner.SelectMany(o => duration.Select(d => (JobStep)new RenewJobStep(s, h, o, 1, d)))))),
            (8, slot.SelectMany(s => hint.SelectMany(h => owner.Select(o => (JobStep)new MarkRunningJobStep(s, h, o, 1))))),
            (9, slot.SelectMany(s => hint.SelectMany(h => owner.SelectMany(o => Gen.Frequency((3, Gen.Const(JobStatus.Completed)), (2, Gen.Const(JobStatus.Failed))).SelectMany(st => Gen.Int[0, 30].SelectMany(at => Gen.Frequency((1, Gen.Const((int?)null)), (2, Gen.Int[0, u.Errors.Length - 1].Select(static x => (int?)x))).Select(e => (JobStep)new CompleteJobStep(s, h, o, 1, st, at, e)))))))),
            (6, slot.SelectMany(s => hint.SelectMany(h => owner.SelectMany(o => Gen.Int[0, u.Errors.Length - 1].SelectMany(e => Gen.Frequency((2, Gen.Const((int?)null)), (1, Gen.Const((int?)0)), (4, Gen.Int[1, 120].Select(static x => (int?)x))).Select(d => (JobStep)new ReleaseJobStep(s, h, o, 1, e, d))))))),
            (5, tenant.SelectMany(t => slot.Select(s => (JobStep)new CancelJobStep(t, s)))),
            (9, Gen.Frequency((6, Gen.Const(-1)), (2, tenant)).SelectMany(t => slot.Select(s => (JobStep)new GetJobStep(t, s)))),
            (8, query),
            (4, slot.Select(static s => (JobStep)new ListItemsStep(s))),
            (10, report),
            (4, Gen.Const((JobStep)new QueueDepthStep())),
            (10, Gen.Frequency((6, Gen.Int[1, 60]), (2, Gen.Const(0)), (1, Gen.Const(3600))).Select(static seconds => (JobStep)new AdvanceClockStep(seconds))));
    }
}

/// <summary>The job specification, written from the interface documentation and the behaviour its contract tests pin.</summary>
internal sealed class JobModel(JobUniverse universe, ProviderTraits traits) : IStepModel<JobStep>
{
    // The model's own zero: an instant that is a whole number of microseconds from the start of every provider's
    // timestamp grid, so a stored instant is the model's offset cut to the provider's resolution.
    private static readonly DateTimeOffset Base = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan[] Offsets =
    [
        TimeSpan.FromHours(14),
        TimeSpan.FromHours(-12),
        TimeSpan.FromMinutes(330),
    ];

    private readonly Job?[] _jobs = new Job?[JobStateMachine.SlotCount];
    private long _now;

    /// <summary>Gets the ticks a due time is written with for a slot: whole seconds plus a fraction unique to the slot.</summary>
    /// <param name="seconds">Whole seconds.</param>
    /// <param name="slot">The slot.</param>
    /// <param name="flavor">The flavour of the written instant.</param>
    /// <returns>The ticks from the start of the sequence.</returns>
    internal static long DueTicks(int seconds, int slot, TimeFlavor flavor)
        => (seconds * TimeSpan.TicksPerSecond) + 100_000 + (slot * 13_131L * 10) + (flavor == TimeFlavor.SubMicrosecond ? 7 : 0);

    /// <summary>Gets the ticks a creation time is written with: whole seconds, and no fraction a tie could be broken on.</summary>
    /// <param name="seconds">Whole seconds.</param>
    /// <param name="flavor">The flavour of the written instant.</param>
    /// <returns>The ticks from the start of the sequence.</returns>
    internal static long CreatedTicks(int seconds, TimeFlavor flavor)
        => (seconds * TimeSpan.TicksPerSecond) + (flavor == TimeFlavor.SubMicrosecond ? 7 : 0);

    /// <summary>Gets the other offset a flavoured instant is written in.</summary>
    /// <param name="slotOrSeconds">A number that picks one of the offsets.</param>
    /// <returns>The offset.</returns>
    internal static TimeSpan OffsetFor(int slotOrSeconds) => Offsets[Math.Abs(slotOrSeconds) % Offsets.Length];

    /// <summary>Gets the delay of a retry, in ticks: whole seconds plus a whole number of milliseconds unique to the slot.</summary>
    /// <param name="seconds">Whole seconds.</param>
    /// <param name="slot">The slot.</param>
    /// <returns>The ticks.</returns>
    internal static long RetryDelayTicks(int seconds, int slot) => (seconds * TimeSpan.TicksPerSecond) + (slot * TimeSpan.TicksPerMillisecond);

    /// <summary>Renders a job record in the one form both sides use.</summary>
    /// <returns>The text.</returns>
    internal static string Render(int slot, string tenant, string handler, string lane, string target, JobStatus status, string payload, int total, int done, int failed, int attempt, int? maxAttempts, long? maxDurationMs, string? owner, long? leaseUntil, long scheduledFor, long? startedAt, string? completedAt, string? error, long createdAt)
        => $"job#{slot} tenant={BoundaryValues.Show(tenant)} handler={BoundaryValues.Show(handler)} lane={BoundaryValues.Show(lane)} target={BoundaryValues.Show(target)} status={status} payload={payload} items={total}/{done}/{failed} attempt={attempt} maxAttempts={maxAttempts?.ToString(CultureInfo.InvariantCulture) ?? "-"} maxDuration={maxDurationMs?.ToString(CultureInfo.InvariantCulture) ?? "-"} owner={(owner is null ? "-" : BoundaryValues.Show(owner))} leaseUntil={Ticks(leaseUntil)} scheduledFor={scheduledFor.ToString(CultureInfo.InvariantCulture)} started={Ticks(startedAt)} completed={completedAt ?? "-"} error={(error is null ? "-" : BoundaryValues.Show(error))} created={createdAt.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Renders an item in the one form both sides use.</summary>
    /// <returns>The text.</returns>
    internal static string RenderItem(int seq, string input, JobItemStatus status, int? run, string? error)
        => $"item#{seq} input={BoundaryValues.Show(input)} status={status} run={run?.ToString(CultureInfo.InvariantCulture) ?? "-"} error={(error is null ? "-" : BoundaryValues.Show(error))}";

    /// <inheritdoc />
    public JobStep Concretize(JobStep step)
    {
        switch (step)
        {
            case LeaseHolderStep holder:
                var job = _jobs[holder.Slot];

                if (job is null || holder.Hint == LeaseHint.UnknownJob)
                {
                    return With(holder, holder.Owner, 1);
                }

                var owner = job.OwnerIndex ?? holder.Owner;
                var attempt = Math.Max(1, job.Attempt);

                return holder.Hint switch
                {
                    LeaseHint.OtherAttempt => With(holder, owner, attempt > 1 ? attempt - 1 : attempt + 1),
                    LeaseHint.OtherOwner => With(holder, (owner + 1) % universe.Owners.Length, attempt),
                    _ => With(holder, owner, attempt),
                };
            case ReportItemStep report when report.Run is null && _jobs[report.Slot] is { } reported && report.Seq >= 0 && report.Seq < reported.Items.Count && reported.Items[report.Seq].Run is not null:
                // A report that names no run, on an item a marker gave one, is left to the store: the interface does not
                // say whether the run is kept or cleared, so the sequence never asks.
                return report with { Run = 0 };
            default:
                return step;
        }
    }

    /// <inheritdoc />
    public Observation Apply(JobStep step)
    {
        try
        {
            return step switch
            {
                EnqueueJobStep s => Enqueue(s),
                LeaseJobStep s => Lease(s),
                RenewJobStep s => Holder(s, job => { job.LeaseUntil = Stamp(_now + (s.DurationMs * TimeSpan.TicksPerMillisecond)); }, requireLeased: false),
                MarkRunningJobStep s => Holder(s, job => job.Status = JobStatus.Running, requireLeased: true),
                CompleteJobStep s => Holder(s, job => Finish(job, s), requireLeased: false),
                ReleaseJobStep s => Holder(s, job => Release(job, s), requireLeased: false),
                CancelJobStep s => Cancel(s),
                GetJobStep s => Get(s),
                QueryJobsStep s => Query(s),
                ListItemsStep s => ListItems(s),
                ReportItemStep s => Report(s),
                QueueDepthStep => Depth(),
                AdvanceClockStep s => Advance(s),
                _ => throw new InvalidOperationException(step.GetType().Name),
            };
        }
        catch (ArgumentException)
        {
            return Observation.Rejected();
        }
    }

    private static string Ticks(long? ticks) => ticks?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Fold(string tenant)
    {
        if (string.IsNullOrWhiteSpace(tenant))
        {
            throw new ArgumentException("blank tenant", nameof(tenant));
        }

        return tenant.ToLowerInvariant();
    }

    private static LeaseHolderStep With(LeaseHolderStep step, int owner, int attempt) => step switch
    {
        RenewJobStep s => s with { Owner = owner, Attempt = attempt },
        MarkRunningJobStep s => s with { Owner = owner, Attempt = attempt },
        CompleteJobStep s => s with { Owner = owner, Attempt = attempt },
        ReleaseJobStep s => s with { Owner = owner, Attempt = attempt },
        _ => throw new InvalidOperationException(step.GetType().Name),
    };

    // A lane name is 1-64 characters: lowercase ASCII letters, digits, '.', '_' or '-', starting with a letter or a digit.
    private static bool ValidLane(string lane)
    {
        if (lane.Length is 0 or > 64)
        {
            return false;
        }

        for (var index = 0; index < lane.Length; index++)
        {
            var c = lane[index];
            var alphanumeric = c is (>= 'a' and <= 'z') or (>= '0' and <= '9');

            if (!(alphanumeric || (index > 0 && c is '.' or '_' or '-')))
            {
                return false;
            }
        }

        return true;
    }

    // The instant the model writes at an offset, as the store holds it: cut to the provider's resolution.
    private long Stamp(long ticksFromBase)
        => traits.Ticks(Base.AddTicks(ticksFromBase)) - Base.UtcTicks;

    private Observation Enqueue(EnqueueJobStep s)
    {
        if (!ValidLane(s.Lane))
        {
            throw new ArgumentException("invalid lane", nameof(s));
        }

        var tenant = traits.Text(Fold(universe.Tenants[s.Tenant]));

        if (_jobs[s.Slot] is not null)
        {
            return Observation.Of("throws refused");
        }

        var job = new Job
        {
            Slot = s.Slot,
            Tenant = tenant,
            Handler = Handler(s.Handler),
            Lane = s.Lane,
            Target = traits.Text(universe.Targets[s.Slot % universe.Targets.Length]),
            Payload = CanonicalJson.Of(universe.Payloads[s.Payload], traits.Text),
            Status = JobStatus.Pending,
            MaxAttempts = s.MaxAttempts,
            MaxDurationMs = s.MaxDurationMs,
            ScheduledFor = Stamp(JobModel.DueTicks(s.ScheduledSeconds, s.Slot, s.ScheduledFlavor)),
            CreatedAt = Stamp(JobModel.CreatedTicks(s.CreatedSeconds, s.CreatedFlavor)),
        };

        job.Items.AddRange(s.Inputs.Select(index => new Item(traits.Text(universe.Inputs[index]))));
        _jobs[s.Slot] = job;

        // A SQL store builds the record it returns from the call and writes the row separately, so the text and the times
        // of the returned record are the caller's own (the tenant is not: it is returned in the form the row holds).
        return Observation.Of("enqueued " + (traits.CreateEchoesInputText
            ? JobModel.Render(
                s.Slot,
                tenant,
                Handler(s.Handler),
                s.Lane,
                universe.Targets[s.Slot % universe.Targets.Length],
                JobStatus.Pending,
                CanonicalJson.Of(universe.Payloads[s.Payload]),
                s.Inputs.Length,
                0,
                0,
                0,
                s.MaxAttempts,
                s.MaxDurationMs,
                null,
                null,
                JobModel.DueTicks(s.ScheduledSeconds, s.Slot, s.ScheduledFlavor),
                null,
                null,
                null,
                JobModel.CreatedTicks(s.CreatedSeconds, s.CreatedFlavor))
            : Render(job)));
    }

    private string Handler(int handler) => JobStateMachine.Handlers[handler];

    private Observation Lease(LeaseJobStep s)
    {
        if (s.Cap is { } capCheck && (capCheck.Handlers.Length == 0 || capCheck.Max < 1))
        {
            throw new ArgumentException("a tenant cap names a handler key and a room", nameof(s));
        }

        var now = Stamp(_now);
        HashSet<string>? tenantsAtCap = null;

        if (s.Cap is { } cap)
        {
            var keys = cap.Handlers.Select(Handler).ToHashSet(StringComparer.Ordinal);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var held in _jobs.Where(static job => job is not null).Select(static job => job!))
            {
                // A live lease counts: one that has not run out (a lease that ends at this very instant is still live).
                if (held.Status is JobStatus.Leased or JobStatus.Running && held.LeaseUntil is { } until && until >= now && keys.Contains(held.Handler))
                {
                    counts[held.Tenant] = counts.GetValueOrDefault(held.Tenant) + 1;
                }
            }

            tenantsAtCap = counts.Where(pair => pair.Value >= cap.Max).Select(static pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        }

        var capKeys = s.Cap?.Handlers.Select(Handler).ToHashSet(StringComparer.Ordinal);
        Job? best = null;

        foreach (var job in _jobs)
        {
            if (job is null || job.ScheduledFor > now)
            {
                continue;
            }

            if (s.Lanes is { Length: > 0 } lanes && !lanes.Contains(job.Lane, StringComparer.Ordinal))
            {
                continue;
            }

            // Ready: waiting, or leased with a lease that has run out (a lease that ends at this very instant has not).
            var ready = job.Status == JobStatus.Pending
                || (job.Status is JobStatus.Leased or JobStatus.Running && job.LeaseUntil is { } until && until < now);

            if (!ready)
            {
                continue;
            }

            if (capKeys is not null && capKeys.Contains(job.Handler) && tenantsAtCap!.Contains(job.Tenant))
            {
                continue;
            }

            if (best is null || job.ScheduledFor < best.ScheduledFor)
            {
                best = job;
            }
        }

        if (best is null)
        {
            return Observation.Of("null");
        }

        best.Status = JobStatus.Leased;
        best.Owner = traits.Text(universe.Owners[s.Owner]);
        best.OwnerIndex = s.Owner;
        best.LeaseUntil = Stamp(_now + (s.DurationMs * TimeSpan.TicksPerMillisecond));
        best.Attempt++;
        best.StartedAt ??= now;

        return Observation.Of("leased " + Render(best));
    }

    private Observation Holder(LeaseHolderStep s, Action<Job> change, bool requireLeased)
    {
        var job = _jobs[s.Slot];

        // The lease a call names is the job, the worker and the attempt, on a job that is still open; nothing else is
        // compared (in particular not the time: a lease that ran out and was not taken over is still the holder's).
        if (job is not null
            && s.Hint != LeaseHint.UnknownJob
            && job.Status is JobStatus.Leased or JobStatus.Running
            && job.Attempt == s.Attempt
            && string.Equals(job.Owner, traits.Text(universe.Owners[s.Owner]), StringComparison.Ordinal)
            && (!requireLeased || job.Status == JobStatus.Leased))
        {
            change(job);

            return Observation.Of("true");
        }

        return Observation.Of("false");
    }

    private void Finish(Job job, CompleteJobStep s)
    {
        job.Status = s.Status;
        job.CompletedAt = Stamp(JobModel.CreatedTicks(s.CompletedSeconds, TimeFlavor.Plain)).ToString(CultureInfo.InvariantCulture);
        job.Error = s.Error is { } error ? traits.Text(universe.Errors[error]) : null;
        job.Owner = null;
        job.OwnerIndex = null;
        job.LeaseUntil = null;
    }

    private void Release(Job job, ReleaseJobStep s)
    {
        job.Status = JobStatus.Pending;
        job.Owner = null;
        job.OwnerIndex = null;
        job.LeaseUntil = null;
        job.Error = traits.Text(universe.Errors[s.Error]);

        // A delay pushes the due time forward; none (or zero) leaves it, and the job is ready at once.
        if (s.DelaySeconds is > 0 and var seconds)
        {
            job.ScheduledFor = Stamp(_now + JobModel.RetryDelayTicks(seconds, job.Slot));
        }
    }

    private Observation Cancel(CancelJobStep s)
    {
        var tenant = traits.Text(Fold(universe.Tenants[s.Tenant]));

        if (_jobs[s.Slot] is { } job && string.Equals(job.Tenant, tenant, StringComparison.Ordinal) && job.Status is JobStatus.Pending or JobStatus.Leased or JobStatus.Running)
        {
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = "set";
            job.Owner = null;
            job.OwnerIndex = null;
            job.LeaseUntil = null;

            return Observation.Of("true");
        }

        return Observation.Of("false");
    }

    private Observation Get(GetJobStep s)
    {
        if (_jobs[s.Slot] is not { } job)
        {
            return Observation.Of("null");
        }

        var tenant = s.Tenant < 0 ? job.Tenant : traits.Text(Fold(universe.Tenants[s.Tenant]));

        return Observation.Of(string.Equals(job.Tenant, tenant, StringComparison.Ordinal) ? Render(job) : "null");
    }

    private Observation Query(QueryJobsStep s)
    {
        var tenant = s.Tenant is { } t ? traits.Text(Fold(universe.Tenants[t])) : null;
        var matches = _jobs
            .Where(static job => job is not null)
            .Select(static job => job!)
            .Where(job => tenant is null || string.Equals(job.Tenant, tenant, StringComparison.Ordinal))
            .Where(job => s.Handler is not { } h || string.Equals(job.Handler, Handler(h), StringComparison.Ordinal))
            .Where(job => s.Lane is null || string.Equals(job.Lane, s.Lane, StringComparison.Ordinal))
            .Where(job => s.Status is not { } status || job.Status == status)
            .Where(_ => !s.UnknownSchedule)
            .OrderByDescending(static job => job.CreatedAt)
            .Select(job => (Key: job.CreatedAt, Rendered: Render(job)))
            .ToList();

        return TiedPage.Cut("jobs", matches, s.Skip, s.Take);
    }

    private Observation ListItems(ListItemsStep s)
        => Observation.List("items", _jobs[s.Slot] is { } job ? [.. job.Items.Select(static (item, seq) => RenderItem(seq, item.Input, item.Status, item.Run, item.Error))] : []);

    private Observation Report(ReportItemStep s)
    {
        if (_jobs[s.Slot] is not { } job || s.Seq < 0 || s.Seq >= job.Items.Count)
        {
            return Observation.Of("ok");
        }

        var item = job.Items[s.Seq];

        // Only an item that is still pending changes. A pending report is a marker: it records the run and counts nothing.
        if (item.Status != JobItemStatus.Pending)
        {
            return Observation.Of("ok");
        }

        item.Status = s.Status;
        item.Run = s.Run;
        item.Error = s.Error is { } error ? traits.Text(universe.Errors[error]) : null;

        if (s.Status == JobItemStatus.Completed)
        {
            job.Done++;
        }
        else if (s.Status == JobItemStatus.Failed)
        {
            job.Failed++;
        }

        return Observation.Of("ok");
    }

    private Observation Depth()
    {
        var rows = _jobs
            .Where(static job => job is { Status: JobStatus.Pending or JobStatus.Leased or JobStatus.Running })
            .GroupBy(static job => (job!.Lane, job.Status))
            .Select(static group => $"lane={BoundaryValues.Show(group.Key.Lane)} status={group.Key.Status} count={group.Count()}")
            .Order(StringComparer.Ordinal)
            .ToList();

        return Observation.List("depth", rows);
    }

    private Observation Advance(AdvanceClockStep s)
    {
        _now += s.Seconds * TimeSpan.TicksPerSecond;

        return Observation.Of("ok");
    }

    private static string Render(Job job)
        => JobModel.Render(job.Slot, job.Tenant, job.Handler, job.Lane, job.Target, job.Status, job.Payload, job.Items.Count, job.Done, job.Failed, job.Attempt, job.MaxAttempts, job.MaxDurationMs, job.Owner, job.LeaseUntil, job.ScheduledFor, job.StartedAt, job.CompletedAt, job.Error, job.CreatedAt);

    private sealed class Item(string input)
    {
        public string Input { get; } = input;

        public JobItemStatus Status { get; set; } = JobItemStatus.Pending;

        public int? Run { get; set; }

        public string? Error { get; set; }
    }

    private sealed class Job
    {
        public required int Slot { get; init; }

        public required string Tenant { get; init; }

        public required string Handler { get; init; }

        public required string Lane { get; init; }

        public required string Target { get; init; }

        public required string Payload { get; init; }

        public required long ScheduledFor { get; set; }

        public required long CreatedAt { get; init; }

        public JobStatus Status { get; set; }

        public int? MaxAttempts { get; init; }

        public long? MaxDurationMs { get; init; }

        public List<Item> Items { get; } = [];

        public int Done { get; set; }

        public int Failed { get; set; }

        public int Attempt { get; set; }

        public string? Owner { get; set; }

        public int? OwnerIndex { get; set; }

        public long? LeaseUntil { get; set; }

        public long? StartedAt { get; set; }

        public string? CompletedAt { get; set; }

        public string? Error { get; set; }
    }
}

/// <summary>Applies job steps to a store.</summary>
internal sealed class JobDriver : IStepDriver<JobStep>, IAsyncDisposable
{
    private readonly IJobStore _store;
    private readonly DifferentialClock _clock;
    private readonly DifferentialScope _scope;
    private readonly JobUniverse _universe;
    private readonly Guid[] _ids = [.. Enumerable.Range(0, JobStateMachine.SlotCount).Select(static _ => Guid.NewGuid())];
    private readonly Guid[] _runs = [.. Enumerable.Range(0, 3).Select(static _ => Guid.NewGuid())];
    private readonly Dictionary<int, string> _queued = [];
    private readonly DateTimeOffset _start;

    /// <summary>Creates the driver of one sequence.</summary>
    /// <param name="store">The store under test.</param>
    /// <param name="clock">The clock that decides the store's leases.</param>
    /// <param name="scope">The tenant namespace of the sequence.</param>
    /// <param name="universe">The values the steps draw from.</param>
    public JobDriver(IJobStore store, DifferentialClock clock, DifferentialScope scope, JobUniverse universe)
    {
        _store = store;
        _clock = clock;
        _scope = scope;
        _universe = universe;
        _start = clock.Now;
    }

    /// <inheritdoc />
    public async ValueTask<Observation> ApplyAsync(JobStep step)
    {
        switch (step)
        {
            case EnqueueJobStep s:
                return await EnqueueAsync(s);
            case LeaseJobStep s:
                var leased = await _store.LeaseAsync(new JobLeaseRequest
                {
                    Owner = _universe.Owners[s.Owner],
                    LeaseDuration = TimeSpan.FromMilliseconds(s.DurationMs),
                    Lanes = s.Lanes,
                    TenantCap = s.Cap is { } cap
                        ? new JobTenantCap { HandlerKeys = [.. cap.Handlers.Select(index => _scope.Wrap(JobStateMachine.Handlers[index]))], MaxLeasedPerTenant = cap.Max }
                        : null,
                });

                return Observation.Of(leased is null ? "null" : "leased " + Render(leased));
            case RenewJobStep s:
                return Bool(await _store.RenewLeaseAsync(Lease(s), TimeSpan.FromMilliseconds(s.DurationMs)));
            case MarkRunningJobStep s:
                return Bool(await _store.MarkRunningAsync(Lease(s)));
            case CompleteJobStep s:
                return Bool(await _store.CompleteAsync(
                    Lease(s),
                    new JobCompletion
                    {
                        Status = s.Status,
                        CompletedAt = _start.AddTicks(JobModel.CreatedTicks(s.CompletedSeconds, TimeFlavor.Plain)),
                        ErrorMessage = s.Error is { } error ? _universe.Errors[error] : null,
                    }));
            case ReleaseJobStep s:
                return Bool(await _store.ReleaseForRetryAsync(
                    Lease(s),
                    _universe.Errors[s.Error],
                    s.DelaySeconds is { } seconds ? (seconds == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(JobModel.RetryDelayTicks(seconds, s.Slot))) : null));
            case CancelJobStep s:
                return Bool(await _store.CancelAsync(_scope.Wrap(_universe.Tenants[s.Tenant]), _ids[s.Slot]));
            case GetJobStep s:
                var tenant = s.Tenant < 0 ? _queued.GetValueOrDefault(s.Slot) ?? _scope.Wrap(_universe.Tenants[0]) : _scope.Wrap(_universe.Tenants[s.Tenant]);
                var found = await _store.GetAsync(tenant, _ids[s.Slot]);

                return Observation.Of(found is null ? "null" : Render(found));
            case QueryJobsStep s:
                var page = await _store.QueryAsync(new JobQuery
                {
                    TenantId = s.Tenant is { } t ? _scope.Wrap(_universe.Tenants[t]) : null,
                    HandlerKey = s.Handler is { } h ? _scope.Wrap(JobStateMachine.Handlers[h]) : null,
                    Lane = s.Lane,
                    Status = s.Status,
                    ScheduleId = s.UnknownSchedule ? Guid.NewGuid() : null,
                    Skip = s.Skip,
                    Take = s.Take,
                });

                return Observation.List("jobs", [.. page.Select(Render)]);
            case ListItemsStep s:
                var items = await _store.ListItemsAsync(_ids[s.Slot]);

                return Observation.List("items", [.. items.Select(item => JobModel.RenderItem(item.Seq, item.Input, item.Status, item.RunId is { } run ? Array.IndexOf(_runs, run) : null, item.Error))]);
            case ReportItemStep s:
                await _store.ReportItemAsync(new JobItemResult
                {
                    JobId = _ids[s.Slot],
                    Seq = s.Seq,
                    Status = s.Status,
                    RunId = s.Run is { } r ? _runs[r] : null,
                    Error = s.Error is { } e ? _universe.Errors[e] : null,
                });

                return Observation.Of("ok");
            case QueueDepthStep:
                var depth = await _store.GetQueueDepthAsync();

                return Observation.List(
                    "depth",
                    [.. depth.Select(row => $"lane={BoundaryValues.Show(row.Lane)} status={row.Status} count={row.Count}").Order(StringComparer.Ordinal)]);
            case AdvanceClockStep s:
                await _clock.AdvanceAsync(TimeSpan.FromSeconds(s.Seconds));

                return Observation.Of("ok");
            default:
                throw new InvalidOperationException(step.GetType().Name);
        }
    }

    /// <summary>Cancels every job of the sequence, so that the next sequence's lease cannot take one.</summary>
    /// <returns>The completion task.</returns>
    public async ValueTask DisposeAsync()
    {
        foreach (var (slot, tenant) in _queued)
        {
            try
            {
                await _store.CancelAsync(tenant, _ids[slot]);
            }
            catch (Exception)
            {
                // Cleanup: a store that cannot answer is reported by the sequence that used it, not by this.
            }
        }
    }

    private static Observation Bool(bool value) => Observation.Of(value ? "true" : "false");

    private JobLease Lease(LeaseHolderStep step)
        => new()
        {
            JobId = step.Hint == LeaseHint.UnknownJob ? Guid.NewGuid() : _ids[step.Slot],
            Owner = _universe.Owners[step.Owner],
            Attempt = step.Attempt,
        };

    private async ValueTask<Observation> EnqueueAsync(EnqueueJobStep s)
    {
        var due = Instant(JobModel.DueTicks(s.ScheduledSeconds, s.Slot, s.ScheduledFlavor), s.ScheduledFlavor, s.Slot);
        var created = Instant(JobModel.CreatedTicks(s.CreatedSeconds, s.CreatedFlavor), s.CreatedFlavor, s.CreatedSeconds);
        var tenant = _scope.Wrap(_universe.Tenants[s.Tenant]);

        try
        {
            var record = await _store.EnqueueAsync(
                new JobRecord
                {
                    Id = _ids[s.Slot],
                    TenantId = tenant,
                    HandlerKey = _scope.Wrap(JobStateMachine.Handlers[s.Handler]),
                    Lane = s.Lane,
                    TargetName = _universe.Targets[s.Slot % _universe.Targets.Length],
                    Status = JobStatus.Pending,
                    Payload = JsonDocument.Parse(_universe.Payloads[s.Payload]).RootElement.Clone(),
                    ScheduledFor = due,
                    CreatedAt = created,
                    MaxAttempts = s.MaxAttempts,
                    MaxDuration = s.MaxDurationMs is { } ms ? TimeSpan.FromMilliseconds(ms) : null,
                },
                [.. s.Inputs.Select(index => _universe.Inputs[index])]);

            _queued[s.Slot] = tenant;

            return Observation.Of("enqueued " + Render(record));
        }
        catch (Exception exception) when (exception is not (ArgumentException or OperationCanceledException or DifferentialMismatchException))
        {
            // The interface says the call "throws" for a job id that is already stored, and no more than that: any
            // refusal of the store is the same answer.
            return Observation.Of("throws refused");
        }
    }

    private DateTimeOffset Instant(long ticksFromStart, TimeFlavor flavor, int offsetPick)
    {
        var instant = _start.AddTicks(ticksFromStart);

        return flavor == TimeFlavor.OtherOffset ? instant.ToOffset(JobModel.OffsetFor(offsetPick)) : instant;
    }

    private string Render(JobRecord job)
    {
        var slot = Array.IndexOf(_ids, job.Id);
        var completed = job.CompletedAt is null
            ? null
            : job.Status == JobStatus.Cancelled ? "set" : Offset(job.CompletedAt.Value).ToString(CultureInfo.InvariantCulture);

        return JobModel.Render(
            slot,
            _scope.Unwrap(job.TenantId) ?? "<null>",
            _scope.Unwrap(job.HandlerKey) ?? "<null>",
            job.Lane,
            job.TargetName,
            job.Status,
            CanonicalJson.Of(job.Payload.GetRawText()),
            job.TotalItems,
            job.DoneItems,
            job.FailedItems,
            job.Attempt,
            job.MaxAttempts,
            job.MaxDuration is { } limit ? (long)limit.TotalMilliseconds : null,
            job.LeaseOwner,
            job.LeaseUntil is { } until ? Offset(until) : null,
            Offset(job.ScheduledFor),
            job.StartedAt is { } started ? Offset(started) : null,
            completed,
            job.ErrorMessage,
            Offset(job.CreatedAt));
    }

    private long Offset(DateTimeOffset instant) => instant.UtcTicks - _start.UtcTicks;
}
