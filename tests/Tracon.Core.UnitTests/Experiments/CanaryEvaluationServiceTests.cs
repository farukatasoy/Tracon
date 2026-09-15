using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Experiments;

/// <summary>
/// <see cref="CanaryEvaluationService"/> contract: automatic rollback, gradual
/// ramp-up, K1 default, K-089 audit trail, and singleton execution (Phase 56).
/// </summary>
public sealed class CanaryEvaluationServiceTests
{
    private const string TenantId = "default";
    private const string AgentName = "agent-a";

    [Fact]
    public async Task AutoRollbackEnabled_disabled_scans_and_stops_no_experiment()
    {
        var experiments = new CountingExperimentStore(new InMemoryExperimentStore());
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(experiments, maxErrorRateDelta: 0.01, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var service = CreateService(experiments, runs, auditLog, new InMemorySingletonLeaseStore(), autoRollbackEnabled: false, scanInterval: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        experiments.ListCalls.ShouldBe(0);

        var unchanged = await experiments.GetAsync(TenantId, experiment.Name);
        unchanged!.Status.ShouldBe(ExperimentStatus.Running);
        unchanged.RollbackReason.ShouldBeNull();
    }

    [Fact]
    public async Task Canary_worse_than_control_by_threshold_triggers_automatic_rollback()
    {
        var experiments = new InMemoryExperimentStore();
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(experiments, maxErrorRateDelta: 0.1, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var service = CreateService(experiments, runs, auditLog, new InMemorySingletonLeaseStore(), autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            async () => (await experiments.GetAsync(TenantId, experiment.Name))?.Status == ExperimentStatus.Stopped,
            "the unhealthy canary to roll back",
            TestContext.Current.CancellationToken);
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
    public async Task Rollback_is_not_applied_when_audit_log_write_fails()
    {
        var experiments = new CountingExperimentStore(new InMemoryExperimentStore());
        var runs = new InMemoryRunStore();
        var auditLog = new ThrowingAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(experiments, maxErrorRateDelta: 0.1, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var service = CreateService(experiments, runs, auditLog, new InMemorySingletonLeaseStore(), autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20));

        await service.StartAsync(TestContext.Current.CancellationToken);
        await WaitUntilAsync(
            () => Task.FromResult(auditLog.WriteCalls > 0),
            "the failed audit write",
            TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var stillRunning = await experiments.GetAsync(TenantId, experiment.Name);
        stillRunning!.Status.ShouldBe(ExperimentStatus.Running);
        stillRunning.RollbackReason.ShouldBeNull();
        experiments.RollbackCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Gradual_ramp_advances_weight_by_one_step_and_keeps_existing_assignments()
    {
        var experiments = new InMemoryExperimentStore();
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        // 🚨 A single-step list is deliberate: with RampInterval=Zero and a fast
        // ScanInterval, multiple rounds could be skipped; with more than one step
        // the test would depend on its own timing. With one step the ramp is
        // PINNED at 25 (no other step above 25 exists).
        const int rampedCanaryWeight = 25;

        var experiment = await CreateRunningCanaryExperimentAsync(
            experiments,
            maxErrorRateDelta: 0.5,
            minSampleSize: 5,
            rampSteps: [rampedCanaryWeight],
            canaryWeight: 5,
            controlWeight: 95);

        // Both are healthy: the threshold is not exceeded, the ramp should continue.
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 5, failed: 0);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 20, failed: 0);

        // BEFORE the ramp: measure which arm two fixed keys land in.
        // The canary range only GROWS with the ramp (see ExperimentAssignmentResolver.
        // OrderForAssignment), so a canary key found with the current (pre-ramp)
        // experiment is safe. The control key must also stay in the control range
        // AFTER the ramp; otherwise a key landing in the [5,25) range would be found
        // as "control" by FindAssignmentKey but would shift to canary during the
        // ramp — since the experiment id differs on every run, the hash also
        // changes, and this test would fail at random (~21% chance).
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
        await WaitUntilAsync(
            async () =>
            {
                var current = await experiments.GetAsync(TenantId, experiment.Name);
                return current is not null && current.Variants.Single(static variant =>
                    string.Equals(variant.Name, "canary", StringComparison.Ordinal)).Weight == rampedCanaryWeight;
            },
            "the healthy canary to advance",
            TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        var advanced = await experiments.GetAsync(TenantId, experiment.Name);
        advanced!.Status.ShouldBe(ExperimentStatus.Running);
        advanced.Variants.Single(static v => string.Equals(v.Name, "canary", StringComparison.Ordinal)).Weight.ShouldBe(rampedCanaryWeight);
        advanced.Variants.Single(static v => string.Equals(v.Name, "control", StringComparison.Ordinal)).Weight.ShouldBe(100 - rampedCanaryWeight);

        // 🚨 56.4: even if the weight changes, existing sessions must NOT switch arms.
        ExperimentAssignmentResolver.SelectVariant(advanced, canaryKey).Name.ShouldBe("canary");
        ExperimentAssignmentResolver.SelectVariant(advanced, controlKey).Name.ShouldBe("control");
    }

    [Fact]
    public async Task Evaluation_runs_on_only_one_of_two_instances()
    {
        var innerExperiments = new InMemoryExperimentStore();
        var runs = new InMemoryRunStore();
        var auditLog = new InMemoryAuditLog();

        var experiment = await CreateRunningCanaryExperimentAsync(innerExperiments, maxErrorRateDelta: 0.1, minSampleSize: 5);
        await SeedRunsAsync(runs, experiment.Id, "canary", completed: 0, failed: 10);
        await SeedRunsAsync(runs, experiment.Id, "control", completed: 10, failed: 0);

        var leaseStore = new InMemorySingletonLeaseStore();
        // 🚨 The lease MUST NOT be able to expire inside the test window:
        // renewal runs at one third of the lease but never faster than
        // SingletonGuard.MinimumRenewInterval, so a 1-second lease used to
        // expire under its live owner and the second instance took over -
        // this assertion then depended on the window staying under a second.
        // SingletonExecutionOptionsValidator rejects that configuration now (K-743).
        var singletonOptions = Options(new SingletonExecutionOptions { Enabled = true, LeaseDuration = TimeSpan.FromMinutes(5) });

        var storeA = new CountingExperimentStore(innerExperiments);
        var storeB = new CountingExperimentStore(innerExperiments);

        var serviceA = CreateService(storeA, runs, auditLog, leaseStore, autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20), singletonOptions: singletonOptions);
        var serviceB = CreateService(storeB, runs, auditLog, leaseStore, autoRollbackEnabled: true, scanInterval: TimeSpan.FromMilliseconds(20), singletonOptions: singletonOptions);

        await serviceA.StartAsync(TestContext.Current.CancellationToken);
        await serviceB.StartAsync(TestContext.Current.CancellationToken);

        await WaitUntilAsync(
            () => Task.FromResult(storeA.ListCalls > 0 ^ storeB.ListCalls > 0),
            "exactly one evaluator to acquire the singleton lease",
            TestContext.Current.CancellationToken);

        // Stop the non-holder first. Otherwise it can acquire the lease while
        // the original holder is stopping and make this concurrency assertion
        // observe two sequential owners as if they had run concurrently.
        if (storeA.ListCalls > 0)
        {
            await serviceB.StopAsync(TestContext.Current.CancellationToken);
            await serviceA.StopAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            await serviceA.StopAsync(TestContext.Current.CancellationToken);
            await serviceB.StopAsync(TestContext.Current.CancellationToken);
        }

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
            metrics: null,
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
            Id = TraconId.NewId(),
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
        var runId = TraconId.NewId();
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

    /// <summary>Finds the first assignment key that produces the given arm (deterministic hash scan).</summary>
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

        throw new InvalidOperationException($"No key found that produces the '{variantName}' arm.");
    }

    /// <summary>Returns a probe experiment that pins the canary arm to the given weight and completes the rest with the other arm.</summary>
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

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string description,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);

        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {description}.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    /// <summary>Fake <see cref="IOptionsMonitor{T}"/> that returns a fixed value and never watches for changes.</summary>
    private sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
        where T : class
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        private int _writeCalls;

        public int WriteCalls => Volatile.Read(ref _writeCalls);

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _writeCalls);
            throw new InvalidOperationException("could not reach audit log store");
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by this test");
    }

    /// <summary>
    /// Wrapper that tracks call counts for <see cref="IExperimentStore.ListRunningWithCanaryAsync"/>
    /// and <see cref="IExperimentStore.RollbackCanaryAsync"/>, delegating everything else to the inner store.
    /// </summary>
    private sealed class CountingExperimentStore(IExperimentStore inner) : IExperimentStore
    {
        private int _listCalls;
        private int _rollbackCalls;

        public int ListCalls => Volatile.Read(ref _listCalls);

        public int RollbackCalls => Volatile.Read(ref _rollbackCalls);

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
            Interlocked.Increment(ref _listCalls);
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
            Interlocked.Increment(ref _rollbackCalls);
            return inner.RollbackCanaryAsync(tenantId, name, variants, reason, cancellationToken);
        }
    }
}
