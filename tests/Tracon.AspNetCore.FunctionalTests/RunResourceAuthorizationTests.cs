using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
// Same rationale as ApprovalEndpointTests.cs (K-269): the package's
// Tracon.Testing.TraconTestHost and this project's own TraconTestHost
// share the same name; a blanket `using Tracon.Testing;` would produce CS0104.
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 147 (F-195) over HTTP: the installation's own
/// <see cref="IRunAuthorizationHandler"/> now gates a run's RESOURCES — its
/// summary, tree, events, input, trace, tool calls, scores, cancellation, its
/// attachments and its approvals — and the two run-starting surfaces phase 139
/// missed (<c>POST /api/runs/{id}/replay</c> and <c>/v1/chat/completions</c>).
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Every endpoint gets its OWN test rather than one parametrized sweep, the
/// same rationale <see cref="RunAuthorizationEndpointTests"/> documents: the
/// gate is not an endpoint filter, so an endpoint that forgot the call looks
/// identical to every other test passing. Only a test that exercises THAT
/// endpoint catches the gap.
/// </para>
/// <para>
/// The central claim is not "a denial is refused" but "a denial is
/// INDISTINGUISHABLE from absence": a denied single resource returns the same
/// 404 body a genuinely missing one does, because a 403 there would confirm
/// the resource exists (K-671). Lists are the exception — a list has no single
/// identity to hide, so a denial is a plain 403.
/// </para>
/// </remarks>
public sealed class RunResourceAuthorizationTests
{
    private const string AgentName = "kod-agent";
    private const string ModelId = "echo-1";

    private static readonly Guid Missing = new("00000000-0000-0000-0000-0000000000ff");

    // --- DoD 1: nothing registered, nothing changes ---

    [Fact]
    public async Task Every_resource_endpoint_is_unchanged_when_no_handler_is_registered()
    {
        await using var host = await StartAsync(handler: null);
        var runId = await SeedRunAsync(host);
        var attachmentId = await SeedAttachmentAsync(host);
        var approvalId = await SeedApprovalAsync(host, runId);

        // Exactly the statuses this API answered before phase 147 existed.
        await AssertStatusAsync(host, HttpMethod.Get, "/tracon/api/runs", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/tree", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/events", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/input", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/trace", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/tools", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/feedback", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/compare/{runId}", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, "/tracon/api/attachments", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/attachments/{attachmentId}", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, "/tracon/api/approvals/pending", HttpStatusCode.OK);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/approvals/{approvalId}", HttpStatusCode.OK);

        // A completed run cannot be canceled: 409, not 404 - proof the gate did
        // not short-circuit ahead of the status check.
        await AssertStatusAsync(host, HttpMethod.Post, $"/tracon/api/runs/{runId}/cancel", HttpStatusCode.Conflict);

        using var feedback = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/feedback", UriKind.Relative),
            new RunFeedbackRequest { Kind = RunScoreKind.Binary, Value = 1 });
        feedback.StatusCode.ShouldBe(HttpStatusCode.OK);

        await AssertStatusAsync(host, HttpMethod.Delete, $"/tracon/api/attachments/{attachmentId}", HttpStatusCode.NoContent);

