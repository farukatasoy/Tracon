using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Evaluation;

/// <summary>Cevrimici degerlendirme ornekleyicisinin testleri (Faz 49).</summary>
public sealed class RunSamplerTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    [Fact]
    public async Task Varsayilan_ayarlarla_hicbir_sey_orneklenmez()
    {
        var (sampler, jobs) = Build();

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Enabled_acik_ama_SampleRate_sifirken_hicbir_sey_orneklenmez()
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
    public async Task SampleRate_1_ile_her_tamamlanan_calistirma_orneklenir()
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
    public async Task Ayni_runId_deterministik_karar_verir()
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
    public async Task Eval_turundeki_calistirma_orneklenmez()
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
    public async Task Basarisiz_calistirma_orneklenmez()
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
    public async Task AgentNames_listesindeyse_yalniz_o_agentlar_orneklenir()
    {
        var (sampler, jobs) = Build(options =>
        {
            options.Enabled = true;
            options.SampleRate = 1.0;
            options.AgentNames.Add("baska-agent");
        });

        (await sampler.SampleAsync(Completed(Guid.NewGuid()))).ShouldBeFalse();
        (await jobs.QueryAsync(new JobQuery { TenantId = Tenant })).ShouldBeEmpty();
    }

    [Fact]
    public async Task MaxScoresPerHour_asilinca_ornekleme_durur()
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
    public async Task Saat_degisince_butce_sifirlanir()
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
