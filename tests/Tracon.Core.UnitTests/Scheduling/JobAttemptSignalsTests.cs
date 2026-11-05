namespace Tracon.Core.UnitTests.Scheduling;

/// <summary>
/// Which of the four sources behind a job handler's token the attempt reports when more than one has fired.
/// </summary>
/// <remarks>
/// <para>
/// The order is a rule of the product, not an accident of an expression: a worker that is stopping leaves the job to its
/// lease whatever else happened, a cancelled job stays cancelled whatever the clock says, and a lost lease ends the
/// attempt before its time limit can name it. A lost lease means the job is another worker's, so the attempt writes
/// nothing; a time limit means the attempt is this worker's to close. Taking the wrong one writes a result for a job the
/// worker no longer holds, or drops one it does hold.
/// </para>
/// <para>
/// The expected column is written out by hand for all sixteen combinations. It is not computed by the code under test.
/// </para>
/// </remarks>
public sealed class JobAttemptSignalsTests
{
    public static TheoryData<bool, bool, bool, bool, string> Combinations => new()
    {
        // stopping, cancelled, lease lost, time limit -> reason
        { false, false, false, false, nameof(JobStopReason.None) },
        { false, false, false, true, nameof(JobStopReason.MaxDurationExceeded) },
        { false, false, true, false, nameof(JobStopReason.LeaseLost) },
        { false, false, true, true, nameof(JobStopReason.LeaseLost) },
        { false, true, false, false, nameof(JobStopReason.Cancelled) },
        { false, true, false, true, nameof(JobStopReason.Cancelled) },
        { false, true, true, false, nameof(JobStopReason.Cancelled) },
        { false, true, true, true, nameof(JobStopReason.Cancelled) },
        { true, false, false, false, nameof(JobStopReason.WorkerStopping) },
        { true, false, false, true, nameof(JobStopReason.WorkerStopping) },
        { true, false, true, false, nameof(JobStopReason.WorkerStopping) },
        { true, false, true, true, nameof(JobStopReason.WorkerStopping) },
        { true, true, false, false, nameof(JobStopReason.WorkerStopping) },
        { true, true, false, true, nameof(JobStopReason.WorkerStopping) },
        { true, true, true, false, nameof(JobStopReason.WorkerStopping) },
        { true, true, true, true, nameof(JobStopReason.WorkerStopping) },
    };

    [Theory]
    [MemberData(nameof(Combinations))]
    public void The_reason_of_an_attempt_follows_the_order_of_the_sources(
        bool stopping,
        bool cancelled,
        bool leaseLost,
        bool timeLimit,
        string expected)
    {
        using var stoppingSource = new CancellationTokenSource();
        using var cancelledSource = new CancellationTokenSource();
        using var leaseLostSource = new CancellationTokenSource();
        using var timeLimitSource = new CancellationTokenSource();

        var signals = new JobAttemptSignals(
            maxDuration: TimeSpan.FromMinutes(1),
            stoppingSource.Token,
            cancelledSource.Token,
            timeLimitSource.Token,
            leaseLostSource.Token);

        // Cancelled in an order that is not the order of the rule, so an answer that depends on which fired first fails.
        if (timeLimit)
        {
            timeLimitSource.Cancel();
        }

        if (leaseLost)
        {
            leaseLostSource.Cancel();
        }

        if (cancelled)
        {
            cancelledSource.Cancel();
        }

        if (stopping)
        {
            stoppingSource.Cancel();
        }

        signals.Reason.ToString().ShouldBe(expected);
    }

    [Fact]
    public void The_reason_is_read_when_it_is_asked_for_and_not_when_the_signals_were_built()
    {
        using var stoppingSource = new CancellationTokenSource();
        using var cancelledSource = new CancellationTokenSource();
        using var leaseLostSource = new CancellationTokenSource();
        using var timeLimitSource = new CancellationTokenSource();

        var signals = new JobAttemptSignals(
            maxDuration: null,
            stoppingSource.Token,
            cancelledSource.Token,
            timeLimitSource.Token,
            leaseLostSource.Token);

        signals.Reason.ShouldBe(JobStopReason.None);

        timeLimitSource.Cancel();
        signals.Reason.ShouldBe(JobStopReason.MaxDurationExceeded);

        leaseLostSource.Cancel();
        signals.Reason.ShouldBe(JobStopReason.LeaseLost, "a lost lease outranks a time limit that fired first");
    }
}
