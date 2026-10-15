using System.Net;
using System.Text.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using static Tracon.AspNetCore.FunctionalTests.Infrastructure.ReviewAndReviseHarness;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A code graph with a human review and a bounded revision loop (phase 211): the writer is an agent step
/// that a back edge can reach, and the loop's counter survives the run that answers.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 Two things only the real host shows. First, an <c>AIAgentBinding</c> that is not the graph's entry never
/// runs when a message returns to it over a back edge - the run ends with no output and no error (K-495),
/// and the answer run sends no <c>TurnToken</c> at all. The agent step is a plain function executor and
/// runs whenever a message arrives. Second, every answer is a NEW run and the graph is built again, so a
/// counter kept in a field of an executor starts from zero each time; the counter here lives in the
/// workflow's state, which the run that answers restores from the checkpoint.
/// </para>
/// </remarks>
public sealed class CodeWorkflowRevisionTests
{
    [Fact]
    public async Task A_rejected_draft_goes_back_to_the_writer_with_the_note_and_the_media_nodes_run_once_after_the_approval()
    {
        var harness = new ReviewAndReviseHarness("first draft", "second draft");
        await using var host = await harness.StartAsync();

        var (first, _) = await StartRunAsync(host);
        await PendingDraftAsync(host, first);

        var (second, secondFrames) = await AnswerAsync(host, first, new ReviewDecision(false, "make it shorter"));

        // The back edge ran the agent step: it is called a second time, with the note and the previous draft.
        harness.Provider.Requests.Count.ShouldBe(2);

        var secondRequest = string.Join(" ", harness.Provider.Requests[1].Messages.Select(static message => message.Text));

        secondRequest.ShouldContain("make it shorter", Case.Sensitive);
        secondRequest.ShouldContain("first draft", Case.Sensitive);

        // The revision waits for its own review; the media nodes did not run for the draft that was rejected.
        secondFrames.ShouldContain(static frame => frame.Data.Contains("second draft", StringComparison.Ordinal));
        harness.Media.ShouldBeEmpty();

        var (third, thirdFrames) = await AnswerAsync(host, second, new ReviewDecision(true, null));

        (await host.WaitForTerminalRunAsync(third)).Status.ShouldBe(RunStatus.Completed);
        thirdFrames[^1].Event.ShouldBe("done");
        harness.Media.ShouldBe(["tts", "render", "qa"]);
    }

    [Fact]
    public async Task The_revision_counter_survives_the_run_that_answers_and_the_run_fails_after_the_limit()
    {
        var harness = new ReviewAndReviseHarness("one", "two", "three", "four") { MaxRevisions = 2 };
        await using var host = await harness.StartAsync();

        var (waiting, _) = await StartRunAsync(host);
        var reject = new ReviewDecision(false, "again");

        // Two revisions are allowed: each rejection is answered by a NEW run on a freshly built graph.
        (waiting, _) = await AnswerAsync(host, waiting, reject);
        (waiting, _) = await AnswerAsync(host, waiting, reject);

        // The third rejection finds the counter at the limit. A counter that started from zero in each
        // run would ask for a fourth draft and never fail.
        var (last, _) = await AnswerAsync(host, waiting, reject);
        var run = await host.WaitForTerminalRunAsync(last);

        run.Status.ShouldBe(RunStatus.Failed);

        var error = run.Error.ShouldNotBeNull();

        error.Type.ShouldBe(ContentRejectedException.ErrorTypeName);
        error.Message.ShouldContain("limit is 2", Case.Sensitive);
        harness.Provider.Requests.Count.ShouldBe(3, "the writer was asked for a fourth draft");
        harness.Media.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_draft_that_fails_validation_goes_back_to_the_writer_and_is_never_shown_to_the_reviewer()
    {
        var harness = new ReviewAndReviseHarness($"too long {ViolationMarker}", "short enough");
        await using var host = await harness.StartAsync();

        var (waiting, frames) = await StartRunAsync(host);

        harness.Provider.Requests.Count.ShouldBe(2);
        frames.ShouldNotContain(static frame => frame.Data.Contains("\"WorkflowRequest\"", StringComparison.Ordinal) && frame.Data.Contains("too long", StringComparison.Ordinal));
        frames.Count(static frame => frame.Data.Contains("\"WorkflowRequest\"", StringComparison.Ordinal)).ShouldBe(1);

        var shown = await PendingDraftAsync(host, waiting);

        shown.ShouldContain("short enough", Case.Sensitive);
        shown.ShouldNotContain("too long", Case.Sensitive);

        var secondRequest = string.Join(" ", harness.Provider.Requests[1].Messages.Select(static message => message.Text));

        secondRequest.ShouldContain("over the length limit", Case.Sensitive);
    }

    [Fact]
    public async Task Another_tenant_cannot_see_or_answer_the_review_of_a_run_it_does_not_own()
    {
        var harness = new ReviewAndReviseHarness("first draft", "second draft");

        await using var host = await harness.StartAsync(configureTracon: static builder => builder.UseTenancy(static options =>
        {
            options.Enabled = true;
            options.AllowHeaderResolution = true;
        }));

        var (runId, requestId) = await WorkflowResponseHarness.RunToAwaitAsync(host, Workflow, "tenant-a");

        // Tenant B neither lists the request nor answers it; the run is not even confirmed to exist.
        using var listing = new HttpRequestMessage(HttpMethod.Get, new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));
        listing.Headers.Add(WorkflowResponseHarness.TenantHeader, "tenant-b");

        using var listed = await host.Client.SendAsync(listing);

        listed.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var refused = await WorkflowResponseHarness.RespondAsync(
            host,
            runId,
            new { requestId, data = JsonSerializer.SerializeToElement(new ReviewDecision(true, null), JsonSerializerOptions.Web) },
            "tenant-b");

        refused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        harness.Media.ShouldBeEmpty();
        harness.Provider.Requests.Count.ShouldBe(1);

        // The owner still can.
        using var answered = await WorkflowResponseHarness.RespondAsync(
            host,
            runId,
            new { requestId, data = JsonSerializer.SerializeToElement(new ReviewDecision(true, null), JsonSerializerOptions.Web) },
            "tenant-a");
        var frames = await WorkflowResponseHarness.FramesAsync(answered);

        await host.WaitForRunStatusAsync(WorkflowResponseHarness.RunIdOf(frames), nameof(RunStatus.Completed), "tenant-a");
        harness.Media.ShouldBe(["tts", "render", "qa"]);
    }
}
