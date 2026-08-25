using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Tests for the online evaluation sampler (Phase 49).</summary>
public sealed class RunSamplerTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task Nothing_is_sampled_with_default_settings()
    {
        var (sampler, jobs) = Build();

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_is_sampled_when_Enabled_is_on_but_SampleRate_is_zero()
    {
        var (sampler, jobs) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 0.0;
        });

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Every_completed_run_is_sampled_with_SampleRate_1()
    {
        var (sampler, jobs) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 1.0;
        });

        var runId = Guid.NewGuid();
        (await sampler.SampleAsync(Completed(runId))).ShouldBeTrue();

        var queued = await jobs.QueryAsync(new JobQuery { TenantId = Tenant });
        queued.Count.ShouldBe(1);
        queued[0].Kind.ShouldBe(JobKind.OnlineEval);
        queued[0].TargetName.ShouldBe(Agent);

        var items = await jobs.ListItemsAsync(queued[0].Id);
        items.Single().Input.ShouldBe(runId.ToString());
    }

    [Fact]
    public async Task Same_runId_decides_deterministically()
    {
        var (sampler, _) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 0.5;
        });

        var runId = Guid.NewGuid();
        var first = await sampler.SampleAsync(Completed(runId));
        var second = await sampler.SampleAsync(Completed(runId));

        second.ShouldBe(first);
    }

    [Fact]
    public async Task A_run_of_kind_Eval_is_not_sampled()
    {
        var (sampler, jobs) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 1.0;
        });

        (await sampler.SampleAsync(Completed(Guid.NewGuid()) with { Kind = RunKind.Eval })).ShouldBeFalse();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_run_started_inside_a_judge_scope_is_not_sampled()
    {
        var (sampler, jobs) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 1.0;
        });

        using (AmbientSamplingSuppressionScope.Begin())
        {
            (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();
        }

        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_run_is_not_sampled()
    {
        var (sampler, jobs) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 1.0;
        });

        (await sampler.SampleAsync(Completed(Guid.NewGuid()) with { Status = RunStatus.Failed })).ShouldBeFalse();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Only_agents_in_the_AgentNames_list_are_sampled()
    {
        var (sampler, jobs) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 1.0;
            options.AgentNames.Add("another-agent");
        });

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sampling_stops_when_MaxScoresPerHour_is_exceeded()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));
        var (sampler, jobs) = Build(
            options =>
            {
                options.Enabled = true;
                options.SampleRate = 1.0;
                options.MaxScoresPerHour = 2;
            },
            clock);

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeTrue();
        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeTrue();
        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();

        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Budget_resets_when_the_hour_changes()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 7, 10, 0, 0, TimeSpan.Zero));
        var (sampler, jobs) = Build(
            options =>
            {
                options.Enabled = true;
                options.SampleRate = 1.0;
                options.MaxScoresPerHour = 1;
            },
            clock);

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeTrue();
        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();

        clock.Advance(TimeSpan.FromHours(1));

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeTrue();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).Count.ShouldBe(2);
    }

    private static RunSampleRequest Completed(Guid runId) => new()
    {
        RunId = runId,
        TenantId = Tenant,
        AgentName = Agent,
        Kind = RunKind.Agent,
        Status = RunStatus.Completed,
    };

    private static (RunSampler Sampler, IJobStore Jobs) Build(
        Action<OnlineEvaluationOptions>? configure = null,
        TimeProvider? clock = null)
    {
        var options = new OnlineEvaluationOptions();
        configure?.Invoke(options);

        var jobs = new InMemoryJobStore();

        return (
            new RunSampler(jobs, new StaticOptionsMonitor<OnlineEvaluationOptions>(options), clock ?? new ManualTimeProvider()),
            jobs);
    }
}
