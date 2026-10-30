using System.Net;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.QueuedWorkflowHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure modes 7 and 8: <c>Idempotency-Key</c> on the workflow run endpoints. A repeated
/// queued request returns the same <c>202</c> and queues nothing; a key without
/// <c>Prefer: respond-async</c> is refused with <c>400</c> (option A).
/// </summary>
/// <remarks>
/// The filter stores the response it captures in memory. On these endpoints the
/// synchronous response is an SSE stream: capturing it would hold a whole workflow run
/// in a buffer and replay a frozen copy. The queued <c>202</c> is small and replayable.
/// </remarks>
public sealed class WorkflowAsyncIdempotencyTests
{
    [Fact]
    public async Task The_same_key_sent_twice_returns_the_same_run_and_queues_one_job()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var body = new WorkflowRunHttpRequest { Message = "episode 7" };

        using var first = await PostAsync(host, $"/api/workflows/{Recoverable}/run", body, idempotencyKey: "production-7");
        using var second = await PostAsync(host, $"/api/workflows/{Recoverable}/run", body, idempotencyKey: "production-7");

        var firstId = await AcceptedRunIdAsync(first);
        var secondId = await AcceptedRunIdAsync(second);

        secondId.ShouldBe(firstId);
        second.Headers.GetValues("Idempotency-Replayed").ShouldHaveSingleItem().ShouldBe("true");
        second.Headers.Location!.ToString().ShouldBe($"/tracon/api/runs/{firstId}");
        (await JobsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(firstId);
        (await WorkflowRunsAsync(host)).ShouldHaveSingleItem();
    }

    /// <summary>Failure mode 7: concurrent requests with one key open one job, whatever the timing.</summary>
    [Fact]
    public async Task Concurrent_requests_with_one_key_open_exactly_one_job()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe, runWorker: false);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        var body = new WorkflowRunHttpRequest { Message = "episode 7" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(
            _ => PostAsync(host, $"/api/workflows/{Recoverable}/run", body, idempotencyKey: "production-7")));

        try
        {
            // Each one is the 202 (first or replayed) or a 409 for a request still in flight.
            responses.ShouldAllBe(static response =>
                response.StatusCode == HttpStatusCode.Accepted || response.StatusCode == HttpStatusCode.Conflict);

            var runIds = new HashSet<Guid>();

            foreach (var response in responses.Where(static response => response.StatusCode == HttpStatusCode.Accepted))
            {
                runIds.Add(await AcceptedRunIdAsync(response));
            }

            runIds.ShouldHaveSingleItem();
            (await JobsAsync(host)).ShouldHaveSingleItem().Id.ShouldBe(runIds.Single());
            (await WorkflowRunsAsync(host)).ShouldHaveSingleItem();
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    /// <summary>Failure mode 8: without the preference the answer is a stream; the key is refused before anything runs.</summary>
    [Fact]
    public async Task A_key_without_respond_async_is_refused_with_400_on_all_three_endpoints_and_nothing_runs()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;
        await SaveRecoverableAsync(host);

        using (var run = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" }, idempotencyKey: "k-run", preferAsync: false))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            string.Equals(run.Content.Headers.ContentType!.MediaType, "text/event-stream", StringComparison.Ordinal).ShouldBeFalse();
            (await TraconTestHost.ReadJsonAsync(run)).GetProperty("title").GetString()
                .ShouldBe("Idempotency-Key requires Prefer: respond-async");
        }

        var sourceId = Guid.NewGuid();

        using (var resume = await PostAsync(host, $"/api/workflows/runs/{sourceId}/resume", new WorkflowResumeHttpRequest(), idempotencyKey: "k-resume", preferAsync: false))
        {
            resume.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var respond = await PostAsync(host, $"/api/workflows/runs/{sourceId}/respond", new { requestId = "r", approved = true }, idempotencyKey: "k-respond", preferAsync: false))
        {
            respond.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        probe.FirstCalls.ShouldBe(0);
        (await JobsAsync(host)).ShouldBeEmpty();
        (await WorkflowRunsAsync(host)).ShouldBeEmpty();

        // The key was never reserved: the same key with the preference is accepted.
        using var queued = await PostAsync(host, $"/api/workflows/{Recoverable}/run", new WorkflowRunHttpRequest { Message = "episode 7" }, idempotencyKey: "k-run");
        queued.StatusCode.ShouldBe(HttpStatusCode.Accepted, await queued.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_queued_response_repeated_with_its_key_opens_one_run()
    {
        var probe = new RecoveryProbe();
        await using var host = await StartAsync(probe);
        await using var lifetime = probe;

        using var accepted = await PostAsync(host, $"/api/workflows/{Approval}/run", new WorkflowRunHttpRequest { Message = "the draft" });
        var waitingId = await AcceptedRunIdAsync(accepted);
        await host.WaitForRunStatusAsync(waitingId, nameof(RunStatus.AwaitingInput));

        using var listed = await host.Client.GetAsync($"/tracon/api/workflows/runs/{waitingId}/requests");
        var requestId = (await TraconTestHost.ReadJsonAsync(listed))[0].GetProperty("requestId").GetString()!;

        var answer = new { requestId, approved = true };

        using var first = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", answer, idempotencyKey: "render-done-7");
        using var second = await PostAsync(host, $"/api/workflows/runs/{waitingId}/respond", answer, idempotencyKey: "render-done-7");

        var answerId = await AcceptedRunIdAsync(first);
        (await AcceptedRunIdAsync(second)).ShouldBe(answerId);

        await host.WaitForRunStatusAsync(answerId, nameof(RunStatus.Completed));

        (await WorkflowRunsAsync(host)).Count.ShouldBe(2, "the waiting run and ONE answer");
    }
}
