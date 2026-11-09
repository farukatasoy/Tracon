using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>
/// The in-memory windows of online evaluation (the hourly sampling budget and the score window) do not keep a
/// tenant whose window is over.
/// </summary>
/// <remarks>
/// <para>
/// Both windows are kept per tenant, and the tenant identifier comes from a request. A window that belongs to an
/// earlier hour, or holds no score, counts for nothing: the next sample of its tenant starts a new count, and a
/// summary of an empty window is the summary of no window. Both were kept for the life of the process all the
/// same, one entry for every tenant that ever had a run sampled.
/// </para>
/// <para>
/// A sweep must not change what a tenant is allowed: a window removed between the lookup and the lock would give
/// its tenant a second budget for the same hour. The race test counts the samples that pass across the hour
/// boundary.
/// </para>
/// </remarks>
public sealed class EvaluationWindowCardinalityTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static RunSampleRequest Completed(string tenant) => new()
    {
        RunId = Guid.NewGuid(),
        TenantId = tenant,
        AgentName = "support",
        Kind = RunKind.Agent,
        Status = RunStatus.Completed,
    };

    private static (RunSampler Sampler, ManualClock Clock) BuildSampler(int maxScoresPerHour = 5)
    {
        var options = new OnlineEvaluationOptions { Enabled = true, SampleRate = 1.0, MaxScoresPerHour = maxScoresPerHour };
        var clock = new ManualClock(Noon);

        return (new RunSampler(new InMemoryJobStore(), new StaticOptionsMonitor<OnlineEvaluationOptions>(options), clock), clock);
    }

    [Fact]
    public async Task The_sampling_windows_of_an_hour_that_is_over_are_removed_when_the_next_hour_starts()
    {
        var (sampler, clock) = BuildSampler();

        for (var tenant = 0; tenant < 3_000; tenant++)
        {
            (await sampler.SampleAsync(Completed($"tenant-{tenant}"))).ShouldBeTrue();
        }

        sampler.TrackedTenants.ShouldBe(3_000);

        clock.Advance(TimeSpan.FromHours(1));
        (await sampler.SampleAsync(Completed("newcomer"))).ShouldBeTrue();

        sampler.TrackedTenants.ShouldBe(1, "only the tenant that was sampled in the new hour has a window");
    }

    [Fact]
    public async Task A_tenant_is_not_given_a_second_budget_by_the_sweep_and_gets_a_new_one_each_hour()
    {
        var (sampler, clock) = BuildSampler(maxScoresPerHour: 2);

        (await sampler.SampleAsync(Completed("acme"))).ShouldBeTrue();
        (await sampler.SampleAsync(Completed("acme"))).ShouldBeTrue();
        (await sampler.SampleAsync(Completed("acme"))).ShouldBeFalse();

        // Inside the hour nothing is swept, so the budget stands.
        clock.Advance(TimeSpan.FromMinutes(30));
        (await sampler.SampleAsync(Completed("acme"))).ShouldBeFalse();

        // The next hour sweeps acme's old window and the same call starts a new one: two samples, then none.
        clock.Advance(TimeSpan.FromMinutes(30));
        (await sampler.SampleAsync(Completed("acme"))).ShouldBeTrue();
        (await sampler.SampleAsync(Completed("acme"))).ShouldBeTrue();
        (await sampler.SampleAsync(Completed("acme"))).ShouldBeFalse();
        sampler.TrackedTenants.ShouldBe(1);
    }

    [Fact]
    public async Task Samples_that_cross_the_hour_boundary_together_still_pass_no_more_than_the_budget()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var (sampler, clock) = BuildSampler(maxScoresPerHour: 3);

            (await sampler.SampleAsync(Completed("acme"))).ShouldBeTrue();
            clock.Advance(TimeSpan.FromHours(1));

            var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => sampler.SampleAsync(Completed("acme")).AsTask())));

            results.Count(static passed => passed).ShouldBe(3, $"attempt {attempt}: one tenant, one hour, a budget of three");
        }
    }

    [Fact]
    public async Task A_sweep_that_removes_the_sampling_window_between_the_lookup_and_the_lock_does_not_give_a_second_budget()
    {
        var (sampler, _) = BuildSampler(maxScoresPerHour: 1);
        var swept = false;

        // The next hour starts exactly where the race is: the caller holds the window it looked up and has not locked it.
        sampler.AfterLookup = () =>
        {
            if (swept)
            {
                return;
            }

            swept = true;
            sampler.DropWindowsOfEarlierHours(Noon.AddHours(1));
        };

        var first = await sampler.SampleAsync(Completed("acme"));
        var second = await sampler.SampleAsync(Completed("acme"));

        swept.ShouldBeTrue("the sweep must have run between the lookup and the lock, or the test proves nothing");
        first.ShouldBeTrue();
        second.ShouldBeFalse("the first sample was counted in the window that replaced the removed one, so the budget of one is spent");
    }

    [Fact]
    public async Task A_sweep_that_removes_the_score_window_between_the_lookup_and_the_lock_does_not_lose_the_score()
    {
        var clock = new ManualClock(Noon);
        var window = TimeSpan.FromHours(1);
        var options = new OnlineEvaluationOptions { EvaluationWindow = window, MinSampleSize = 2, LowScoreThreshold = 60 };
        var publisher = new CountingPublisher();
        var service = new OnlineEvalSummaryService(
            new InMemoryRunStore(),
            new StaticOptionsMonitor<OnlineEvaluationOptions>(options),
            publisher,
            clock);
        var swept = false;

        // The window is empty when the sweep runs (the score is added after the lock), so the sweep removes it.
        service.AfterLookup = () =>
        {
            if (swept)
            {
                return;
            }

            swept = true;
            service.DropEmptyWindows(Noon.AddHours(2), window);
        };

        await service.RecordScoreAsync("acme", 10);
        await service.RecordScoreAsync("acme", 10);

        swept.ShouldBeTrue("the sweep must have run between the lookup and the lock, or the test proves nothing");
        publisher.Published.ShouldBe(1, "two low scores are in one window: the first was not counted in a window nobody reads");
    }

    [Fact]
    public async Task The_score_windows_that_hold_no_score_are_removed()
    {
        var clock = new ManualClock(Noon);
        var options = new OnlineEvaluationOptions { EvaluationWindow = TimeSpan.FromHours(1) };
        var service = new OnlineEvalSummaryService(
            new InMemoryRunStore(),
            new StaticOptionsMonitor<OnlineEvaluationOptions>(options),
            webhookPublisher: null,
            clock);

        for (var tenant = 0; tenant < 3_000; tenant++)
        {
            await service.RecordScoreAsync($"tenant-{tenant}", 80);
        }

        service.TrackedTenants.ShouldBe(3_000);

        clock.Advance(TimeSpan.FromHours(2));
        await service.RecordScoreAsync("newcomer", 80);

        service.TrackedTenants.ShouldBe(1, "every score of the others has left the window");
    }

    [Fact]
    public async Task A_score_window_that_still_holds_a_score_is_kept_and_keeps_counting()
    {
        var clock = new ManualClock(Noon);
        var options = new OnlineEvaluationOptions { EvaluationWindow = TimeSpan.FromHours(1), MinSampleSize = 2, LowScoreThreshold = 60 };
        var publisher = new CountingPublisher();
        var service = new OnlineEvalSummaryService(
            new InMemoryRunStore(),
            new StaticOptionsMonitor<OnlineEvaluationOptions>(options),
            publisher,
            clock);

        await service.RecordScoreAsync("acme", 10);
        clock.Advance(TimeSpan.FromMinutes(50));
        await service.RecordScoreAsync("acme", 10);

        // 80 minutes in: the sweep is due, the first score has left the window and the second has not.
        clock.Advance(TimeSpan.FromMinutes(30));
        await service.RecordScoreAsync("other", 90);

        service.TrackedTenants.ShouldBe(2, "acme still has a score in the window");

        // acme's window kept its score, so the next low score makes two in the window and the alarm fires.
        var before = publisher.Published;
        await service.RecordScoreAsync("acme", 10);

        publisher.Published.ShouldBe(before + 1);
    }

    private sealed class CountingPublisher : IWebhookPublisher
    {
        public int Published { get; private set; }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            Published++;

            return new ValueTask<int>(1);
        }
    }
}
