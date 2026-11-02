using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A queued run whose agent could not be resolved because the store did not answer is run again, like any other failure that
/// is not final.
/// </summary>
/// <remarks>
/// <para>
/// The agent catalog used to wrap what a source threw as "the agent source failed"; a store failure now reaches the callers
/// of the catalog as it is. The job worker decides whether another attempt can help from the stable identity of the failure
/// (<c>FinalFailure</c>), not from its type, so the raw exception must keep the retries the wrapped one had: a store that
/// answers on the next attempt must not cost the caller a failed run.
/// </para>
/// <para>
/// The same exceptions are what the synchronous run, the OpenAI-compatible routes, MCP and A2A resolve through the catalog:
/// the first two answer 503 (see <c>StoreFailureMatrixTests</c>); the last two hand them to their protocol libraries.
/// </para>
/// </remarks>
public sealed class CatalogStoreFailureJobRetryTests
{
    private const string AgentName = "queued-agent";

    /// <summary>
    /// The reads of the tenant's provider binding the 202 itself makes while it resolves the agent (the request is
    /// refused before it is queued when the agent cannot be built). The failure is meant for the job's own attempts, so
    /// these pass.
    /// </summary>
    private const int EnqueueReads = 2;

    [Fact]
    public async Task A_queued_run_whose_agent_could_not_be_resolved_for_one_attempt_is_run_again_and_completes()
    {
        var failures = new CountingFault(afterCalls: EnqueueReads, times: 1);
        await using var host = await StartAsync(failures);

        var runId = await QueueRunAsync(host);
        var job = await WaitForEndAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Completed);
        job.Attempt.ShouldBe(2, "the first attempt met a store that did not answer; the second one is the retry");
    }

    [Fact]
    public async Task A_queued_run_whose_store_never_answers_uses_every_attempt_and_ends_failed()
    {
        var failures = new CountingFault(afterCalls: EnqueueReads, times: int.MaxValue);
        await using var host = await StartAsync(failures);

        var runId = await QueueRunAsync(host);
        var job = await WaitForEndAsync(host, runId);

        job.Status.ShouldBe(JobStatus.Failed);
        job.Attempt.ShouldBe(3, "a store that did not answer is not a failure the next attempt is certain to repeat");
        (await host.Services.GetRequiredService<IRunStore>().GetRunAsync(runId))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.Failed);
    }

    private static Task<TraconTestHost> StartAsync(CountingFault fault)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddAgent(TestData.Definition(AgentName));
                builder.UseScheduling(static options => options.PollInterval = TimeSpan.FromMilliseconds(20));
                builder.Services.ThrowFrom<ITenantProviderBindingStore>(
                    "GetAsync",
                    static () => new InjectedDbException(),
                    fault.Times,
                    fault.Counts);
            },
            configureServices: static services => services.Configure<TraconAsyncRunOptions>(static options => options.MaxAttempts = 3));

    private static async Task<Guid> QueueRunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello" }),
        };
        request.Headers.Add("Prefer", "respond-async");

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        return (await TraconTestHost.ReadJsonAsync(response)).GetProperty("runId").GetGuid();
    }

    private static async Task<JobRecord> WaitForEndAsync(TraconTestHost host, Guid jobId)
    {
        var jobs = host.Services.GetRequiredService<IJobStore>();
        var tenantId = host.Services.GetRequiredService<ITenantContext>().TenantId;

        var job = await WaitUntil.ValueAsync(
            () => jobs.GetAsync(tenantId, jobId).AsTask(),
            static job => job?.Status is JobStatus.Completed or JobStatus.Failed,
            "the job to end");

        return job!;
    }

    /// <summary>Lets the first <c>afterCalls</c> calls through, then throws for <c>times</c> calls.</summary>
    private sealed class CountingFault(int afterCalls, int times)
    {
        private int _calls;

        public int Times { get; } = times;

        public bool Counts(object?[] arguments) => Interlocked.Increment(ref _calls) > afterCalls;
    }

    private sealed class InjectedDbException : DbException
    {
        public InjectedDbException()
            : base("injected")
        {
        }
    }
}