        // 🚨 The two run-STARTING endpoints phase 147 added a gate to, and the
        // two writes with a second identity in the route. A gate that goes into
        // a path which never had one before is exactly where a null-handler
        // regression would land, so each is exercised here as well.
        using (var replayed = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/replay", UriKind.Relative), new { toolMode = "NoTools" }))
        {
            replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var completion = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = AgentName, messages = new[] { new { role = "user", content = "hello" } } }))
        {
            completion.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var uploaded = await UploadAsync(host))
        {
            uploaded.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var score = (await TraconTestHost.ReadJsonAsync(
            await host.Client.PostAsJsonAsync(
                new Uri($"/tracon/api/runs/{runId}/feedback", UriKind.Relative),
                new RunFeedbackRequest { Kind = RunScoreKind.Binary, Value = 0 })))
            .GetProperty("id").GetGuid();

        await AssertStatusAsync(
            host, HttpMethod.Delete, $"/tracon/api/runs/{runId}/feedback/{score}", HttpStatusCode.NoContent);
    }

    // --- Run reads: a denial is the SAME 404 a missing run gets ---

    [Fact]
    public async Task Denied_run_read_is_indistinguishable_from_a_missing_run()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        await AssertLooksMissingAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", runId);
    }

    [Fact]
    public async Task Denied_run_tree_returns_404()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        await AssertLooksMissingAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/tree", runId);
    }

    [Fact]
    public async Task Denied_run_events_returns_404_and_the_stream_never_opens()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/events", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 🚨 The denial arrived as a STATUS CODE, not as a stream that opened
        // and then stopped: once an SSE response has started the status can no
        // longer be changed, so a gate placed after the stream began would be
        // useless.
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/event-stream", StringComparer.Ordinal);
    }

    [Fact]
    public async Task Denied_run_input_returns_404()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        await AssertLooksMissingAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/input", runId);
    }

    [Fact]
    public async Task Denied_run_trace_returns_404()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        // The genuinely-missing counterpart here is a run with no recorded
        // spans, which answers with the SAME "Trace not found" body.
        using var denied = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{runId}/trace", UriKind.Relative));

        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadBodyAsync(denied, runId)).ShouldBe(await MissingTraceBodyAsync(host));
    }

    [Fact]
    public async Task Denied_run_tool_invocations_returns_404()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        await AssertLooksMissingAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/tools", runId);
    }

    [Fact]
    public async Task Denied_run_feedback_read_returns_404()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        await AssertLooksMissingAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/feedback", runId);
    }

    // --- Lists: a denial is a plain 403, never a filtered list ---

    [Fact]
    public async Task Denied_run_list_returns_403_and_is_never_filtered()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;

        handler.OnRun = Deny(RunAccess.Read);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The handler saw a LIST request: no run id, so nothing to hide.
        handler.RunRequests.ShouldContain(static r => r.Access == RunAccess.Read && r.RunId == null);
    }

    [Fact]
    public async Task Denied_attachment_list_returns_403()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;

        handler.OnRun = Deny(RunAccess.Attachment);

        await AssertStatusAsync(host, HttpMethod.Get, "/tracon/api/attachments", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Denied_approval_list_returns_403()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;

        handler.OnRun = Deny(RunAccess.Approval);

        await AssertStatusAsync(host, HttpMethod.Get, "/tracon/api/approvals/pending", HttpStatusCode.Forbidden);
    }

    // --- Compare: BOTH sides pass the gate ---

    [Fact]
    public async Task Compare_asks_the_gate_for_each_side_and_either_denial_refuses_it()
    {
        var (host, handler, left) = await StartWithRunAsync();
        await using var _ = host;
        var right = await SeedRunAsync(host);

        handler.OnRun = request => request.RunId == right
            ? RunAuthorizationResult.Deny("no")
            : RunAuthorizationResult.Allow();

        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{left}/compare/{right}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Both ids reached the handler: asking only once would let a caller
        // read a run they may not see by pairing it with one they may.
        handler.RunRequests.ShouldContain(r => r.RunId == left);
        handler.RunRequests.ShouldContain(r => r.RunId == right);
    }

    // --- Cancel and feedback writes ---

    [Fact]
    public async Task Denied_cancel_returns_404_before_the_status_is_read()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Cancel);

        using var response = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/cancel", UriKind.Relative), content: null);

        // 🚨 404, NOT the 409 this completed run would otherwise get: a 409
        // would tell a denied caller the run exists and has already ended.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Denied_feedback_write_returns_404_and_writes_no_score()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Feedback);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/feedback", UriKind.Relative),
            new RunFeedbackRequest { Kind = RunScoreKind.Binary, Value = 1 });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var scores = host.Services.GetRequiredService<IRunScoreStore>();
        (await scores.ListAsync("default", runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Denied_feedback_delete_returns_404()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        var scores = host.Services.GetRequiredService<IRunScoreStore>();
        var saved = await scores.UpsertAsync(new RunScore
        {
            TenantId = "default",
            RunId = runId,
            Name = RunScoreRules.DefaultName,
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        handler.OnRun = Deny(RunAccess.Feedback);

        using var response = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/runs/{runId}/feedback/{saved.Id}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scores.ListAsync("default", runId)).ShouldHaveSingleItem();
    }

    // --- Attachments ---

    [Fact]
    public async Task Denied_attachment_upload_returns_403_and_stores_nothing()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;

        handler.OnRun = Deny(RunAccess.Attachment);

        using var response = await UploadAsync(host);

        // An upload addresses no existing resource, so there is nothing to
        // hide behind a 404.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var store = host.Services.GetRequiredService<IAttachmentStore>();
        (await store.ListAsync(new AttachmentQuery { TenantId = "default" })).ShouldBeEmpty();
    }

    [Fact]
    public async Task Denied_attachment_download_returns_404()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;
        var attachmentId = await SeedAttachmentAsync(host);

        handler.OnRun = Deny(RunAccess.Attachment);

        using var denied = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments/{attachmentId}", UriKind.Relative));
        using var missing = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments/{Missing}", UriKind.Relative));

        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadBodyAsync(denied, attachmentId)).ShouldBe(await ReadBodyAsync(missing, Missing));
    }

    [Fact]
    public async Task Denied_attachment_delete_returns_404_and_keeps_the_bytes()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;
        var attachmentId = await SeedAttachmentAsync(host);

        handler.OnRun = Deny(RunAccess.Attachment);

        using var response = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/attachments/{attachmentId}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var store = host.Services.GetRequiredService<IAttachmentStore>();
        (await store.GetAsync("default", attachmentId)).ShouldNotBeNull();
    }

    // --- Approvals ---

    [Fact]
    public async Task Denied_approval_read_returns_404()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;
        var approvalId = await SeedApprovalAsync(host, runId);

        handler.OnRun = Deny(RunAccess.Approval);

        using var denied = await host.Client.GetAsync(
            new Uri($"/tracon/api/approvals/{approvalId}", UriKind.Relative));
        using var missing = await host.Client.GetAsync(
            new Uri($"/tracon/api/approvals/{Missing}", UriKind.Relative));

        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadBodyAsync(denied, approvalId)).ShouldBe(await ReadBodyAsync(missing, Missing));
    }

    [Fact]
    public async Task Denied_approval_decision_returns_404_and_leaves_it_pending()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;
        var approvalId = await SeedApprovalAsync(host, runId);

        handler.OnRun = Deny(RunAccess.Approval);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/approvals/{approvalId}/decide", UriKind.Relative),
            new ApprovalDecisionRequest { Approved = true });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var approvals = host.Services.GetRequiredService<IPendingApprovalStore>();
        (await approvals.GetAsync(approvalId))!.Status.ShouldBe(ApprovalStatus.Pending);
    }

    // --- The two run-starting surfaces phase 139 missed ---

    [Fact]
    public async Task Denied_replay_returns_403_and_opens_no_run_row()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        var runs = host.Services.GetRequiredService<IRunStore>();
        var before = (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count;

        handler.OnRun = Deny(RunAccess.Start);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/replay", UriKind.Relative),
            new { toolMode = "NoTools" });

        // A replay STARTS a run, so its denial is a 403 like every other
        // run-starting surface - not the 404 a resource read gets.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count.ShouldBe(before);

        // 🚨 The SOURCE run's id reached the handler. Without it a handler
        // could not tell "start a run of this agent" (which it may allow) from
        // "replay somebody else's recorded conversation" (which it may not).
        handler.RunRequests.Single(static r => r.Access == RunAccess.Start).RunId.ShouldBe(runId);
    }

    [Fact]
    public async Task Denied_replay_does_not_consume_the_quota()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        using (var created = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/quotas", UriKind.Relative),
            new QuotaSaveRequest { AgentName = AgentName, Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        handler.OnRun = Deny(RunAccess.Start);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/replay", UriKind.Relative),
            new { toolMode = "NoTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var enforcer = host.Services.GetRequiredService<QuotaEnforcer>();
        (await enforcer.CheckAsync("default", AgentName)).IsAllowed.ShouldBeTrue();
    }

    [Fact]
    public async Task Denied_chat_completions_returns_403_on_the_non_streaming_path()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;

        handler.OnRun = Deny(RunAccess.Start);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = AgentName, messages = new[] { new { role = "user", content = "hello" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        handler.RunRequests.ShouldContain(static r => r.Access == RunAccess.Start && string.Equals(r.AgentName, AgentName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Denied_chat_completions_returns_403_on_the_streaming_path()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;

        handler.OnRun = Deny(RunAccess.Start);

        // 🚨 The streaming branch is a SEPARATE code path that returns an
        // IResult of its own; a gate placed inside the non-streaming branch
        // would leave this one wide open.
        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/v1/chat/completions", UriKind.Relative),
            new { model = AgentName, stream = true, messages = new[] { new { role = "user", content = "hello" } } });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/event-stream", StringComparer.Ordinal);
    }

    // --- The surfaces the phase's own audit found still ungated ---

    [Fact]
    public async Task Feedback_delete_refuses_a_score_that_belongs_to_a_different_run()
    {
        var (host, handler, ownRun) = await StartWithRunAsync();
        await using var _ = host;
        var otherRun = await SeedRunAsync(host);

        var scores = host.Services.GetRequiredService<IRunScoreStore>();
        var othersScore = await scores.UpsertAsync(new RunScore
        {
            TenantId = "default",
            RunId = otherRun,
            Name = RunScoreRules.DefaultName,
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        // The caller may score their OWN run and is refused on the other one.
        handler.OnRun = request => request.RunId == otherRun
            ? RunAuthorizationResult.Deny("no")
            : RunAuthorizationResult.Allow();

        // 🚨 IRunScoreStore.DeleteAsync takes no run id, so the gate's answer
        // (about ownRun) and the store's action (on the other run's score) are
        // two different identities. Naming a run you may score and a score you
        // may not delete has to be refused, or the gate is decorative here.
        using var response = await host.Client.DeleteAsync(
            new Uri($"/tracon/api/runs/{ownRun}/feedback/{othersScore.Id}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await scores.ListAsync("default", otherRun)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Denied_judge_returns_404_and_writes_no_score()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = Deny(RunAccess.Read);

        // Judging reads the run AND writes a score for it, so it passes the
        // gate twice; either refusal refuses the whole call.
        using var response = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/judge", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var scores = host.Services.GetRequiredService<IRunScoreStore>();
        (await scores.ListAsync("default", runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Denied_judge_is_refused_on_the_feedback_half_too()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        // Reading is allowed; only the WRITE is refused. Asking for Read alone
        // would let this route write a score the feedback endpoint refuses.
        handler.OnRun = Deny(RunAccess.Feedback);

        using var response = await host.Client.PostAsync(
            new Uri($"/tracon/api/runs/{runId}/judge", UriKind.Relative), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Denied_workflow_resume_returns_403_and_opens_no_run_row()
    {
        var handler = new ConfigurableRunAuthorizationHandler();
        await using var host = await StartWorkflowAsync(handler);
        var runId = await RunWorkflowAsync(host);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var before = (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count;

        // 🚨 Resume opens a NEW run row from an existing run's checkpoint, so
        // it is a run-STARTING surface — phase 139 counted four and phase 147's
        // own audit found this one still outside the gate.
        handler.OnRun = Deny(RunAccess.Start);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/workflows/runs/{runId}/resume", UriKind.Relative),
            new WorkflowResumeHttpRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await runs.QueryRunsAsync(new RunQuery { OnlyRootRuns = false })).Count.ShouldBe(before);

        // The SOURCE run's id reached the handler. The workflow's own first run
        // also asked with Start (through CheckRunAsync, carrying no run id), so
        // this looks for the resume's request rather than the only one.
        handler.RunRequests.ShouldContain(r => r.Access == RunAccess.Start && r.RunId == runId);
    }

    [Fact]
    public async Task Denied_workflow_respond_returns_403()
    {
        var handler = new ConfigurableRunAuthorizationHandler();
        await using var host = await StartWorkflowAsync(handler);
        var runId = await RunWorkflowAsync(host);

        handler.OnRun = Deny(RunAccess.Start);

        using var response = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/workflows/runs/{runId}/respond", UriKind.Relative),
            new { requestId = "request-1" });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Denied_workflow_checkpoints_and_requests_return_404()
    {
        var handler = new ConfigurableRunAuthorizationHandler();
        await using var host = await StartWorkflowAsync(handler);
        var runId = await RunWorkflowAsync(host);

        handler.OnRun = Deny(RunAccess.Read);

        // A checkpoint carries the workflow's own state; reading the list is
        // reading the run.
        await AssertStatusAsync(
            host, HttpMethod.Get, $"/tracon/api/workflows/runs/{runId}/checkpoints", HttpStatusCode.NotFound);
        await AssertStatusAsync(
            host, HttpMethod.Get, $"/tracon/api/workflows/runs/{runId}/requests", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Workflow_continuation_is_unchanged_when_no_handler_is_registered()
    {
        await using var host = await StartWorkflowAsync(handler: null);
        var runId = await RunWorkflowAsync(host);

        await AssertStatusAsync(
            host, HttpMethod.Get, $"/tracon/api/workflows/runs/{runId}/checkpoints", HttpStatusCode.OK);
        await AssertStatusAsync(
            host, HttpMethod.Get, $"/tracon/api/workflows/runs/{runId}/requests", HttpStatusCode.OK);

        using var resumed = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/workflows/runs/{runId}/resume", UriKind.Relative),
            new WorkflowResumeHttpRequest());

        resumed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // --- Fail-closed, ordering, and load ---

    [Fact]
    public async Task A_failing_run_store_does_not_turn_into_an_allow()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        // A subsystem failure must not become an ALLOW. The store is not
        // replaceable on a live host, so the closest reachable equivalent is
        // asserted: the gate never sees a run it could not read, and the
        // request ends as "not found" rather than as an authorized read.
        handler.RunRequests.Clear();

        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{Missing}/input", HttpStatusCode.NotFound);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{Missing}/trace", HttpStatusCode.NotFound);

        handler.RunRequests.ShouldBeEmpty();

        // The real run still reads, so the assertion above is about the missing
        // one, not about a host that stopped answering altogether.
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", HttpStatusCode.OK);
    }

    [Fact]
    public async Task Throwing_handler_denies_every_resource_fail_closed()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;
        var attachmentId = await SeedAttachmentAsync(host);

        handler.OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down");

        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", HttpStatusCode.NotFound);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/tools", HttpStatusCode.NotFound);
        await AssertStatusAsync(host, HttpMethod.Get, "/tracon/api/runs", HttpStatusCode.Forbidden);
        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/attachments/{attachmentId}", HttpStatusCode.NotFound);
        await AssertStatusAsync(host, HttpMethod.Get, "/tracon/api/approvals/pending", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Throwing_handler_on_a_single_run_keeps_the_missing_run_body_and_is_logged()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down");

        // 🚨 The identity-hiding 404 does not change for a FAILED check either:
        // a body that said "the check failed" would confirm the run exists.
        await AssertLooksMissingAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", runId);

        var error = GateErrors(host).ShouldHaveSingleItem();
        error.ShouldContain(nameof(ConfigurableRunAuthorizationHandler));
        error.ShouldContain(nameof(RunAccess.Read));
        error.ShouldContain(runId.ToString());
        error.ShouldContain("policy store is down");
    }

    [Fact]
    public async Task Throwing_handler_on_a_403_surface_reports_a_failed_check_not_a_denial()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down");

        // Every CheckRunResourceAsync surface that answers 403: the three
        // lists, and a run started FROM another run.
        using var runList = await host.Client.GetAsync(new Uri("/tracon/api/runs", UriKind.Relative));
        using var attachmentList = await host.Client.GetAsync(new Uri("/tracon/api/attachments", UriKind.Relative));
        using var approvalList = await host.Client.GetAsync(new Uri("/tracon/api/approvals/pending", UriKind.Relative));
        using var replay = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/runs/{runId}/replay", UriKind.Relative),
            new { toolMode = "NoTools" });

        foreach (var response in new[] { runList, attachmentList, approvalList, replay })
        {
            var uri = response.RequestMessage!.RequestUri!.ToString();

            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, uri);

            var body = await response.Content.ReadAsStringAsync();
            body.ShouldNotContain("policy store is down", customMessage: uri);

            var problem = JsonDocument.Parse(body).RootElement;
            problem.GetProperty("title").GetString().ShouldBe("Run not authorized", uri);
            problem.GetProperty("detail").GetString().ShouldBe(FailedCheckDetail, uri);
        }

        GateErrors(host).Count.ShouldBe(4);
    }

    [Fact]
    public async Task Throwing_handler_on_a_workflow_continuation_reports_a_failed_check()
    {
        var handler = new ConfigurableRunAuthorizationHandler();
        await using var host = await StartWorkflowAsync(handler);
        var runId = await RunWorkflowAsync(host);

        handler.OnRun = static _ => throw new InvalidOperationException("the consumer's own policy store is down");

        using var resumed = await host.Client.PostAsJsonAsync(
            new Uri($"/tracon/api/workflows/runs/{runId}/resume", UriKind.Relative),
            new WorkflowResumeHttpRequest());

        resumed.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await TraconTestHost.ReadJsonAsync(resumed)).GetProperty("detail").GetString().ShouldBe(FailedCheckDetail);
        GateErrors(host).ShouldHaveSingleItem().ShouldContain(runId.ToString());
    }

    [Fact]
    public async Task A_missing_run_is_answered_without_asking_the_handler()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;

        handler.RunRequests.Clear();

        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{Missing}", HttpStatusCode.NotFound);

        // 🚨 The gate is asked AFTER the resource is found and its tenant
        // settled, so another tenant's identity never reaches a consumer's
        // handler - and a handler is never asked about a run that is not there.
        handler.RunRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Another_tenants_run_never_reaches_the_handler()
    {
        var (host, handler, _) = await StartWithRunAsync();
        await using var __ = host;
        var foreign = await SeedRunAsync(host, tenantId: "tenant-b");

        handler.RunRequests.Clear();

        await AssertStatusAsync(host, HttpMethod.Get, $"/tracon/api/runs/{foreign}", HttpStatusCode.NotFound);

        handler.RunRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_reads_of_the_same_run_are_each_authorized()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        handler.RunRequests.Clear();

        var uri = new Uri($"/tracon/api/runs/{runId}", UriKind.Relative);
        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => host.Client.GetAsync(uri)));

        try
        {
            responses.ShouldAllBe(static r => r.StatusCode == HttpStatusCode.OK);
            handler.RunRequests.Count.ShouldBe(16);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_cancellation_the_request_did_not_cause_is_a_failed_check()
    {
        var (host, handler, runId) = await StartWithRunAsync();
        await using var _ = host;

        // 🚨 K-840. A handler that calls its policy store over HTTP surfaces
        // HttpClient.Timeout as TaskCanceledException while the REQUEST is
        // still alive. Letting that travel answered 500 for a run that exists
        // and 404 for one that does not - an existence oracle on exactly the
        // endpoints whose 404 exists to hide it.
        handler.OnRun = static _ => throw new TaskCanceledException("the policy store timed out");

        await AssertLooksMissingAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}", runId);

        GateErrors(host).ShouldHaveSingleItem().ShouldContain("the policy store timed out");
    }

    [Fact]
    public async Task A_request_the_caller_cancels_is_not_swallowed_into_a_denial()
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ConfigurableRunAuthorizationHandler();

        await using var host = await TraconTestHost.StartAsync(
            static builder => builder
                .AddModelProvider(new FakeModelProvider(ModelId).RespondsWith("ok"))
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = ModelId, Model = ModelId },
                }),
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler)),
            configureApp: app => app.Use(async (HttpContext context, RequestDelegate next) =>
            {
                try
                {
                    await next(context);
                }
                catch (OperationCanceledException)
                {
                    // The cancellation travelled out of the endpoint - the point of this test.
                }
                finally
                {
                    finished.TrySetResult();
                }
            }));

        var runId = await SeedRunAsync(host);

        // 🚨 A caller who walked away has not been DENIED: turning their own
        // cancellation into a 404 and an Error line would hide a real abort
        // behind something that reads like a failed policy check.
        handler.OnRunAsync = async (_, cancellationToken) =>
        {
            reached.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); // delay: simulated

            return RunAuthorizationResult.Allow();
        };

        using var cancel = new CancellationTokenSource();
        var request = host.Client.GetAsync(new Uri($"/tracon/api/runs/{runId}", UriKind.Relative), cancel.Token);

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(request);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Read only once the endpoint has fully unwound, so an Error line a
        // swallowing filter would write cannot land after this assertion.
        GateErrors(host).ShouldBeEmpty();
    }

    // --- Helpers ---

    /// <summary>The fixed detail a 403 carries when the handler threw instead of deciding.</summary>
    private const string FailedCheckDetail = "The run authorization check failed. Retry the request.";

    /// <summary>The Error lines the authorization gates wrote, in order.</summary>
    private static List<string> GateErrors(TraconTestHost host)
        => host.Logs.Entries
            .Where(static line => line.StartsWith("Error Tracon.RunAuthorization ", StringComparison.Ordinal))
            .ToList();

    private static Func<RunAuthorizationRequest, RunAuthorizationResult> Deny(RunAccess access)
        => request => request.Access == access
            ? RunAuthorizationResult.Deny("no")
            : RunAuthorizationResult.Allow();

    private static async Task AssertStatusAsync(
        TraconTestHost host,
        HttpMethod method,
        string uri,
        HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative));
        using var response = await host.Client.SendAsync(request);

        response.StatusCode.ShouldBe(expected, $"{method} {uri}");
    }

    /// <summary>
    /// Asserts that a denied resource answers with the byte-for-byte body a
    /// genuinely missing one does, with only the id itself differing.
    /// </summary>
    private static async Task AssertLooksMissingAsync(
        TraconTestHost host,
        HttpMethod method,
        string uri,
        Guid runId)
    {
        using var deniedRequest = new HttpRequestMessage(method, new Uri(uri, UriKind.Relative));
        using var denied = await host.Client.SendAsync(deniedRequest);

        using var missingRequest = new HttpRequestMessage(
            method,
            new Uri(uri.Replace(runId.ToString(), Missing.ToString(), StringComparison.Ordinal), UriKind.Relative));
        using var missing = await host.Client.SendAsync(missingRequest);

        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound, uri);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound, uri);

        (await ReadBodyAsync(denied, runId)).ShouldBe(await ReadBodyAsync(missing, Missing), uri);
    }

    /// <summary>Reads a problem body with the resource id blanked out, so two ids compare equal.</summary>
    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, Guid id)
        => (await response.Content.ReadAsStringAsync())
            .Replace(id.ToString(), "{id}", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> MissingTraceBodyAsync(TraconTestHost host)
    {
        using var response = await host.Client.GetAsync(
            new Uri($"/tracon/api/runs/{Missing}/trace", UriKind.Relative));

        return await ReadBodyAsync(response, Missing);
    }

    private static async Task<HttpResponseMessage> UploadAsync(TraconTestHost host)
    {
        using var content = new MultipartFormDataContent
        {
            { new ByteArrayContent(Png()), "file", "test.png" },
        };

        return await host.Client.PostAsync(new Uri("/tracon/api/attachments", UriKind.Relative), content);
    }

    /// <summary>
    /// Starts a host with an allow-everything handler and one fully seeded run,
    /// then hands the SAME handler back so the caller can flip its decision.
    /// </summary>
    private static async Task<(TraconTestHost Host, ConfigurableRunAuthorizationHandler Handler, Guid RunId)> StartWithRunAsync()
    {
        var handler = new ConfigurableRunAuthorizationHandler();
        var host = await StartAsync(handler);
        var runId = await SeedRunAsync(host);

        handler.RunRequests.Clear();

        return (host, handler, runId);
    }

    /// <summary>Writes one complete run: the row, its input, a tool call, and a span.</summary>
    /// <remarks>
    /// The agent definition is written to the definition store as well: a
    /// replay recompiles from a stored DEFINITION, and an agent that only
    /// exists in the catalog cannot be replayed at all (it would answer 400
    /// before the gate was ever asked).
    /// </remarks>
    private static async Task<Guid> SeedRunAsync(TraconTestHost host, string? tenantId = null)
    {
        await host.Services.GetRequiredService<IAgentDefinitionStore>().SaveAsync(new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Reply briefly.",
            Model = new ModelBinding { Provider = ModelId, Model = ModelId },
        });

        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var traces = host.Services.GetRequiredService<ITraceStore>();
        var tenant = tenantId ?? host.Services.GetRequiredService<ITenantContext>().TenantId;
        var runId = TraconId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
            ModelId = ModelId,
            SessionId = "session-under-test",
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = tenant,
            Messages = [new ChatMessage(ChatRole.User, "hello")],
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            TenantId = tenant,
            ToolName = "noop",
            Arguments = "{}",
            Result = "ok",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await traces.WriteSpansAsync(new TraceSpanBatch
        {
            TraceId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            TenantId = tenant,
            RunId = runId,
            Spans =
            [
                new TraceSpan
                {
                    Id = TraconId.NewId(),
                    SpanId = "0123456789abcdef",
                    Name = "run",
                    StartedAt = DateTimeOffset.UtcNow,
                    EndedAt = DateTimeOffset.UtcNow,
                },
            ],
        });

        return runId;
    }

    private static async Task<Guid> SeedAttachmentAsync(TraconTestHost host)
    {
        var store = host.Services.GetRequiredService<IAttachmentStore>();

        var descriptor = await store.SaveAsync(new AttachmentContent
        {
            TenantId = "default",
            FileName = "test.png",
            MediaType = "image/png",
            Data = Png(),
        });

        return descriptor.Id;
    }

    private static async Task<Guid> SeedApprovalAsync(TraconTestHost host, Guid runId)
    {
        var store = host.Services.GetRequiredService<IPendingApprovalStore>();
        var id = TraconId.NewId();

        await store.CreateAsync(new PendingApproval
        {
            Id = id,
            TenantId = "default",
            RunId = runId,
            SessionId = "session-under-test",
            RequestId = "request-1",
            ToolName = "cancel_order",
            Status = ApprovalStatus.Pending,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return id;
    }

    private static Task<TraconTestHost> StartAsync(ConfigurableRunAuthorizationHandler? handler)
        => TraconTestHost.StartAsync(
            static builder => builder
                .AddModelProvider(new FakeModelProvider(ModelId).RespondsWith("ok"))
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Reply briefly.",
                    Model = new ModelBinding { Provider = ModelId, Model = ModelId },
                }),
            configureServices: services =>
            {
                if (handler is not null)
                {
                    // Registered BEFORE Tracon's own TryAdd, the way a consumer
                    // binding its own policy would (K4: the consumer's registration wins).
                    services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler));
                }
            });

    private static Task<TraconTestHost> StartWorkflowAsync(ConfigurableRunAuthorizationHandler? handler)
        => TraconTestHost.StartAsync(
            static builder => builder
                .AddAgent(TestData.Definition("writer"))
                .AddAgent(TestData.Definition("editor"))
                .UseWorkflows(),
            configureServices: services =>
            {
                if (handler is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton<IRunAuthorizationHandler>(handler));
                }
            });

    /// <summary>Saves a two-step chain, runs it once, and returns the workflow run's id.</summary>
    private static async Task<Guid> RunWorkflowAsync(TraconTestHost host)
    {
        using (var saved = await host.Client.PutAsJsonAsync(
            new Uri("/tracon/api/workflows/chain", UriKind.Relative),
            new WorkflowSaveRequest { Kind = WorkflowKind.Sequential, AgentNames = ["writer", "editor"] }))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/workflows/chain/run", UriKind.Relative),
            new WorkflowRunHttpRequest { Message = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = await SseReader.ReadAllAsync(await response.Content.ReadAsStreamAsync());

        return JsonDocument.Parse(frames[0].Data).RootElement.GetProperty("runId").GetGuid();
    }

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01];

    /// <summary>
    /// A configurable <see cref="IRunAuthorizationHandler"/> test double: every
    /// call is recorded, and the decision is delegated to an injected callback
    /// that defaults to Allow.
    /// </summary>
    private sealed class ConfigurableRunAuthorizationHandler : IRunAuthorizationHandler
    {
        private readonly Lock _gate = new();

        public Func<RunAuthorizationRequest, RunAuthorizationResult>? OnRun { get; set; }

        /// <summary>An asynchronous decision that sees the request's token; wins over <see cref="OnRun"/>.</summary>
        public Func<RunAuthorizationRequest, CancellationToken, Task<RunAuthorizationResult>>? OnRunAsync { get; set; }

        public Func<SessionAuthorizationRequest, RunAuthorizationResult>? OnSession { get; set; }

        public List<RunAuthorizationRequest> RunRequests { get; } = [];

        public ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
            RunAuthorizationRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                RunRequests.Add(request);
            }

            if (OnRunAsync is { } decideAsync)
            {
                return new ValueTask<RunAuthorizationResult>(decideAsync(request, cancellationToken));
            }

            return ValueTask.FromResult(OnRun?.Invoke(request) ?? RunAuthorizationResult.Allow());
        }

        public ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
            SessionAuthorizationRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(OnSession?.Invoke(request) ?? RunAuthorizationResult.Allow());
    }
}
