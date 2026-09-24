using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The service types whose constructors became internal (phase 188) still
/// resolve from the container a real host builds.
/// </summary>
/// <remarks>
/// <para>
/// The container only activates a public constructor. A type-based
/// registration (<c>TryAddSingleton&lt;T&gt;()</c>) of a type with an internal
/// constructor compiles and fails only at the first resolution, so a unit test
/// that calls the constructor directly cannot see it. <see cref="RunSampler"/>
/// was such a registration and moved to a factory.
/// </para>
/// <para>
/// A factory has its own failure: an optional dependency it forgets is passed
/// as <see langword="null"/> without any error, where the container used to
/// fill it. The clock and the logger tests below prove the factory passes both.
/// </para>
/// </remarks>
public sealed class DiConstructedServiceResolutionTests
{
    private const string Tenant = "default";

    private static readonly Uri Run = new("/tracon/api/agents/kod-agent/run", UriKind.Relative);

    [Fact]
    public async Task Every_service_with_an_internal_constructor_resolves_from_the_container()
    {
        await using var host = await TraconTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition())
            .UseSkillScripts(static options =>
            {
                options.PlatformIsolationAcknowledged = true;
                options.Interpreters["sh"] = "/bin/bash";
            }));

        var services = host.Services;

        services.GetRequiredService<AgentDefinitionCompiler>().ShouldNotBeNull();
        services.GetRequiredService<TraconDiagnosticsCollector>().ShouldNotBeNull();
        services.GetRequiredService<IModelProviderRegistry>().ShouldBeOfType<ModelProviderRegistry>();
        services.GetRequiredService<SandboxedSkillScriptRunner>().ShouldNotBeNull();
        services.GetRequiredService<ContentGuardPipeline>().ShouldNotBeNull();
        services.GetRequiredService<AgentSessionManager>().ShouldNotBeNull();
        services.GetRequiredService<QuotaEnforcer>().ShouldNotBeNull();
        services.GetRequiredService<ModelProviderHealthCache>().ShouldNotBeNull();
        services.GetRequiredService<RunSampler>().ShouldNotBeNull();
        services.GetRequiredService<SkillScriptSupport>().ShouldNotBeNull();
        services.GetRequiredService<ModelProviderCircuitBreaker>().ShouldNotBeNull();
        services.GetRequiredService<TraconMetrics>().ShouldNotBeNull();
    }

    [Fact]
    public async Task RunSampler_is_a_single_instance()
    {
        await using var host = await TraconTestHost.StartAsync();

        // Two instances would keep two hourly windows, so a tenant's sampling
        // budget would double.
        host.Services.GetRequiredService<RunSampler>()
            .ShouldBeSameAs(host.Services.GetRequiredService<RunSampler>());
    }

    [Fact]
    public async Task RunSampler_uses_the_registered_clock_for_its_hourly_window()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 24, 10, 15, 0, TimeSpan.Zero));

        await using var host = await TraconTestHost.StartAsync(
            configureServices: services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.Configure<OnlineEvaluationOptions>(static options =>
                {
                    options.Enabled = true;
                    options.SampleRate = 1.0;
                    options.MaxScoresPerHour = 1;
                });
            });

        var sampler = host.Services.GetRequiredService<RunSampler>();
        var cancellationToken = TestContext.Current.CancellationToken;

        (await sampler.SampleAsync(Request(), cancellationToken)).ShouldBeTrue();
        (await sampler.SampleAsync(Request(), cancellationToken)).ShouldBeFalse("the hourly budget of one is spent");

        // With TimeProvider.System in the factory the window would not move
        // here, and the third call would be rejected too.
        clock.Advance(TimeSpan.FromHours(1));

        (await sampler.SampleAsync(Request(), cancellationToken)).ShouldBeTrue("a new hour opens a new window");
    }

    [Fact]
    public async Task RunSampler_logs_a_queue_failure_and_the_run_still_completes()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder =>
            {
                builder.AddAgent(TestData.Definition());
                OnlineEvalEnqueueFailure.Install(builder.Services);
            },
            configureServices: static services => services.Configure<OnlineEvaluationOptions>(static options =>
            {
                options.Enabled = true;
                options.SampleRate = 1.0;
            }));

        var sampled = await host.Services.GetRequiredService<RunSampler>()
            .SampleAsync(Request(), TestContext.Current.CancellationToken);

        sampled.ShouldBeFalse();
        host.Logs.Entries.ShouldContain(
            static entry => entry.StartsWith("Warning Tracon.RunSampler ", StringComparison.Ordinal)
                            && entry.Contains(OnlineEvalEnqueueFailure.Message, StringComparison.Ordinal),
            "the factory must pass ILogger<RunSampler>; without it the failure leaves no trace");

        using (var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = "hello" },
            TestContext.Current.CancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/tracon/api/runs", UriKind.Relative),
            TestContext.Current.CancellationToken);

        runs.ShouldHaveSingleItem().Status.ShouldBe(RunStatus.Completed);
    }

    private static RunSampleRequest Request() => new()
    {
        RunId = Guid.NewGuid(),
        TenantId = Tenant,
        AgentName = "kod-agent",
        Kind = RunKind.Agent,
        Status = RunStatus.Completed,
    };

    /// <summary>
    /// Fails every online-evaluation enqueue and delegates everything else to
    /// the store Tracon registered.
    /// </summary>
    private static class OnlineEvalEnqueueFailure
    {
        public const string Message = "Injected: the online evaluation queue is unavailable.";

        /// <summary>
        /// Called through <c>configureTracon</c>, so Tracon's own
        /// <c>TryAddSingleton&lt;IJobStore&gt;</c> has already run and there is a
        /// real implementation to delegate to.
        /// </summary>
        public static void Install(IServiceCollection services)
        {
            var registered = services.Last(static d => d.ServiceType == typeof(IJobStore));

            services.Remove(registered);
            services.AddSingleton<IJobStore>(provider => new Store(
                (IJobStore)ActivatorUtilities.CreateInstance(provider, registered.ImplementationType!)));
        }

        private sealed class Store(IJobStore inner) : IJobStore
        {
            public ValueTask<JobRecord> EnqueueAsync(
                JobRecord job, IReadOnlyList<string> items, CancellationToken cancellationToken = default)
                => string.Equals(job.HandlerKey, JobHandlerKeys.OnlineEval, StringComparison.Ordinal)
                    ? throw new InvalidOperationException(Message)
                    : inner.EnqueueAsync(job, items, cancellationToken);

            public ValueTask<JobRecord?> LeaseAsync(
                string owner, TimeSpan leaseDuration, IReadOnlyList<string>? lanes,
                CancellationToken cancellationToken = default)
                => inner.LeaseAsync(owner, leaseDuration, lanes, cancellationToken);

            public ValueTask RenewLeaseAsync(
                Guid jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
                => inner.RenewLeaseAsync(jobId, owner, leaseDuration, cancellationToken);

            public ValueTask<bool> MarkRunningAsync(Guid jobId, string owner, CancellationToken cancellationToken = default)
                => inner.MarkRunningAsync(jobId, owner, cancellationToken);

            public ValueTask CompleteAsync(JobCompletion completion, CancellationToken cancellationToken = default)
                => inner.CompleteAsync(completion, cancellationToken);

            public ValueTask ReleaseForRetryAsync(
                Guid jobId, string errorMessage, TimeSpan? retryAfter = null,
                CancellationToken cancellationToken = default)
                => inner.ReleaseForRetryAsync(jobId, errorMessage, retryAfter, cancellationToken);

            public ValueTask<bool> CancelAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
                => inner.CancelAsync(tenantId, jobId, cancellationToken);

            public ValueTask<JobRecord?> GetAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
                => inner.GetAsync(tenantId, jobId, cancellationToken);

            public ValueTask<IReadOnlyList<JobRecord>> QueryAsync(JobQuery query, CancellationToken cancellationToken = default)
                => inner.QueryAsync(query, cancellationToken);

            public ValueTask<IReadOnlyList<JobItemRecord>> ListItemsAsync(Guid jobId, CancellationToken cancellationToken = default)
                => inner.ListItemsAsync(jobId, cancellationToken);

            public ValueTask ReportItemAsync(JobItemResult item, CancellationToken cancellationToken = default)
                => inner.ReportItemAsync(item, cancellationToken);

            public ValueTask<IReadOnlyList<JobQueueDepth>> GetQueueDepthAsync(CancellationToken cancellationToken = default)
                => inner.GetQueueDepthAsync(cancellationToken);
        }
    }
}
