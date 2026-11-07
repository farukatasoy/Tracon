using Tracon.Tests.Differential;

namespace Tracon.Core.UnitTests.Contracts;

/// <summary>
/// What the job model lets a lease do when other steps run at the same moment: pass over a job a racing step writes, and
/// nothing else.
/// </summary>
/// <remarks>
/// A lease finds work with <c>FOR UPDATE SKIP LOCKED</c> or <c>READPAST</c>, so a report that holds a job's row for the
/// length of its statement makes a lease at that moment take the next job. The race check tries both explanations; the
/// second one must not be wide enough to excuse a store that skips a job nothing else touches.
/// </remarks>
public sealed class JobModelPassingOverTests
{
    private static readonly JobUniverse Universe = new(["a"], ["worker-a", "worker-b"], ["target"], ["input"], ["{}"], ["failure"]);

    private static readonly LeaseJobStep Lease = new(Owner: 0, DurationMs: 60_000, Lanes: null, Cap: null);

    [Fact]
    public void A_lease_with_no_racing_step_on_a_job_takes_the_earliest_job_either_way()
    {
        var serial = Model(out var concurrent, new LeaseJobStep(0, 60_000, null, null));
        var passing = Model(out _, new LeaseJobStep(0, 60_000, null, null));

        var passed = passing.ApplyPassingOver(Lease, concurrent).ToString();

        passed.ShouldBe(serial.Apply(Lease).ToString());
        passed.ShouldContain("job#0");
    }

    [Fact]
    public void A_lease_may_pass_over_the_job_a_racing_report_writes()
    {
        var model = Model(out _, new ReportItemStep(Slot: 0, Seq: 0, JobItemStatus.Pending, Run: null, Error: null));

        var leased = model.ApplyPassingOver(Lease, [Lease, new ReportItemStep(Slot: 0, Seq: 0, JobItemStatus.Pending, Run: null, Error: null)]);

        leased.ToString().ShouldContain("job#1", customMessage: "the report holds job#0, so the lease takes the next one");
    }

    [Fact]
    public void A_lease_may_not_pass_over_a_job_that_no_racing_step_writes()
    {
        var model = Model(out _, new ReportItemStep(Slot: 1, Seq: 0, JobItemStatus.Pending, Run: null, Error: null));

        var leased = model.ApplyPassingOver(Lease, [Lease, new ReportItemStep(Slot: 1, Seq: 0, JobItemStatus.Pending, Run: null, Error: null)]);

        leased.ToString().ShouldContain("job#0", customMessage: "nothing holds job#0: a store that skipped it would be wrong");
    }

    [Fact]
    public void Only_a_lease_may_pass_over_a_row()
    {
        var model = new JobModel(Universe, new ProviderTraits { Name = "model" });

        model.MayPassOverHeldRow(Lease).ShouldBeTrue();
        model.MayPassOverHeldRow(new ReportItemStep(0, 0, JobItemStatus.Pending, null, null)).ShouldBeFalse();
        model.MayPassOverHeldRow(new GetJobStep(Tenant: -1, Slot: 0)).ShouldBeFalse();
    }

    /// <summary>Builds a model with two due jobs, slot 0 due before slot 1.</summary>
    private static JobModel Model(out IReadOnlyList<JobStep> concurrent, JobStep racing)
    {
        var model = new JobModel(Universe, new ProviderTraits { Name = "model" });

        model.Apply(Enqueue(slot: 0, dueSeconds: -8)).ToString().ShouldNotBeNull();
        model.Apply(Enqueue(slot: 1, dueSeconds: -2)).ToString().ShouldNotBeNull();

        concurrent = [Lease, racing];

        return model;
    }

    private static EnqueueJobStep Enqueue(int slot, int dueSeconds)
        => new(slot, Tenant: 0, Handler: 0, Lane: "default", dueSeconds, TimeFlavor.Plain, CreatedSeconds: 0, TimeFlavor.Plain, MaxDurationMs: null, MaxAttempts: null, Payload: 0, Inputs: [0]);
}
