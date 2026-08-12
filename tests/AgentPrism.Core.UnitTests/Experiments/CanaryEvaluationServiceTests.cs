using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Experiments;

/// <summary>
/// <see cref="CanaryEvaluationService"/> sozlesmesi — otomatik geri alma, kademeli
/// artirma, K1 varsayilani, K-089 denetim izi ve tekil koşum (Faz 56).
/// </summary>
public sealed class CanaryEvaluationServiceTests
{
    private const string TenantId = "default";
    private const string AgentName = "agent-a";

    [Fact]
    public async Task AutoRollbackEnabled_kapaliyken_hicbir_deney_taranmaz_ve_durdurulmaz()
    {
        var experiments = new CountingExperimentStore(new InMemoryExperimentStore());
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(experiments, maxErrorRateDelta: 0.01, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var service = CreateService(experiments, runs, auditLog, new InMemorySingletonLeaseStore(), autoRollbackEnabled: false, scanInterval: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        experiments.ListCalls.ShouldBe(0);

        var unchanged = await experiments.GetAsync(TenantId, experiment.Name);
        unchanged!.Status.ShouldBe(ExperimentStatus.Running);
        unchanged.RollbackReason.ShouldBeNull();
    }

    [Fact]
    public async Task Kanarya_kontrolden_esik_kadar_kotuyse_otomatik_geri_alinir()
    {
        var experiments = new InMemoryExperimentStore();
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(experiments, maxErrorRateDelta: 0.1, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var service = CreateService(experiments, runs, auditLog, new InMemorySingletonLeaseStore(), autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var rolledBack = await experiments.GetAsync(TenantId, experiment.Name);
        rolledBack!.Status.ShouldBe(ExperimentStatus.Stopped);
        rolledBack.RollbackReason.ShouldNotBeNull();
        rolledBack.Variants.Single(static v => string.Equals(v.Name, "canary", StringComparison.Ordinal)).Weight.ShouldBe(0);
        rolledBack.Variants.Single(static v => string.Equals(v.Name, "control", StringComparison.Ordinal)).Weight.ShouldBe(100);

        var audited = await auditLog.QueryAsync(new AuditQuery { TenantId = TenantId, Action = "experiment.auto_rollback", Limit = 10 });
        audited.ShouldHaveSingleItem();
        audited[0].Entity.ShouldBe($"experiment:{experiment.Name}");
    }

    [Fact]
    public async Task Denetim_izi_yazilamazsa_geri_alma_uygulanmaz()
    {
        var experiments = new CountingExperimentStore(new InMemoryExperimentStore());
        var runs = new InMemoryRunStore();
        var auditLog = new ThrowingAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(experiments, maxErrorRateDelta: 0.1, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var service = CreateService(experiments, runs, auditLog, new InMemorySingletonLeaseStore(), autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var stillRunning = await experiments.GetAsync(TenantId, experiment.Name);
        stillRunning!.Status.ShouldBe(ExperimentStatus.Running);
        stillRunning.RollbackReason.ShouldBeNull();
        experiments.RollbackCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Kademeli_artirma_agirligi_bir_adim_ilerletir_ve_var_olan_atamalari_degistirmez()
    {
        var experiments = new InMemoryExperimentStore();
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        // 🚨 Tek adimli liste bilerek secildi: RampInterval=Zero ve hizli
        // ScanInterval ile birden fazla tur atlanabilir; birden fazla adim
        // olsaydi test testin kendi zamanlamasina bagimli hale gelirdi. Tek
        // adimda ramp 25'te SABITLENIR (25'ten buyuk baska adim yok).
        const int rampedCanaryWeight = 25;

        var experiment = await CreateRunningCanaryExperimentAsync(
            experiments,
            maxErrorRateDelta: 0.5,
            minSampleSize: 5,
            rampSteps: [rampedCanaryWeight],
            canaryWeight: 5,
            controlWeight: 95);

        // Ikisi de saglikli: eskik asilmiyor, ramp'e devam edilmeli.
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 5, failed: 0);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 20, failed: 0);

        // Ramp'ten ONCE: sabit iki anahtarin hangi kola dustugunu olc.
        // Kanarya araligi rampla yalniz BUYUR (bkz. ExperimentAssignmentResolver.
        // OrderForAssignment), bu yuzden mevcut (ramp oncesi) deneyle bulunan bir
        // kanarya anahtari guvenlidir. Kontrol anahtari ise ramp SONRASI kontrol
        // araliginda da kalmalidir; aksi halde [5,25) araligina dusen bir anahtar
        // FindAssignmentKey tarafindan "control" olarak bulunur ama ramp sirasinda
        // kanaryaya kayar — deney kimligi her kosuda farkli oldugu icin hash de
        // degisir ve bu test rastgele (~%21 ihtimalle) basarisiz olurdu.
        var canaryKey = FindAssignmentKey(experiment, "canary");
        var controlKey = FindAssignmentKey(WithCanaryWeight(experiment, rampedCanaryWeight), "control");

        var service = CreateService(
            experiments,
            runs,
            auditLog,
            new InMemorySingletonLeaseStore(),
            autoRollbackEnabled: true,
            scanInterval: TimeSpan.FromMilliseconds(20),
            rampInterval: TimeSpan.Zero);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var advanced = await experiments.GetAsync(TenantId, experiment.Name);
        advanced!.Status.ShouldBe(ExperimentStatus.Running);
        advanced.Variants.Single(static v => string.Equals(v.Name, "canary", StringComparison.Ordinal)).Weight.ShouldBe(rampedCanaryWeight);
        advanced.Variants.Single(static v => string.Equals(v.Name, "control", StringComparison.Ordinal)).Weight.ShouldBe(100 - rampedCanaryWeight);

        // 🚨 56.4: agirlik degisse de var olan oturumlar kolunu DEGISTIRMEMELIDIR.
        ExperimentAssignmentResolver.SelectVariant(advanced, canaryKey).Name.ShouldBe("canary");
        ExperimentAssignmentResolver.SelectVariant(advanced, controlKey).Name.ShouldBe("control");
    }

    [Fact]
    public async Task Iki_ornekte_degerlendirme_yalniz_birinde_kosar()
    {
        var innerExperiments = new InMemoryExperimentStore();
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(innerExperiments, maxErrorRateDelta: 0.1, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var leaseStore = new InMemorySingletonLeaseStore();
        var singletonOptions = Options(new SingletonExecutionOptions { Enabled = true, LeaseDuration = TimeSpan.FromSeconds(1) });

        var storeA = new CountingExperimentStore(innerExperiments);
        var storeB = new CountingExperimentStore(innerExperiments);

        var serviceA = CreateService(storeA, runs, auditLog, leaseStore, autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20), singletonOptions: singletonOptions);
        var serviceB = CreateService(storeB, runs, auditLog, leaseStore, autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20), singletonOptions: singletonOptions);

        await serviceA.StartAsync(TestContext.Current.CancellationToken);
        await serviceB.StartAsync(TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);

        await serviceA.StopAsync(TestContext.Current.CancellationToken);
        await serviceB.StopAsync(TestContext.Current.CancellationToken);

        (storeA.ListCalls > 0 ^ storeB.ListCalls > 0).ShouldBeTrue(
            $"storeA.ListCalls={storeA.ListCalls}, storeB.ListCalls={storeB.ListCalls}");
    }

    private static CanaryEvaluationService CreateService(
        IExperimentStore experiments,
        IRunStore runs,
        IAuditLog auditLog,
        ISingletonLeaseStore leaseStore,
        bool autoRollbackEnabled,
        TimeSpan scanInterval,
        TimeSpan? rampInterval = null,
        IOptionsMonitor<SingletonExecutionOptions>? singletonOptions = null)
        => new(
            experiments,
            runs,
            auditLog,
            leaseStore,
            Options(new CanaryOptions { AutoRollbackEnabled = autoRollbackEnabled, ScanInterval = scanInterval }),
            singletonOptions ?? Options(new SingletonExecutionOptions()),
            new SchemaReadyGate([]),
            logger: NullLogger<CanaryEvaluationService>.Instance);

    private static async Task<Experiment> CreateRunningCanaryExperimentAsync(
        IExperimentStore experiments,
        double maxErrorRateDelta,
        int minSampleSize,
        IReadOnlyList<int>? rampSteps = null,
        int canaryWeight = 50,
        int controlWeight = 50)
    {
        var draft = await experiments.SaveAsync(new Experiment
        {
            Id = AgentPrismId.NewId(),
            TenantId = TenantId,
            Name = $"canary-{Guid.NewGuid():N}",
            AgentName = AgentName,
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = controlWeight },
                new ExperimentVariant { Name = "canary", Version = 2, Weight = canaryWeight },
            ],
        });

        var withPolicy = await experiments.SetCanaryPolicyAsync(
            TenantId,
            draft.Name,
            new CanaryPolicy
            {
                CanaryVariant = "canary",
                MaxErrorRateDelta = maxErrorRateDelta,
                MinSampleSize = minSampleSize,
                RampSteps = rampSteps ?? [],
                RampInterval = TimeSpan.Zero,
            });

        withPolicy.ShouldNotBeNull();

        return await experiments.StartAsync(TenantId, draft.Name);
    }

    private static async Task SeedRunsAsync(IRunStore runs, Guid experimentId, string variant, int completed, int failed)
    {
        for (var i = 0; i < completed; i++)
        {
            await SeedRunAsync(runs, experimentId, variant, RunStatus.Completed);
        }

        for (var i = 0; i < failed; i++)
        {
            await SeedRunAsync(runs, experimentId, variant, RunStatus.Failed);
        }
    }

    private static async Task SeedRunAsync(IRunStore runs, Guid experimentId, string variant, RunStatus status)
    {
        var runId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            StartedAt = now.AddSeconds(-1),
            TenantId = TenantId,
            ExperimentId = experimentId,
            Variant = variant,
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = status,
            CompletedAt = now,
        });
    }

