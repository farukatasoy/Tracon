using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 242 (F-411): the built-in <c>agentReview</c> loop criterion. Another agent of the catalog reviews
/// the draft as a CHILD run, answers with <c>{"approved", "feedback"}</c>, and the iteration event says who
/// reviewed.
/// </summary>
/// <remarks>
/// <para>
/// Only a real host answers these: the reviewer is resolved from the real catalog and runs through the
/// real <c>ChildAgentInvoker</c>, the feedback has to reach the writer's NEXT model request through MAF's
/// loop, and every gate (tenant, depth, budget, approval) is a property of the run tree, not of the criterion.
/// </para>
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class AgentReviewLoopTests
{
    private const string TenantHeader = "X-Tracon-Tenant";
    private const string WriterProvider = "writing";
    private const string WriterModel = "writer-model";
    private const string ReviewerProvider = "reviewing";
    private const string ReviewerModel = "reviewer-model";
    private const string WriterName = "writer";
    private const string ReviewerName = "draft-reviewer";

    [Fact]
    public async Task A_reviewer_that_declines_continues_the_loop_with_its_feedback_and_one_that_approves_stops_it()
    {
        var writer = Writer("a draft without a heading", "# Heading\nA draft with a heading.");
        var reviewer = Reviewer(Verdict(approved: false, "Start with a heading"), Verdict(approved: true));

        await using var host = await StartAsync(writer, reviewer);

        await RunWriterAsync(host, "Write the piece.");

        // The feedback is the writer's next input; the writer ran twice.
        writer.Requests.Count.ShouldBe(2);
        Text(writer.Requests[1].Messages).ShouldContain("Start with a heading");

        // The reviewer got the request, the iteration and the draft as ONE text, each iteration.
        reviewer.Requests.Count.ShouldBe(2);
        var first = Text(reviewer.Requests[0].Messages);
        first.ShouldContain("Write the piece.");
        first.ShouldContain("Iteration: 1");
        first.ShouldContain("a draft without a heading");

        var second = Text(reviewer.Requests[1].Messages);
        second.ShouldContain("Iteration: 2");
        second.ShouldContain("A draft with a heading.");

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Completed);

        var iterations = await IterationEventsAsync(host, root.Id);
        iterations.Count.ShouldBe(2);

        iterations[0].GetProperty("continued").GetBoolean().ShouldBeTrue();
        iterations[0].GetProperty("continuedBy").GetString().ShouldBe("agentReview");
        iterations[0].GetProperty("hasFeedback").GetBoolean().ShouldBeTrue();
        iterations[0].GetProperty("failedCriterion").ValueKind.ShouldBe(JsonValueKind.Null);

        iterations[1].GetProperty("continued").GetBoolean().ShouldBeFalse();
        iterations[1].GetProperty("continuedBy").ValueKind.ShouldBe(JsonValueKind.Null);

        Criterion(iterations[0], 0).GetProperty("decision").GetString().ShouldBe("continue");
        Criterion(iterations[1], 0).GetProperty("decision").GetString().ShouldBe("stop");
    }

    [Fact]
    public async Task The_reviewer_runs_as_a_child_of_the_run_under_review_and_the_event_names_that_run()
    {
        var writer = Writer("first draft", "second draft");
        var reviewer = Reviewer(Verdict(approved: false, "Again"), Verdict(approved: true));

        await using var host = await StartAsync(writer, reviewer);

        await RunWriterAsync(host, "Write.");

        var root = await RootRunAsync(host);
        var children = await ReviewerRunsAsync(host);

        children.Count.ShouldBe(2);
        children.ShouldAllBe(child => child.ParentRunId == root.Id && child.RootRunId == root.Id && child.Depth == 1);

        // The tree endpoint shows the same shape.
        using var tree = await host.Client.GetAsync(new Uri($"/tracon/api/runs/{root.Id}/tree", UriKind.Relative));
        tree.EnsureSuccessStatusCode();
        (await TraconTestHost.ReadJsonAsync(tree)).GetArrayLength().ShouldBe(3);

        var iterations = await IterationEventsAsync(host, root.Id);

        for (var index = 0; index < 2; index++)
        {
            var named = Criterion(iterations[index], 0).GetProperty("reviewer");

            named.GetProperty("agentName").GetString().ShouldBe(ReviewerName);

            // The id in the event is the id of the run the reviewer executed in, and the version is the one
            // that run recorded.
            var runId = named.GetProperty("runId").GetGuid();
            var child = children.Single(candidate => candidate.Id == runId);
            named.GetProperty("agentVersion").GetInt32().ShouldBe(child.AgentVersion.ShouldNotBeNull());
        }

        // Iteration 1 asked the first reviewer run, iteration 2 the second.
        Criterion(iterations[0], 0).GetProperty("reviewer").GetProperty("runId").GetGuid()
            .ShouldNotBe(Criterion(iterations[1], 0).GetProperty("reviewer").GetProperty("runId").GetGuid());
    }

    [Fact]
    public async Task The_reviewers_tokens_are_its_own_runs_and_the_tree_total_carries_both()
    {
        var writer = new FakeModelProvider(WriterProvider);
        writer.ForModel(WriterModel, cfg => cfg.RespondsWith("a draft", inputTokens: 4, outputTokens: 6));

        var reviewer = new FakeModelProvider(ReviewerProvider);
        reviewer.ForModel(ReviewerModel, cfg => cfg.RespondsWith(Verdict(approved: true), inputTokens: 3, outputTokens: 2));

        await using var host = await StartAsync(writer, reviewer);

        await RunWriterAsync(host, "Write.");

        var root = await RootRunAsync(host);
        var child = (await ReviewerRunsAsync(host)).ShouldHaveSingleItem();

        root.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(10);
        child.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(5);
        root.TreeUsage.ShouldNotBeNull().TotalTokens.ShouldBe(15);
    }

    [Fact]
    public async Task The_event_names_the_reviewer_but_carries_neither_the_feedback_nor_the_draft()
    {
        var writer = Writer("SECRET-DRAFT-TEXT", "final");
        var reviewer = Reviewer(Verdict(approved: false, "SECRET-FEEDBACK-TEXT"), Verdict(approved: true));

        await using var host = await StartAsync(writer, reviewer);

        await RunWriterAsync(host, "Write.");

        var root = await RootRunAsync(host);
        var runs = host.Services.GetRequiredService<IRunStore>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            (runEvent.Text ?? string.Empty).ShouldNotContain("SECRET-FEEDBACK-TEXT", Case.Sensitive);
            (runEvent.Payload ?? string.Empty).ShouldNotContain("SECRET-FEEDBACK-TEXT", Case.Sensitive);

            if (runEvent.Type == RunEventType.LoopIterationCompleted)
            {
                (runEvent.Payload ?? string.Empty).ShouldNotContain("SECRET-DRAFT-TEXT", Case.Sensitive);
            }
        }

        // The event text says who decided, in both the continuing and the stopping iteration.
        var texts = new List<string>();

        await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
        {
            if (runEvent.Type == RunEventType.LoopIterationCompleted)
            {
                texts.Add(runEvent.Text.ShouldNotBeNull());
            }
        }

        texts.Count.ShouldBe(2);
        texts[0].ShouldStartWith("iteration 1 continued by 'agentReview' (reviewer 'draft-reviewer'");
        texts[1].ShouldStartWith("iteration 2 stopped (approved by reviewer 'draft-reviewer'");
    }

    [Fact]
    public async Task A_reviewer_that_declines_without_feedback_sends_a_fixed_sentence()
    {
        var writer = Writer("draft", "better draft");
        var reviewer = Reviewer("{\"approved\":false}", Verdict(approved: true));

        await using var host = await StartAsync(writer, reviewer);

        await RunWriterAsync(host, "Write.");

        writer.Requests.Count.ShouldBe(2);
        Text(writer.Requests[1].Messages).ShouldContain(AgentReviewLoopEvaluator.NoFeedback);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("{\"feedback\":\"no verdict\"}")]
    [InlineData("{\"approved\":\"yes\"}")]
    [InlineData("[]")]
    public async Task A_verdict_that_cannot_be_read_fails_the_criterion_instead_of_approving_the_draft(string answer)
    {
        var writer = Writer("draft one", "draft two");
        var reviewer = Reviewer(answer);

        await using var host = await StartAsync(writer, reviewer);

        await RunWriterAsync(host, "Write.");

        // The default outcome: the loop stops and the last draft comes back. The failure is on the record.
        writer.Requests.Count.ShouldBe(1);

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Completed);

        var iteration = (await IterationEventsAsync(host, root.Id)).ShouldHaveSingleItem();
        iteration.GetProperty("failedCriterion").GetString().ShouldBe("agentReview");
        iteration.GetProperty("failsRun").GetBoolean().ShouldBeFalse();
        iteration.GetProperty("continued").GetBoolean().ShouldBeFalse();

        var entry = Criterion(iteration, 0);
        entry.GetProperty("decision").GetString().ShouldBe("failed");

        // The reviewer did run: its run is named even though its answer was no verdict.
        entry.GetProperty("reviewer").GetProperty("runId").GetGuid()
            .ShouldBe((await ReviewerRunsAsync(host)).ShouldHaveSingleItem().Id);
    }

    [Fact]
    public async Task A_reviewer_failure_fails_the_run_with_the_reviewers_own_error_when_criterion_errors_fail_the_run()
    {
        var writer = Writer("draft");
        var reviewer = Reviewer("not a verdict");

        await using var host = await StartAsync(writer, reviewer, onCriterionError: LoopOutcome.FailRun);

        await Should.ThrowAsync<TraconException>(async () => await RunWriterAsync(host, "Write."));

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull().Message.ShouldContain("did not give a readable verdict", Case.Sensitive);

        // The text of the answer is not repeated in the error.
        root.Error.Message.ShouldNotContain("not a verdict", Case.Sensitive);

        var iteration = (await IterationEventsAsync(host, root.Id)).ShouldHaveSingleItem();
        iteration.GetProperty("failsRun").GetBoolean().ShouldBeTrue();
        iteration.GetProperty("failedCriterion").GetString().ShouldBe("agentReview");
    }

    [Fact]
    public async Task A_reviewer_the_tree_budget_refuses_fails_the_run_with_the_budget_error_when_criterion_errors_fail_the_run()
    {
        // FailRun hands the run the criterion's OWN error. A refusal read as the reviewer's answer would instead
        // fail it as an unreadable verdict, and a client that tells a spent budget from a broken reviewer could not.
        var writer = Writer("draft one", "draft two");
        var reviewer = Reviewer(Verdict(approved: false, "once more"), Verdict(approved: true));

        await using var host = await StartAsync(
            writer,
            reviewer,
            onCriterionError: LoopOutcome.FailRun,
            configureOptions: options => options.AgentGraph.MaxTotalRuns = 1);

        await Should.ThrowAsync<TraconException>(async () => await RunWriterAsync(host, "Write."));

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Failed);

        var error = root.Error.ShouldNotBeNull();
        error.Class.ShouldBe(RunErrorClass.QuotaExceeded);
        error.Message.ShouldNotContain("readable verdict", Case.Sensitive);

        // The first review ran; the second was refused, and the event of that iteration says the run failed there.
        reviewer.Requests.Count.ShouldBe(1);
        (await IterationEventsAsync(host, root.Id)).Last().GetProperty("failsRun").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task A_reviewer_the_depth_limit_refuses_fails_the_run_with_the_refusal_when_criterion_errors_fail_the_run()
    {
        var writer = Writer("draft");
        var reviewer = Reviewer(Verdict(approved: true));

        await using var host = await StartAsync(
            writer,
            reviewer,
            onCriterionError: LoopOutcome.FailRun,
            configureOptions: options => options.AgentGraph.MaxDepth = 0);

        await Should.ThrowAsync<TraconException>(async () => await RunWriterAsync(host, "Write."));

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Failed);
        root.Error.ShouldNotBeNull().Message.ShouldContain("depth limit", Case.Sensitive);
        reviewer.Requests.Count.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reviewer_that_outlasts_the_wait_limit_fails_the_criterion_and_the_event_still_names_its_run(bool ignoresCancellation)
    {
        // The reviewer shares the agent's sub-agent wait limits: the cooperative deadline cancels a reviewer that reads the
        // token, the hard cutoff abandons one that does not. Either way the run it opened is on the record.
        var hanging = new HangingModelProvider(ReviewerProvider, respectsCancellation: !ignoresCancellation);

        await using var host = await StartAsync(
            Writer("a draft"),
            hanging,
            onCriterionError: LoopOutcome.FailRun,
            subAgents: new SubAgentSettings
            {
                ChildDeadline = TimeSpan.FromMilliseconds(50),
                WaitTimeout = ignoresCancellation ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromSeconds(5),
            });

        try
        {
            await Should.ThrowAsync<TraconException>(async () => await RunWriterAsync(host, "Write."));

            var root = await RootRunAsync(host);
            root.Status.ShouldBe(RunStatus.Failed);

            // The failure is the wait limit's own, named for what called it.
            var message = root.Error.ShouldNotBeNull().Message;
            message.ShouldContain("did not respond in time", Case.Sensitive);
            message.ShouldContain("The loop reviewer failed", Case.Sensitive);
            message.ShouldContain("the agent's own 'SubAgents.ChildDeadline'", Case.Sensitive);

            var runs = host.Services.GetRequiredService<IRunStore>();
            var events = new List<RunEvent>();

            await foreach (var runEvent in runs.ReadEventsAsync(root.Id))
            {
                events.Add(runEvent);
            }

            var timedOut = events.Single(static runEvent => runEvent.Type == RunEventType.ChildRunTimedOut);
            using var timeoutPayload = JsonDocument.Parse(timedOut.Payload.ShouldNotBeNull());
            timeoutPayload.RootElement.GetProperty("hardCutoff").GetBoolean().ShouldBe(ignoresCancellation);

            // The iteration event names the reviewer run that timed out, although it never answered.
            var iteration = (await IterationEventsAsync(host, root.Id)).ShouldHaveSingleItem();
            iteration.GetProperty("failedCriterion").GetString().ShouldBe("agentReview");
            iteration.GetProperty("failsRun").GetBoolean().ShouldBeTrue();
            Criterion(iteration, 0).GetProperty("reviewer").GetProperty("runId").GetGuid()
                .ShouldBe(timeoutPayload.RootElement.GetProperty("childRunId").GetGuid());
        }
        finally
        {
            // Let an abandoned call finish so it does not outlive the host.
            hanging.Release("late");
        }
    }

    [Fact]
    public async Task A_loop_ceiling_ends_a_loop_whose_reviewer_never_approves()
    {
        var writer = Writer("one", "two", "three");
        var reviewer = Reviewer(Verdict(approved: false, "no"), Verdict(approved: false, "no"), Verdict(approved: false, "no"));

        await using var host = await StartAsync(writer, reviewer, maxIterations: 2);

        await RunWriterAsync(host, "Write.");

        // The turn that reaches the ceiling is not evaluated, so the reviewer ran once.
        writer.Requests.Count.ShouldBe(2);
        reviewer.Requests.Count.ShouldBe(1);

        var iteration = (await IterationEventsAsync(host, (await RootRunAsync(host)).Id)).ShouldHaveSingleItem();
        iteration.GetProperty("ceilingReached").GetBoolean().ShouldBeTrue();
        iteration.GetProperty("continuedBy").GetString().ShouldBe("agentReview");
    }

    [Theory]
    [InlineData("depth")]
    [InlineData("runs")]
    public async Task A_full_tree_budget_or_depth_starts_no_reviewer_run_and_the_criterion_fails(string limit)
    {
        var writer = Writer("draft one", "draft two");
        var reviewer = Reviewer(Verdict(approved: false, "once more"), Verdict(approved: true));

        // Depth: the reviewer would be at depth 1 and the tree allows 0. Runs: one child run fits, the second does not.
        await using var host = await StartAsync(
            writer,
            reviewer,
            configureOptions: options =>
            {
                if (string.Equals(limit, "depth", StringComparison.Ordinal))
                {
                    options.AgentGraph.MaxDepth = 0;
                }
                else
                {
                    options.AgentGraph.MaxTotalRuns = 1;
                }
            });

        await RunWriterAsync(host, "Write.");

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Completed);

        var iterations = await IterationEventsAsync(host, root.Id);

        if (string.Equals(limit, "depth", StringComparison.Ordinal))
        {
            // Refused before any child run: nothing was reserved, nothing started.
            reviewer.Requests.Count.ShouldBe(0);
            (await ReviewerRunsAsync(host)).ShouldBeEmpty();

            var only = iterations.ShouldHaveSingleItem();
            only.GetProperty("failedCriterion").GetString().ShouldBe("agentReview");

            // No run started, so the event has no run to name.
            Criterion(only, 0).GetProperty("reviewer").GetProperty("runId").ValueKind.ShouldBe(JsonValueKind.Null);
        }
        else
        {
            // The first review used the one slot; the second was refused and the count did not move.
            reviewer.Requests.Count.ShouldBe(1);
            (await ReviewerRunsAsync(host)).Count.ShouldBe(1);
            root.ChildRunCount.ShouldBe(1);

            iterations.Count.ShouldBe(2);
            iterations[0].GetProperty("continuedBy").GetString().ShouldBe("agentReview");
            iterations[1].GetProperty("failedCriterion").GetString().ShouldBe("agentReview");
        }
    }

    [Fact]
    public async Task A_reviewer_that_needs_a_tool_approval_fails_the_criterion_and_leaves_no_run_waiting()
    {
        var writer = Writer("a draft");
        var reviewer = new FakeModelProvider(ReviewerProvider);
        reviewer.ForModel(ReviewerModel, cfg => cfg.CallsTool("approve_draft", new { id = "d-1" }).EchoesLastToolResult());

        var ran = new int[1];

        await using var host = await StartAsync(
            writer,
            reviewer,
            configureTracon: builder => builder.AddTool(
                (Func<string, string>)(id =>
                {
                    Interlocked.Increment(ref ran[0]);

                    return $"{id} approved.";
                }),
                name: "approve_draft",
                description: "Approves a draft.",
                configure: options => options.RequiresApproval = true),
            reviewerTools: ["approve_draft"]);

        await RunWriterAsync(host, "Write.");

        // An agent tree cannot wait in its middle: the tool did not run, the refusal is the criterion's failure,
        // and the run under review finishes instead of waiting for an answer nobody can give.
        ran[0].ShouldBe(0, "the tool ran although it needs approval");

        var root = await RootRunAsync(host);
        root.Status.ShouldBe(RunStatus.Completed);

        var iteration = (await IterationEventsAsync(host, root.Id)).ShouldHaveSingleItem();
        iteration.GetProperty("failedCriterion").GetString().ShouldBe("agentReview");
    }

    [Fact]
    public async Task A_reviewer_tool_that_needs_approval_fails_the_run_naming_the_loop_reviewer_when_criterion_errors_fail_the_run()
    {
        var writer = Writer("a draft");
        var reviewer = new FakeModelProvider(ReviewerProvider);
        reviewer.ForModel(ReviewerModel, cfg => cfg.CallsTool("approve_draft", new { id = "d-1" }).EchoesLastToolResult());

        await using var host = await StartAsync(
            writer,
            reviewer,
            onCriterionError: LoopOutcome.FailRun,
            configureTracon: builder => builder.AddTool(
                (Func<string, string>)(static id => $"{id} approved."),
                name: "approve_draft",
                description: "Approves a draft.",
                configure: static options => options.RequiresApproval = true),
            reviewerTools: ["approve_draft"]);

        await Should.ThrowAsync<TraconException>(async () => await RunWriterAsync(host, "Write."));

        var root = await RootRunAsync(host);

        root.Status.ShouldBe(RunStatus.Failed);

        // The refusal speaks of the loop reviewer, not of a workflow step the run never had.
        var message = root.Error.ShouldNotBeNull().Message;
        message.ShouldContain("A loop reviewer cannot wait for a tool approval", Case.Sensitive);
        message.ShouldNotContain("workflow step", Case.Sensitive);
    }

    [Fact]
    public async Task A_pinned_reviewer_version_runs_even_after_a_newer_version_is_saved()
    {
        var writer = Writer("draft one", "draft two");

        // Version 1 and version 2 of the reviewer run on providers of their own, so a request names the version.
        var versionOne = new FakeModelProvider("reviewing-one");
        versionOne.ForModel(ReviewerModel, cfg => cfg.RespondsWith(Verdict(approved: false, "from version one"), Verdict(approved: true)));
        var versionTwo = new FakeModelProvider("reviewing-two");
        versionTwo.ForModel(ReviewerModel, cfg => cfg.RespondsWith(Verdict(approved: true)));

        await using var host = await TraconTestHost.StartAsync(configureTracon: builder =>
        {
            builder.AddModelProvider(writer);
            builder.AddModelProvider(versionOne);
            builder.AddModelProvider(versionTwo);
            builder.AddAgent(WriterAgent(reviewerVersion: 1));
        });

        await SaveReviewerAsync(host, "reviewing-one", tenant: null);
        await SaveReviewerAsync(host, "reviewing-two", tenant: null);

        await RunWriterAsync(host, "Write.");

        // Version 1 stayed the reviewer although version 2 is now the current one.
        versionOne.Requests.Count.ShouldBe(2);
        versionTwo.Requests.Count.ShouldBe(0);

        var root = await RootRunAsync(host);
        var children = await ReviewerRunsAsync(host);

        children.ShouldAllBe(child => child.AgentVersion == 1);

        var iterations = await IterationEventsAsync(host, root.Id);
        Criterion(iterations[0], 0).GetProperty("reviewer").GetProperty("agentVersion").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task A_reviewer_that_exists_only_in_another_tenant_is_not_found_and_the_criterion_fails()
    {
        var writer = Writer("draft");
        var reviewer = Reviewer(Verdict(approved: true));

        await using var host = await TraconTestHost.StartAsync(configureTracon: builder =>
        {
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
            builder.AddModelProvider(writer);
            builder.AddModelProvider(reviewer);

            // The writer is defined in code, so every tenant has it. The reviewer is stored: only one tenant has it.
            builder.AddAgent(WriterAgent(reviewerVersion: null));
        });

        await SaveReviewerAsync(host, ReviewerProvider, tenant: "tenant-b");

        // Tenant A: validation says so, and the run's criterion fails without borrowing tenant B's agent.
        using var validation = await PostAsync(host, "/tracon/api/agents/validate", "tenant-a", WriterBody());
        var report = await TraconTestHost.ReadJsonAsync(validation);
        report.GetProperty("valid").GetBoolean().ShouldBeFalse();
        report.GetProperty("messages").EnumerateArray()
            .Select(static message => message.GetProperty("code").GetString())
            .ShouldContain("loop_reviewer_not_found", StringComparer.Ordinal);

        var runA = await RunWriterOverHttpAsync(host, "tenant-a");
        var eventsA = await EventsOverHttpAsync(host, "tenant-a", runA);
        reviewer.Requests.Count.ShouldBe(0, "tenant A's loop must not run tenant B's reviewer");
        eventsA.Single().GetProperty("failedCriterion").GetString().ShouldBe("agentReview");

        // Tenant B: the same code agent finds the reviewer there.
        var runB = await RunWriterOverHttpAsync(host, "tenant-b");
        var eventsB = await EventsOverHttpAsync(host, "tenant-b", runB);
        reviewer.Requests.Count.ShouldBe(1);
        eventsB.Single().GetProperty("failedCriterion").ValueKind.ShouldBe(JsonValueKind.Null);
        Criterion(eventsB.Single(), 0).GetProperty("decision").GetString().ShouldBe("stop");
    }

    [Fact]
    public async Task The_native_stream_frame_names_the_reviewer_and_it_is_the_frame_the_events_stream_serves()
    {
        var writer = Writer("first draft", "second draft");
        var reviewer = Reviewer(Verdict(approved: false, "Again"), Verdict(approved: true));

        await using var host = await StartAsync(writer, reviewer);

        // The evaluator runs inside the stream's own enumeration, where an ambient write made once at the top
        // would not survive to the next MoveNextAsync: only a streaming run proves the reviewer reaches the frame.
        using var response = await SendAsync(
            host, HttpMethod.Post, $"/tracon/api/agents/{WriterName}/run", tenant: null, new AgentRunRequest { Message = "Write." });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var frames = ParseLoopPayloads(await response.Content.ReadAsStringAsync());
        var root = await RootRunAsync(host);
        var stored = await IterationEventsAsync(host, root.Id);
        var children = await ReviewerRunsAsync(host);

        frames.Count.ShouldBe(2);
        frames.Select(static frame => frame.GetRawText()).ShouldBe(stored.Select(static payload => payload.GetRawText()));

        for (var index = 0; index < 2; index++)
        {
            var named = Criterion(frames[index], 0).GetProperty("reviewer");

            named.GetProperty("agentName").GetString().ShouldBe(ReviewerName);
            children.ShouldContain(child => child.Id == named.GetProperty("runId").GetGuid() && child.ParentRunId == root.Id);
        }
    }

    [Fact]
    public async Task Two_runs_at_once_keep_their_own_reviewer_runs()
    {
        var writer = new FakeModelProvider(WriterProvider);
        writer.ForModel(WriterModel, cfg => cfg.EchoesUserMessage());

        var reviewer = new FakeModelProvider(ReviewerProvider);
        reviewer.ForModel(ReviewerModel, cfg => cfg.RespondsWith(Verdict(approved: true)));

        await using var host = await StartAsync(writer, reviewer);

        await Task.WhenAll(
            Enumerable.Range(0, 6).Select(index => RunWriterAsync(host, $"Write piece {index}.")));

        var runs = host.Services.GetRequiredService<IRunStore>();
        var roots = (await runs.QueryRunsAsync(new RunQuery { AgentName = WriterName })).ToList();
        var children = await ReviewerRunsAsync(host);

        roots.Count.ShouldBe(6);
        children.Count.ShouldBe(6);

        // Each root's event names exactly the reviewer run that is its own child.
        foreach (var root in roots)
        {
            var named = Criterion((await IterationEventsAsync(host, root.Id)).ShouldHaveSingleItem(), 0)
                .GetProperty("reviewer").GetProperty("runId").GetGuid();

            children.Single(child => child.Id == named).ParentRunId.ShouldBe(root.Id);
        }
    }

    private static string Verdict(bool approved, string? feedback = null)
        => JsonSerializer.Serialize(new { approved, feedback });

    private static FakeModelProvider Writer(params string[] drafts)
    {
        var provider = new FakeModelProvider(WriterProvider);
        provider.ForModel(WriterModel, cfg => cfg.RespondsWith(drafts));

        return provider;
    }

    private static FakeModelProvider Reviewer(params string[] answers)
    {
        var provider = new FakeModelProvider(ReviewerProvider);
        provider.ForModel(ReviewerModel, cfg => cfg.RespondsWith(answers));

        return provider;
    }

    private static AgentDefinition WriterAgent(
        int? reviewerVersion,
        int maxIterations = 3,
        LoopOutcome onCriterionError = LoopOutcome.ReturnLastResponse,
        SubAgentSettings? subAgents = null)
        => new()
        {
            SubAgents = subAgents,
            Name = WriterName,
            Description = "Writes a draft until the reviewer approves it.",
            Instructions = "Write the draft.",
            Model = new ModelBinding { Provider = WriterProvider, Model = WriterModel },
            Harness = new HarnessSettings
            {
                Loop = new LoopSettings
                {
                    Criteria =
                    [
                        new LoopCriterion
                        {
                            Kind = "agentReview",
                            ReviewerAgent = ReviewerName,
                            ReviewerAgentVersion = reviewerVersion,
                        },
                    ],
                    MaxIterations = maxIterations,
                    OnCriterionError = onCriterionError,
                },
            },
            Origin = AgentDefinitionOrigin.Code,
        };

    private static object WriterBody() => new
    {
        name = "validated-writer",
        model = new { provider = WriterProvider, model = WriterModel },
        harness = new
        {
            loop = new
            {
                criteria = new[] { new { kind = "agentReview", reviewerAgent = ReviewerName } },
            },
        },
    };

    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider writer,
        IModelProvider reviewer,
        int maxIterations = 3,
        LoopOutcome onCriterionError = LoopOutcome.ReturnLastResponse,
        Action<TraconOptions>? configureOptions = null,
        Action<ITraconBuilder>? configureTracon = null,
        string[]? reviewerTools = null,
        SubAgentSettings? subAgents = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(writer);
                builder.AddModelProvider(reviewer);
                configureTracon?.Invoke(builder);

                builder.AddAgent(new AgentDefinition
                {
                    Name = ReviewerName,
                    Description = "Reviews drafts and answers with a JSON verdict.",
                    Instructions = "Review the draft. Answer with {\"approved\": true|false, \"feedback\": \"...\"}.",
                    Model = new ModelBinding { Provider = ReviewerProvider, Model = ReviewerModel },
                    ToolNames = reviewerTools ?? [],
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(WriterAgent(reviewerVersion: null, maxIterations, onCriterionError, subAgents));
            },
            configureServices: configureOptions is null
                ? null
                : services => services.Configure(configureOptions));

    private static async Task SaveReviewerAsync(TraconTestHost host, string provider, string? tenant)
    {
        var body = new
        {
            name = ReviewerName,
            description = "Reviews drafts and answers with a JSON verdict.",
            instructions = "Review the draft.",
            model = new { provider, model = ReviewerModel },
        };

        // The first save creates version 1; a second one on the same name is an update.
        using var created = await SendAsync(host, HttpMethod.Post, "/tracon/api/agents", tenant, body);

        if (created.StatusCode == HttpStatusCode.Conflict)
        {
            using var updated = await SendAsync(host, HttpMethod.Put, $"/tracon/api/agents/{ReviewerName}", tenant, body);
            updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());

            return;
        }

        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostAsync(TraconTestHost host, string path, string? tenant, object body)
        => SendAsync(host, HttpMethod.Post, path, tenant, body);

    private static Task<HttpResponseMessage> SendAsync(TraconTestHost host, HttpMethod method, string path, string? tenant, object? body)
    {
        var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };

        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        return host.Client.SendAsync(request);
    }

    private static async Task<Guid> RunWriterOverHttpAsync(TraconTestHost host, string tenant)
    {
        // The native run endpoint streams; reading the whole body is the run's end.
        using var response = await SendAsync(
            host, HttpMethod.Post, $"/tracon/api/agents/{WriterName}/run", tenant, new AgentRunRequest { Message = "Write." });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        using var listing = await SendAsync(host, HttpMethod.Get, $"/tracon/api/runs?agentName={WriterName}", tenant, body: null);
        listing.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await TraconTestHost.ReadJsonAsync(listing)).EnumerateArray().Single().GetProperty("id").GetGuid();
    }

    private static async Task<List<JsonElement>> EventsOverHttpAsync(TraconTestHost host, string tenant, Guid runId)
    {
        using var response = await SendAsync(host, HttpMethod.Get, $"/tracon/api/runs/{runId}/events", tenant, body: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return ParseLoopPayloads(await response.Content.ReadAsStringAsync());
    }

    /// <summary>Server-sent events: the line after "event: loop.iteration-completed" is the serialized RunEvent.</summary>
    private static List<JsonElement> ParseLoopPayloads(string body)
    {
        var lines = body.Split('\n');
        var payloads = new List<JsonElement>();

        for (var index = 0; index < lines.Length - 1; index++)
        {
            if (!string.Equals(lines[index].TrimEnd('\r'), "event: loop.iteration-completed", StringComparison.Ordinal))
            {
                continue;
            }

            var data = lines.Skip(index + 1).First(static line => line.StartsWith("data:", StringComparison.Ordinal))["data:".Length..].Trim();

            using var runEvent = JsonDocument.Parse(data);
            using var payload = JsonDocument.Parse(runEvent.RootElement.GetProperty("payload").GetString().ShouldNotBeNull());
            payloads.Add(payload.RootElement.Clone());
        }

        return payloads;
    }

    private static async Task RunWriterAsync(TraconTestHost host, string message)
    {
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
                .ResolveAsync(WriterName, culture: null, CancellationToken.None))
            .ShouldNotBeNull();
        var session = await agent.CreateSessionAsync();

        await agent
            .RunAsync([new ChatMessage(ChatRole.User, message)], session)
            .WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static async Task<RunRecord> RootRunAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        return (await runs.QueryRunsAsync(new RunQuery { AgentName = WriterName })).ShouldHaveSingleItem();
    }

    private static async Task<List<RunRecord>> ReviewerRunsAsync(TraconTestHost host)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        return (await runs.QueryRunsAsync(new RunQuery { AgentName = ReviewerName, OnlyRootRuns = false })).ToList();
    }

    private static async Task<List<JsonElement>> IterationEventsAsync(TraconTestHost host, Guid runId)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var payloads = new List<JsonElement>();

        await foreach (var runEvent in runs.ReadEventsAsync(runId))
        {
            if (runEvent.Type == RunEventType.LoopIterationCompleted)
            {
                using var document = JsonDocument.Parse(runEvent.Payload.ShouldNotBeNull());
                payloads.Add(document.RootElement.Clone());
            }
        }

        return payloads;
    }

    private static JsonElement Criterion(JsonElement iteration, int index)
        => iteration.GetProperty("criteria")[index];

    private static string Text(IReadOnlyList<ChatMessage> messages)
        => string.Join("\n", messages.Select(static message => message.Text));
}
#pragma warning restore MAAI001
