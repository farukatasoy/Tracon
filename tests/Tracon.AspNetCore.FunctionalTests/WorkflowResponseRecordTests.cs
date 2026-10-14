using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.WorkflowResponseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// What an answer to a workflow request leaves behind (phase 208): a <c>workflow.respond</c>
/// audit entry, a record readable from <c>GET …/responses</c>, and a
/// <see cref="RunEventType.WorkflowResponseApplied"/> event on the run it starts - none of them
/// carrying the answer itself.
/// </summary>
public sealed class WorkflowResponseRecordTests
{
    private const string Marker = "do-not-copy-this-answer";

    [Fact]
    public async Task The_audit_entry_names_the_actor_the_runs_and_a_hash_and_never_the_answer()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: static services =>
            services.AddSingleton<IAuditActorResolver>(new FixedActor("editor@example")));
        var (sourceId, requestId) = await RunToAwaitAsync(host, NoteFlow);

        using var answered = await RespondAsync(host, sourceId, new { requestId, text = $"ship it - {Marker}" });
        var runId = RunIdOf(await FramesAsync(answered));

        var entry = (await RespondEntriesAsync(host)).ShouldHaveSingleItem();
        var after = JsonDocument.Parse(entry.After!).RootElement;

        entry.Entity.ShouldBe($"workflow:{NoteFlow}");
        entry.Before.ShouldBeNull();
        entry.Actor.ShouldBe("editor@example");
        after.GetProperty("sourceRunId").GetGuid().ShouldBe(sourceId);
        after.GetProperty("requestId").GetString().ShouldBe(requestId);
        after.GetProperty("responseRunId").GetGuid().ShouldBe(runId);
        after.GetProperty("mode").GetString().ShouldBe("Branch");
        after.GetProperty("redrive").GetBoolean().ShouldBeFalse();
        after.GetProperty("answerHash").GetString().ShouldNotBeNull().ShouldMatch("^[0-9a-f]{64}$");
        entry.After!.ShouldNotContain(Marker, Case.Sensitive);

        var record = (await RecordsAsync(host, sourceId)).ShouldHaveSingleItem();

        record.AnswerHash.ShouldBe(after.GetProperty("answerHash").GetString());
        record.RespondedBy.ShouldBe("editor@example");
    }

    [Fact]
    public async Task A_large_answer_leaves_an_entry_of_the_same_small_size()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (smallSource, smallRequest) = await RunToAwaitAsync(host, NoteFlow);
        var (largeSource, largeRequest) = await RunToAwaitAsync(host, NoteFlow);

        using (var small = await RespondAsync(host, smallSource, new { requestId = smallRequest, text = "ok" }))
        {
            await FramesAsync(small);
        }

        using (var large = await RespondAsync(host, largeSource, new { requestId = largeRequest, text = new string('x', 64 * 1024) }))
        {
            await FramesAsync(large);
        }

        var sizes = (await RespondEntriesAsync(host)).Select(static entry => entry.After!.Length).ToList();

        sizes.Count.ShouldBe(2);
        sizes.Max().ShouldBeLessThan(400);
        (sizes.Max() - sizes.Min()).ShouldBeLessThan(8, "only the request id's length may differ between the two entries");
    }

    [Fact]
    public async Task An_answer_of_the_wrong_shape_is_refused_before_it_is_audited_or_recorded()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        // The port takes a yes/no; the existing 400 (K-908) comes first.
        using var refused = await RespondAsync(host, sourceId, new { requestId, text = "yes" });

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RespondEntriesAsync(host)).ShouldBeEmpty();
        (await RecordsAsync(host, sourceId)).ShouldBeEmpty();

        // The request is still free for a well-formed answer.
        using var answered = await RespondAsync(host, sourceId, new { requestId, approved = true });
        await FramesAsync(answered);
    }

    [Fact]
    public async Task Another_tenant_can_neither_answer_a_run_nor_read_its_answers()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(tenancy: true);
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow, tenant: "tenant-alpha");

        using var answered = await RespondAsync(host, sourceId, new { requestId, approved = true }, tenant: "tenant-alpha");
        await FramesAsync(answered);

        using var foreignAnswer = await RespondAsync(host, sourceId, new { requestId, approved = false }, tenant: "tenant-beta");

        foreignAnswer.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var foreignRead = await GetResponsesAsync(host, sourceId, "tenant-beta");

        foreignRead.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var ownRead = await GetResponsesAsync(host, sourceId, "tenant-alpha");

        ownRead.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(ownRead)).GetArrayLength().ShouldBe(1);
        (await RespondEntriesAsync(host, "tenant-beta")).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_responses_endpoint_lists_every_answer_oldest_first_without_the_tenant()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync();
        var (sourceId, requestId) = await RunToAwaitAsync(host, BranchFlow);

        using (var first = await RespondAsync(host, sourceId, new { requestId, approved = true }))
        {
            await FramesAsync(first);
        }

        using (var second = await RespondAsync(host, sourceId, new { requestId, approved = false }))
        {
            await FramesAsync(second);
        }

        using var listed = await GetResponsesAsync(host, sourceId, tenant: null);
        var items = (await TraconTestHost.ReadJsonAsync(listed)).EnumerateArray().ToList();

        items.Count.ShouldBe(2);
        items[0].GetProperty("respondedAt").GetDateTimeOffset()
            .ShouldBeLessThanOrEqualTo(items[1].GetProperty("respondedAt").GetDateTimeOffset());
        items.ShouldAllBe(item => item.GetProperty("requestId").GetString() == requestId);
        items.ShouldAllBe(static item => item.GetProperty("status").GetString() == "Delivered");
        items.ShouldAllBe(static item => item.GetProperty("claimScope").ValueKind == JsonValueKind.Null);
        items.Count(static item => item.TryGetProperty("tenantId", out _)).ShouldBe(0);
    }

    [Fact]
    public async Task An_answer_store_that_cannot_write_refuses_the_answer_and_closes_the_run_it_opened()
    {
        // The run row is opened before the record (a record never names a missing run), so a
        // record that cannot be written leaves that row: it is closed Failed, never started.
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: static services =>
            services.AddSingleton<IWorkflowResponseStore, BrokenResponseStore>());
        var (sourceId, requestId) = await RunToAwaitAsync(host, BranchFlow);
        var before = (await WorkflowRunsAsync(host)).Select(static run => run.Id).ToHashSet();

        // No exception handler in the test host: the refusal surfaces as the exception a real
        // host answers with 500 (the FailClosedAuditTests convention).
        await Should.ThrowAsync<TraconException>(async () =>
        {
            using var _ = await RespondAsync(host, sourceId, new { requestId, approved = true });
        });

        var opened = (await WorkflowRunsAsync(host)).Where(run => !before.Contains(run.Id)).ToList().ShouldHaveSingleItem();

        opened.Status.ShouldBe(RunStatus.Failed);
        opened.Error.ShouldNotBeNull().Type.ShouldBe("answer_not_recorded");

        var events = await host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(opened.Id, 0).ToListAsync();

        events.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_run_an_answer_starts_names_its_source_even_with_payload_recording_off()
    {
        var harness = new WorkflowResponseHarness();
        await using var host = await harness.StartAsync(configureServices: static services =>
            services.Configure<TraconOptions>(static options => options.RunRecording.RecordToolPayloads = false));
        var (sourceId, requestId) = await RunToAwaitAsync(host, SingleFlow);

        using var answered = await RespondAsync(host, sourceId, new { requestId, approved = true });
        var frames = await FramesAsync(answered);
        var runId = RunIdOf(frames);

        // Live: the second event of the stream.
        var live = JsonDocument.Parse(frames[2].Data).RootElement;

        live.GetProperty("type").GetString().ShouldBe(nameof(RunEventType.WorkflowResponseApplied));
        live.GetProperty("text").GetString().ShouldBe(requestId);

        // Stored: the same link, read back from the run's history.
        var stored = await host.Services.GetRequiredService<IRunStore>().ReadEventsAsync(runId, 0).ToListAsync();
        var link = stored.Single(static runEvent => runEvent.Type == RunEventType.WorkflowResponseApplied);
        var payload = JsonDocument.Parse(link.Payload.ShouldNotBeNull()).RootElement;

        link.Sequence.ShouldBe(1);
        payload.GetProperty("sourceRunId").GetGuid().ShouldBe(sourceId);
        payload.GetProperty("requestId").GetString().ShouldBe(requestId);
        link.Payload.ShouldNotContain("approved", Case.Insensitive);

        // The run event stream names the frame.
        using var history = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative));
        var historyFrames = await SseReader.ReadAllAsync(await history.Content.ReadAsStreamAsync());

        historyFrames.ShouldContain(static frame => frame.Event == "workflow.response-applied");
    }

    private static async Task<HttpResponseMessage> GetResponsesAsync(TraconTestHost host, Guid sourceId, string? tenant)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/tracon/api/workflows/runs/{sourceId}/responses");

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return await host.Client.SendAsync(request);
    }

    /// <summary>An actor resolver that names one actor.</summary>
    private sealed class FixedActor(string actor) : IAuditActorResolver
    {
        public string? Resolve() => actor;
    }

    /// <summary>An answer store whose every write fails.</summary>
    private sealed class BrokenResponseStore : IWorkflowResponseStore
    {
        public ValueTask<WorkflowResponseClaim> AddAsync(WorkflowResponseRecord record, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the answer store is unreachable");

        public ValueTask<WorkflowResponseRecord?> FindClaimAsync(string claimScope, string requestId, CancellationToken cancellationToken = default)
            => new((WorkflowResponseRecord?)null);

        public ValueTask<bool> ReplaceAsync(Guid abandonedResponseRunId, WorkflowResponseRecord replacement, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the answer store is unreachable");

        public ValueTask MarkDeliveredAsync(Guid responseRunId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("the answer store is unreachable");

        public ValueTask<IReadOnlyList<WorkflowResponseRecord>> ListBySourceRunAsync(Guid sourceRunId, CancellationToken cancellationToken = default)
            => new((IReadOnlyList<WorkflowResponseRecord>)[]);

        public ValueTask<WorkflowResponseRecord?> GetAsync(Guid responseRunId, CancellationToken cancellationToken = default)
            => new((WorkflowResponseRecord?)null);
    }
}
