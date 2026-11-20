using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A queued run whose job the worker fails without running it does not stay <c>Queued</c>.
/// </summary>
/// <remarks>
/// <para>
/// A queued run opens its row as <c>Queued</c> before its job, so <c>GET /api/runs/{id}</c> answers at once. The handler
/// moves the row on, and closes it when it fails. A job that fails before its handler is called (the host's tenant scope
/// could not be opened, the handler could not be built, no handler holds the key, or the lease ran out of attempts) never
/// reached that code: the job ended <c>Failed</c> and the row it promised in the <c>202</c> stayed <c>Queued</c> for ever,
/// because run reconciliation reads only running rows.
/// </para>
/// </remarks>
public sealed class QueuedRunClosedWithoutRunningTests
{
    private const string AgentName = "closed-agent";

    private static readonly Uri Run = new($"/tracon/api/agents/{AgentName}/run", UriKind.Relative);

    [Fact]
    public async Task A_run_whose_job_fails_because_the_hosts_tenant_scope_cannot_be_opened_is_closed_as_failed()
    {
        await using var host = await StartAsync(runWorker: true, configureServices: static services =>
            services.AddSingleton<IHostTenantScope, ThrowingScope>());

        var runId = await QueueAsync(host);
        var job = await QueuedWorkflowHarness.WaitForJobAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(JobErrorCodes.HostTenantScopeFailed);

        var run = await host.WaitForTerminalRunAsync(runId);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(JobErrorCodes.HostTenantScopeFailed);
        run.Error.Message.ShouldStartWith(job.ErrorMessage!, Case.Sensitive);
    }

    [Fact]
    public async Task A_run_whose_worker_died_before_it_started_is_closed_as_failed_when_its_job_runs_out_of_attempts()
    {
        await using var host = await StartAsync(runWorker: false);

        var runId = await QueueAsync(host);

        // A worker that leased the job and died before the handler wrote anything: nobody renews its short lease.
        await QueuedWorkflowHarness.LeaseAsync(host, "dead-worker", TimeSpan.FromMilliseconds(300));

        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.Queued);

        var job = await QueuedWorkflowHarness.FinishWithWorkerAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.ErrorMessage.ShouldNotBeNull().ShouldStartWith(JobErrorCodes.AttemptsExhausted);

        var run = await host.WaitForTerminalRunAsync(runId);

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error.ShouldNotBeNull().Type.ShouldBe(JobErrorCodes.AttemptsExhausted);
    }

    private static Task<TraconTestHost> StartAsync(bool runWorker, Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder
                    .AddModelProvider(new FakeModelProvider("closed-model").RespondsWith("done"))
                    .AddAgent(new AgentDefinition
                    {
                        Name = AgentName,
                        Instructions = "Give a short answer.",
                        Model = new ModelBinding { Provider = "closed-model", Model = "closed-1" },
                    });

                builder.UseScheduling(options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(20);
                    options.RunWorker = runWorker;
                });
            },
            configureServices: services =>
            {
                services.Configure<TraconAsyncRunOptions>(options => options.MaxAttempts = 1);
                configureServices?.Invoke(services);
            });

    private static async Task<Guid> QueueAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run)
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "start" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var accepted = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        return (await TraconTestHost.ReadJsonAsync(accepted)).GetProperty("runId").GetGuid();
    }

    private sealed class ThrowingScope : IHostTenantScope
    {
        public IDisposable? Begin(string tenantId) => throw new InvalidOperationException("the tenant is unknown to the host");
    }
}