    /// <summary>Verilen kolu ureten ilk atama anahtarini bulur (deterministik hash taramasi).</summary>
    private static string FindAssignmentKey(Experiment experiment, string variantName)
    {
        for (var i = 0; i < 1000; i++)
        {
            var key = $"session-{i}";

            if (string.Equals(ExperimentAssignmentResolver.SelectVariant(experiment, key).Name, variantName, StringComparison.Ordinal))
            {
                return key;
            }
        }

        throw new InvalidOperationException($"'{variantName}' kolunu ureten bir anahtar bulunamadi.");
    }

    /// <summary>Kanarya kolunu verilen agirliga sabitleyip digerini tamamlayan bir prob deneyi dondurur.</summary>
    private static Experiment WithCanaryWeight(Experiment experiment, int canaryWeight)
    {
        var canaryVariant = experiment.Canary!.CanaryVariant;

        return experiment with
        {
            Variants =
            [
                .. experiment.Variants.Select(variant => variant with
                {
                    Weight = string.Equals(variant.Name, canaryVariant, StringComparison.Ordinal)
                        ? canaryWeight
                        : 100 - canaryWeight,
                }),
            ],
        };
    }

    private static StaticOptionsMonitor<T> Options<T>(T value) where T : class => new(value);

    /// <summary>Sabit bir deger dondüren, degisikligi izlemeyen sahte <see cref="IOptionsMonitor{T}"/>.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("denetim izi deposuna erisilemedi");

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());
    }

    /// <summary>
    /// <see cref="IExperimentStore.ListRunningWithCanaryAsync"/> ve
    /// <see cref="IExperimentStore.RollbackCanaryAsync"/> cagri sayaclarini tutan,
    /// digerlerini ic depoya devreden sarmalayici.
    /// </summary>
    private sealed class CountingExperimentStore(IExperimentStore inner) : IExperimentStore
    {
        public int ListCalls { get; private set; }

        public int RollbackCalls { get; private set; }

        public ValueTask<IReadOnlyList<Experiment>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
            => inner.ListAsync(tenantId, cancellationToken);

        public ValueTask<Experiment?> GetAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.GetAsync(tenantId, name, cancellationToken);

        public ValueTask<Experiment?> GetRunningAsync(string tenantId, string agentName, CancellationToken cancellationToken = default)
            => inner.GetRunningAsync(tenantId, agentName, cancellationToken);

        public ValueTask<Experiment> SaveAsync(Experiment experiment, CancellationToken cancellationToken = default)
            => inner.SaveAsync(experiment, cancellationToken);

        public ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.DeleteAsync(tenantId, name, cancellationToken);

        public ValueTask<Experiment> StartAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.StartAsync(tenantId, name, cancellationToken);

        public ValueTask<Experiment> StopAsync(string tenantId, string name, CancellationToken cancellationToken = default)
            => inner.StopAsync(tenantId, name, cancellationToken);

        public ValueTask<IReadOnlyList<Experiment>> ListRunningWithCanaryAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            return inner.ListRunningWithCanaryAsync(cancellationToken);
        }

        public ValueTask<Experiment> SetCanaryPolicyAsync(
            string tenantId,
            string name,
            CanaryPolicy? policy,
            CancellationToken cancellationToken = default)
            => inner.SetCanaryPolicyAsync(tenantId, name, policy, cancellationToken);

        public ValueTask<Experiment> AdvanceCanaryRampAsync(
            string tenantId,
            string name,
            IReadOnlyList<ExperimentVariant> variants,
            CancellationToken cancellationToken = default)
            => inner.AdvanceCanaryRampAsync(tenantId, name, variants, cancellationToken);

        public ValueTask<Experiment> RollbackCanaryAsync(
            string tenantId,
            string name,
            IReadOnlyList<ExperimentVariant> variants,
            string reason,
            CancellationToken cancellationToken = default)
        {
            RollbackCalls++;
            return inner.RollbackCanaryAsync(tenantId, name, variants, reason, cancellationToken);
        }
    }
}
