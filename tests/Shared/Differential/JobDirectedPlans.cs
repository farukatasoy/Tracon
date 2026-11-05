namespace Tracon.Tests.Differential;

/// <summary>
/// Job sequences a generator reaches only by luck, written out. Each one has a single answer in the specification and
/// one comparison a store can get wrong, and each runs through the model and the driver a generated sequence does.
/// </summary>
/// <remarks>
/// A lease that is asked about at the very instant it ends needs a clock step that equals the lease and nothing in
/// between; a cancel that has to clear the lease needs a lease, a cancel in the right tenant and a read, in that order.
/// A sequence of at most twenty-four random steps draws either one rarely, and a store that gets the comparison wrong
/// stays green for hundreds of sequences. The plans below have no randomness: they either hold or they do not.
/// </remarks>
internal static class JobDirectedPlans
{
    private const int LeaseSeconds = 60;
    private const long LeaseMs = LeaseSeconds * 1000L;

    private static readonly JobUniverse Universe = new(
        Tenants: ["directed-a", "directed-b"],
        Owners: ["worker-a", "worker-b"],
        Targets: ["directed-target"],
        Inputs: ["input"],
        Payloads: ["{}"],
        Errors: ["failure"]);

    /// <summary>
    /// A job leased for a minute stays leased for the whole minute: asked for at the instant the lease ends, it is still
    /// the first worker's, and it is free one second after.
    /// </summary>
    public static JobPlan LeaseHeldUntilItEnds { get; } = new(
        Universe,
        [
            Enqueue(slot: 0, dueSeconds: -1),
            new LeaseJobStep(Owner: 0, LeaseMs, Lanes: null, Cap: null),
            new AdvanceClockStep(LeaseSeconds),
            new LeaseJobStep(Owner: 1, LeaseMs, Lanes: null, Cap: null),
            new GetJobStep(Tenant: -1, Slot: 0),
            new AdvanceClockStep(1),
            new LeaseJobStep(Owner: 1, LeaseMs, Lanes: null, Cap: null),
            new GetJobStep(Tenant: -1, Slot: 0),
        ]);

    /// <summary>
    /// A tenant counts a lease against its ceiling until the instant the lease ends: at that instant the tenant is still
    /// at its ceiling and its other job waits (another tenant's job does not), and one second later the lease no longer
    /// counts.
    /// </summary>
    public static JobPlan LiveLeaseCountsUntilItEnds { get; } = new(
        Universe,
        [
            Enqueue(slot: 0, dueSeconds: -2),
            Enqueue(slot: 1, dueSeconds: -1),
            Enqueue(slot: 2, dueSeconds: 0, tenant: 1),
            new LeaseJobStep(Owner: 0, LeaseMs, Lanes: null, Cap: null),
            new AdvanceClockStep(LeaseSeconds),
            new LeaseJobStep(Owner: 1, LeaseMs, Lanes: null, Cap: new CapSpec([0], Max: 1)),
            new AdvanceClockStep(1),
            new LeaseJobStep(Owner: 1, LeaseMs, Lanes: null, Cap: new CapSpec([0], Max: 1)),
        ]);

    /// <summary>
    /// A cancelled job shows no worker and no end of a lease, whether it was only leased or already running.
    /// </summary>
    public static JobPlan CancelledJobHoldsNoLease { get; } = new(
        Universe,
        [
            Enqueue(slot: 0, dueSeconds: -2),
            Enqueue(slot: 1, dueSeconds: -1),
            new LeaseJobStep(Owner: 0, LeaseMs, Lanes: null, Cap: null),
            new GetJobStep(Tenant: -1, Slot: 0),
            new CancelJobStep(Tenant: 0, Slot: 0),
            new GetJobStep(Tenant: -1, Slot: 0),
            new LeaseJobStep(Owner: 1, LeaseMs, Lanes: null, Cap: null),
            new MarkRunningJobStep(Slot: 1, LeaseHint.Current, Owner: 1, Attempt: 1),
            new GetJobStep(Tenant: -1, Slot: 1),
            new CancelJobStep(Tenant: 0, Slot: 1),
            new GetJobStep(Tenant: -1, Slot: 1),
        ]);

    private static EnqueueJobStep Enqueue(int slot, int dueSeconds, int tenant = 0)
        => new(
            slot,
            tenant,
            Handler: 0,
            Lane: "default",
            dueSeconds,
            TimeFlavor.Plain,
            CreatedSeconds: 0,
            TimeFlavor.Plain,
            MaxDurationMs: null,
            MaxAttempts: null,
            Payload: 0,
            Inputs: [0]);
}
