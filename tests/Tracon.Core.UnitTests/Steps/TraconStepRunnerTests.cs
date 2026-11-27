using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Steps;

/// <summary>
/// A step of the host's own code is recorded as a run: its row, its status, what it spent, and its place in a tree.
/// </summary>
/// <remarks>
/// <para>
/// An agent run and a workflow run record themselves; a deterministic job that draws an image or voices a script had no run, so
/// its cost was in no run, no statistic and no quota. The cases read what the STORE holds after the step, not what the runner
/// reports, and they read the ambient scope both inside the step and after it, because a value assigned in a method that returns
/// before the step runs would not reach it.
/// </para>
/// <para>
/// 🚨 The parent cases put a run of another tenant in front of the runner: attaching to it would put one tenant's cost into the
/// other's tree.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class TraconStepRunnerTests
{
    private const string ImageProvider = "step-images";
    private const string SpeechProvider = "step-speech";

    [Fact]
    public async Task A_step_is_a_completed_run_of_its_own_kind_with_the_name_the_tenant_and_the_user()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();
        AgentRunScope? inside = null;

        using (AmbientRunAttributionScope.Begin("ada", new Dictionary<string, string>(StringComparer.Ordinal) { ["job"] = "j1" }))
        {
            var result = await steps.RunAsync(
                new TraconStepOptions { Name = "podcast-render", RunId = runId },
                _ =>
                {
                    inside = TraconRunContext.Current;

                    return ValueTask.FromResult(42);
                },
                TestContext.Current.CancellationToken);

            result.ShouldBe(42);
        }

        TraconRunContext.Current.ShouldBeNull("the caller's own scope comes back when the step is over");
        inside.ShouldNotBeNull().RunId.ShouldBe(runId);
        inside!.RootRunId.ShouldBe(runId);
        inside.AgentName.ShouldBe("podcast-render");

        var record = (await Runs(provider).GetRunAsync(runId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        record.Kind.ShouldBe(RunKind.Step);
        record.Status.ShouldBe(RunStatus.Completed);
        record.AgentName.ShouldBe("podcast-render");
        record.TenantId.ShouldBe("default");
        record.UserId.ShouldBe("ada");
        record.Labels.ShouldNotBeNull().ShouldContainKeyAndValue("job", "j1");
        record.ParentRunId.ShouldBeNull();
        record.Usage.ShouldBeNull("a step has no model of its own");
        record.NonTokenCost.ShouldBeNull("it reported nothing");
    }

    [Fact]
    public async Task A_step_that_throws_is_a_failed_run_and_the_exception_is_thrown_on_unchanged()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();
        var thrown = new InvalidOperationException("the step's own failure");

        var caught = await Should.ThrowAsync<InvalidOperationException>(
            async () => await steps.RunAsync(
                new TraconStepOptions { Name = "broken", RunId = runId },
                _ => throw thrown,
                TestContext.Current.CancellationToken));

        caught.ShouldBeSameAs(thrown);

        var record = (await Runs(provider).GetRunAsync(runId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        record.Status.ShouldBe(RunStatus.Failed);
        record.Error.ShouldNotBeNull().Class.ShouldNotBeNull("a failed step is classified like any failed run");
    }

    [Fact]
    public async Task A_cancelled_caller_closes_the_step_as_canceled()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = steps.RunAsync(
            new TraconStepOptions { Name = "waiting", RunId = runId },
            async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); // delay: simulated
            },
            cancellation.Token).AsTask();

        await started.Task;
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await run);

        (await Runs(provider).GetRunAsync(runId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task Cancelling_the_run_reaches_the_token_of_the_step()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var registry = provider.GetRequiredService<IRunCancellationRegistry>();
        var runId = Guid.CreateVersion7();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = steps.RunAsync(
            new TraconStepOptions { Name = "cancel-me", RunId = runId },
            async token =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false); // delay: simulated
            },
            CancellationToken.None).AsTask();

        await started.Task;

        registry.TryCancel(runId, "default").ShouldBeTrue();

        await Should.ThrowAsync<OperationCanceledException>(async () => await run);

        (await Runs(provider).GetRunAsync(runId, TestContext.Current.CancellationToken))
            .ShouldNotBeNull().Status.ShouldBe(RunStatus.Canceled);
    }

    [Fact]
    public async Task The_images_and_the_speech_of_a_step_are_its_non_token_items_and_its_cost()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();
        TraconImageResult? image = null;
        SpeechSynthesisResult? speech = null;
        SpeechDialogueSynthesisResult? dialogue = null;

        await steps.RunAsync(
            new TraconStepOptions { Name = "media", RunId = runId },
            async token =>
            {
                image = await provider.GetRequiredService<IImageGenerationService>()
                    .GenerateAsync(new TraconImageRequest { Prompt = "a lighthouse" }, token).ConfigureAwait(false);
                speech = await provider.GetRequiredService<ISpeechSynthesisService>()
                    .SynthesizeAsync(new SpeechRequest { Text = "1234567890" }, SpeechProvider, token).ConfigureAwait(false);
                dialogue = await provider.GetRequiredService<ISpeechSynthesisService>()
                    .SynthesizeDialogueAsync(
                        new SpeechDialogueRequest
                        {
                            Turns =
                            [
                                new SpeechDialogueTurn { Text = "12345", VoiceId = "a" },
                                new SpeechDialogueTurn { Text = "67890", VoiceId = "b" },
                            ],
                        },
                        SpeechProvider,
                        token).ConfigureAwait(false);
            },
            TestContext.Current.CancellationToken);

        image.ShouldNotBeNull().UsageRecorded.ShouldBeTrue();
        image!.Images.ShouldHaveSingleItem().MediaType.ShouldBe("image/png");
        image.Usage.Cost.ShouldBe(0.5m);
        speech.ShouldNotBeNull().UsageRecorded.ShouldBeTrue();
        speech!.Usage.Cost.ShouldBe(0.01m, "ten characters at 1000 per million");
        dialogue.ShouldNotBeNull().UsageRecorded.ShouldBeTrue();
        dialogue!.Usage.Quantity.ShouldBe(10m, "the characters of every turn");

        var items = await Runs(provider).ListNonTokenUsageAsync(runId, TestContext.Current.CancellationToken);

        items.Select(static item => item.Usage.Provider).ShouldBe([ImageProvider, "stub-voice", "stub-voice"]);

        var record = (await Runs(provider).GetRunAsync(runId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        record.NonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.52m);
        record.NonTokenCost!.Currency.ShouldBe("USD");
    }

    [Fact]
    public async Task Outside_a_run_a_media_call_returns_its_measurement_and_records_nothing()
    {
        await using var provider = Build();

        var image = await provider.GetRequiredService<IImageGenerationService>()
            .GenerateAsync(new TraconImageRequest { Prompt = "a kite" }, TestContext.Current.CancellationToken);
        var speech = await provider.GetRequiredService<ISpeechSynthesisService>()
            .SynthesizeAsync(new SpeechRequest { Text = "hello" }, SpeechProvider, TestContext.Current.CancellationToken);

        image.UsageRecorded.ShouldBeFalse();
        image.Usage.Cost.ShouldBe(0.5m);
        speech.UsageRecorded.ShouldBeFalse();
        speech.Usage.Quantity.ShouldBe(5m);

        (await Runs(provider).QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_blank_text_a_text_over_the_limit_and_a_dialogue_without_turns_are_refused_before_the_provider_is_called()
    {
        var speech = new StubSpeech();
        await using var provider = Build(speech: speech);
        var service = provider.GetRequiredService<ISpeechSynthesisService>();

        await Should.ThrowAsync<ArgumentException>(
            async () => await service.SynthesizeAsync(new SpeechRequest { Text = " " }, SpeechProvider, TestContext.Current.CancellationToken));

        var over = await Should.ThrowAsync<TraconException>(
            async () => await service.SynthesizeAsync(
                new SpeechRequest { Text = new string('a', 5001) },
                SpeechProvider,
                TestContext.Current.CancellationToken));

        over.Message.ShouldContain("5000", Case.Sensitive);

        await Should.ThrowAsync<ArgumentException>(
            async () => await service.SynthesizeDialogueAsync(
                new SpeechDialogueRequest { Turns = [] },
                SpeechProvider,
                TestContext.Current.CancellationToken));

        speech.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_store_that_cannot_write_an_item_neither_stops_the_media_call_nor_the_step()
    {
        var store = new FaultyRunStore(new InMemoryRunStore()) { FailNonTokenUsage = true };
        await using var provider = Build(services => services.AddSingleton<IRunStore>(store));
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();

        var image = await steps.RunAsync(
            new TraconStepOptions { Name = "unwritable", RunId = runId },
            async token => await provider.GetRequiredService<IImageGenerationService>()
                .GenerateAsync(new TraconImageRequest { Prompt = "a cover" }, token).ConfigureAwait(false),
            TestContext.Current.CancellationToken);

        image.Images.ShouldHaveSingleItem();
        image.UsageRecorded.ShouldBeTrue("the amount is counted although the item could not be written");
    }

    [Fact]
    public async Task The_completion_webhook_of_a_step_carries_the_tokens_of_the_agent_runs_below_it()
    {
        var publisher = new CapturingPublisher();
        await using var provider = Build(services => services.AddSingleton<IWebhookPublisher>(publisher));
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var catalog = provider.GetRequiredService<IAgentCatalog>();
        var stepId = Guid.CreateVersion7();

        await steps.RunAsync(
            new TraconStepOptions { Name = "with-agent", RunId = stepId },
            async token =>
            {
                var agent = await catalog.ResolveAsync("writer", cancellationToken: token).ConfigureAwait(false);

                await agent!.RunAsync(
                    "write",
                    options: new TraconRunOptions { RunId = Guid.CreateVersion7(), ParentRunId = stepId },
                    cancellationToken: token).ConfigureAwait(false);

                await provider.GetRequiredService<IImageGenerationService>()
                    .GenerateAsync(new TraconImageRequest { Prompt = "a cover" }, token).ConfigureAwait(false);
            },
            TestContext.Current.CancellationToken);

        var run = publisher.Completed.ShouldHaveSingleItem("the step's own completion; the agent run below it is not a root");

        run.RunId.ShouldBe(stepId.ToString());
        run.Kind.ShouldBe(nameof(RunKind.Step));
        run.InputTokens.ShouldBe(20);
        run.OutputTokens.ShouldBe(10);
        run.NonTokenCost.ShouldBe(0.5m);
        run.Cost.ShouldBe(0.5m, "the amount the step reported; the model has no price here");
    }

    [Fact]
    public async Task A_root_step_adds_one_run_and_what_it_spent_to_the_quota_counter_of_its_own_name()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();

        await steps.RunAsync(
            new TraconStepOptions { Name = "counted-spend" },
            async token => await provider.GetRequiredService<IImageGenerationService>()
                .GenerateAsync(new TraconImageRequest { Prompt = "a cover" }, token).ConfigureAwait(false),
            TestContext.Current.CancellationToken);

        var counter = (await provider.GetRequiredService<IQuotaStore>().GetUsageAsync(
                new QuotaUsageQuery { TenantId = "default", AgentName = "counted-spend" },
                TestContext.Current.CancellationToken))
            .Single(static record => record.Period == QuotaPeriod.Daily);

        counter.Runs.ShouldBe(1);
        counter.Cost.ShouldBe(0.5m);
    }

    [Fact]
    public async Task A_step_a_tool_opens_in_the_run_it_names_as_its_parent_spends_against_that_runs_budget()
    {
        await using var provider = Build();
        var store = Runs(provider);
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var parentId = Guid.CreateVersion7();
        var budget = new AgentRunBudget();

        await store.StartRunAsync(
            new RunStartInfo
            {
                RunId = parentId,
                AgentName = "outer-agent",
                StartedAt = DateTimeOffset.UtcNow,
                TenantId = "default",
            },
            TestContext.Current.CancellationToken);

        TraconRunContext.SetCurrent(new AgentRunScope
        {
            RunId = parentId,
            RootRunId = parentId,
            AgentName = "outer-agent",
            TenantId = "default",
            Budget = budget,
        });

        try
        {
            await steps.RunAsync(
                new TraconStepOptions { Name = "media-in-a-tool", ParentRunId = parentId },
                async token => await provider.GetRequiredService<IImageGenerationService>()
                    .GenerateAsync(new TraconImageRequest { Prompt = "a cover" }, token).ConfigureAwait(false),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }

        budget.ConsumedCost.ShouldBe(0.5m, "the parent's ceiling sees what its tool's step spent");
    }

    [Fact]
    public async Task An_agent_run_of_another_tenant_that_names_a_step_as_its_parent_does_not_join_its_tree()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var catalog = provider.GetRequiredService<IAgentCatalog>();
        var stepId = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();

        await steps.RunAsync(
            new TraconStepOptions { Name = "tenant-a-step", RunId = stepId },
            async token =>
            {
                var agent = await catalog.ResolveAsync("writer", cancellationToken: token).ConfigureAwait(false);

                using (AmbientTenantScope.Begin("tenant-b"))
                {
                    await agent!.RunAsync(
                        "write",
                        options: new TraconRunOptions { RunId = otherId, ParentRunId = stepId },
                        cancellationToken: token).ConfigureAwait(false);
                }
            },
            TestContext.Current.CancellationToken);

        RunRecord other;

        using (AmbientTenantScope.Begin("tenant-b"))
        {
            other = (await Runs(provider).GetRunAsync(otherId, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        }

        other.Depth.ShouldBe(0, "a run of another tenant starts a tree of its own");

        var step = (await Runs(provider).GetRunAsync(stepId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        step.TreeUsage.ShouldBeNull("the other tenant's tokens are not in this tenant's tree");
    }

    [Fact]
    public async Task A_run_store_that_cannot_be_read_does_not_stop_a_step_that_names_its_ids()
    {
        // Observability does not break functionality: the step cannot be checked against the store, so it is not opened as a run,
        // and it runs.
        var store = new FaultyRunStore(new InMemoryRunStore()) { FailReads = true };
        await using var provider = Build(services => services.AddSingleton<IRunStore>(store));
        var steps = provider.GetRequiredService<ITraconStepRunner>();

        var named = await steps.RunAsync(
            new TraconStepOptions { Name = "named", RunId = Guid.CreateVersion7() },
            _ => ValueTask.FromResult("ran"),
            TestContext.Current.CancellationToken);

        var attached = await steps.RunAsync(
            new TraconStepOptions { Name = "attached", ParentRunId = Guid.CreateVersion7() },
            _ => ValueTask.FromResult("ran"),
            TestContext.Current.CancellationToken);

        named.ShouldBe("ran");
        attached.ShouldBe("ran");
        store.ReadsRefused.ShouldBeGreaterThan(0, "the store was asked and could not answer");
    }

    [Fact]
    public async Task A_run_id_another_tenant_holds_is_not_taken_over_and_the_step_runs_without_a_record()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var held = Guid.CreateVersion7();

        using (AmbientTenantScope.Begin("tenant-a"))
        {
            await steps.RunAsync(new TraconStepOptions { Name = "tenant-a-step", RunId = held }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);
        }

        var ran = false;

        using (AmbientTenantScope.Begin("tenant-b"))
        {
            await steps.RunAsync(
                new TraconStepOptions { Name = "tenant-b-step", RunId = held },
                _ =>
                {
                    ran = true;

                    return ValueTask.CompletedTask;
                },
                TestContext.Current.CancellationToken);
        }

        ran.ShouldBeTrue("the refusal of the store is the step's record, not the step's work");

        using (AmbientTenantScope.Begin("tenant-a"))
        {
            var kept = (await Runs(provider).GetRunAsync(held, TestContext.Current.CancellationToken)).ShouldNotBeNull();

            kept.AgentName.ShouldBe("tenant-a-step");
            kept.TenantId.ShouldBe("tenant-a");
            kept.Status.ShouldBe(RunStatus.Completed);
        }

        using (AmbientTenantScope.Begin("tenant-b"))
        {
            (await Runs(provider).GetRunAsync(held, TestContext.Current.CancellationToken)).ShouldBeNull("the row never became tenant B's");
        }
    }

    [Fact]
    public async Task A_step_under_a_parent_is_counted_in_the_tree_of_the_root_and_joins_its_name()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var rootId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();

        await steps.RunAsync(new TraconStepOptions { Name = "outer", RunId = rootId }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        await steps.RunAsync(
            new TraconStepOptions { Name = "inner", RunId = childId, ParentRunId = rootId },
            async token => await provider.GetRequiredService<IImageGenerationService>()
                .GenerateAsync(new TraconImageRequest { Prompt = "a cover" }, token).ConfigureAwait(false),
            TestContext.Current.CancellationToken);

        var child = (await Runs(provider).GetRunAsync(childId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        child.ParentRunId.ShouldBe(rootId);
        child.RootRunId.ShouldBe(rootId);
        child.Depth.ShouldBe(1);

        var root = (await Runs(provider).GetRunAsync(rootId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        root.TreeNonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.5m);
    }

    [Theory]
    [InlineData(RunStatus.Completed)]
    [InlineData(RunStatus.Failed)]
    [InlineData(RunStatus.Canceled)]
    public async Task A_parent_that_has_already_ended_is_accepted_and_the_cost_of_the_child_joins_the_total_of_its_tree(RunStatus parentEndedAs)
    {
        // The state of the parent is not asked: the host that voices the parts of a podcast in steps of their own, after the
        // step that planned them has ended, relies on the part joining the plan's tree.
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var rootId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();
        var parent = new TraconStepOptions { Name = "plan", RunId = rootId };

        switch (parentEndedAs)
        {
            case RunStatus.Completed:
                await steps.RunAsync(parent, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);
                break;

            case RunStatus.Failed:
                await Should.ThrowAsync<InvalidOperationException>(
                    async () => await steps.RunAsync(
                        parent,
                        _ => throw new InvalidOperationException("the plan failed"),
                        TestContext.Current.CancellationToken));
                break;

            default:
                // The caller cancels while the step runs: the run closes as canceled, as a cancelled caller closes any step.
                using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken))
                {
                    await Should.ThrowAsync<OperationCanceledException>(
                        async () => await steps.RunAsync(
                            parent,
                            async token =>
                            {
                                await cancellation.CancelAsync();
                                token.ThrowIfCancellationRequested();
                            },
                            cancellation.Token));
                }

                break;
        }

        var ended = (await Runs(provider).GetRunAsync(rootId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        ended.Status.ShouldBe(parentEndedAs, "the parent is over before the child starts");

        await steps.RunAsync(
            new TraconStepOptions { Name = "part", RunId = childId, ParentRunId = rootId },
            async token => await provider.GetRequiredService<IImageGenerationService>()
                .GenerateAsync(new TraconImageRequest { Prompt = "a cover" }, token).ConfigureAwait(false),
            TestContext.Current.CancellationToken);

        var child = (await Runs(provider).GetRunAsync(childId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        child.ParentRunId.ShouldBe(rootId);
        child.RootRunId.ShouldBe(rootId);
        child.Status.ShouldBe(RunStatus.Completed);

        var root = (await Runs(provider).GetRunAsync(rootId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        root.Status.ShouldBe(ended.Status, "a child joining the tree does not reopen or change the parent");
        root.TreeNonTokenCost.ShouldNotBeNull().Amount.ShouldBe(0.5m);
    }

    [Fact]
    public async Task A_parent_of_another_tenant_is_refused_before_anything_is_written()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var foreignId = Guid.CreateVersion7();
        var ran = false;

        using (AmbientTenantScope.Begin("tenant-b"))
        {
            await steps.RunAsync(new TraconStepOptions { Name = "theirs", RunId = foreignId }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);
        }

        var ownId = Guid.CreateVersion7();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await steps.RunAsync(
                new TraconStepOptions { Name = "mine", RunId = ownId, ParentRunId = foreignId },
                _ =>
                {
                    ran = true;

                    return ValueTask.CompletedTask;
                },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(foreignId.ToString(), Case.Sensitive);
        ran.ShouldBeFalse();
        (await Runs(provider).GetRunAsync(ownId, TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task An_id_a_run_already_has_is_refused_and_the_first_run_is_left_as_it_was()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();
        var ran = false;

        await steps.RunAsync(new TraconStepOptions { Name = "first", RunId = runId }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<TraconException>(
            async () => await steps.RunAsync(
                new TraconStepOptions { Name = "second", RunId = runId },
                _ =>
                {
                    ran = true;

                    return ValueTask.CompletedTask;
                },
                TestContext.Current.CancellationToken));

        ran.ShouldBeFalse();
        (await Runs(provider).GetRunAsync(runId, TestContext.Current.CancellationToken)).ShouldNotBeNull().AgentName.ShouldBe("first");
    }

    [Fact]
    public async Task Two_steps_that_name_one_id_at_once_are_told_apart_in_this_process()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = steps.RunAsync(
            new TraconStepOptions { Name = "first", RunId = runId },
            async _ =>
            {
                started.TrySetResult();
                await release.Task.ConfigureAwait(false);
            },
            TestContext.Current.CancellationToken).AsTask();

        await started.Task;

        await Should.ThrowAsync<TraconException>(
            async () => await steps.RunAsync(new TraconStepOptions { Name = "second", RunId = runId }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken));

        release.TrySetResult();
        await first;
    }

    [Fact]
    public async Task An_agent_run_started_in_a_step_with_the_step_as_its_parent_joins_its_tree()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var catalog = provider.GetRequiredService<IAgentCatalog>();
        var stepId = Guid.CreateVersion7();
        var childId = Guid.CreateVersion7();

        await steps.RunAsync(
            new TraconStepOptions { Name = "with-agent", RunId = stepId },
            async token =>
            {
                var agent = await catalog.ResolveAsync("writer", cancellationToken: token).ConfigureAwait(false);

                await agent!.RunAsync(
                    "write",
                    options: new TraconRunOptions { RunId = childId, ParentRunId = stepId },
                    cancellationToken: token).ConfigureAwait(false);
            },
            TestContext.Current.CancellationToken);

        var child = (await Runs(provider).GetRunAsync(childId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        child.Kind.ShouldBe(RunKind.Agent);
        child.ParentRunId.ShouldBe(stepId);
        child.RootRunId.ShouldBe(stepId);
        child.Depth.ShouldBe(1);

        var step = (await Runs(provider).GetRunAsync(stepId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        step.TreeUsage.ShouldNotBeNull().TotalTokens.ShouldBe(30);
    }

    [Fact]
    public async Task A_step_is_not_continued_and_not_replayed()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();

        await steps.RunAsync(new TraconStepOptions { Name = "once", RunId = runId }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        var record = (await Runs(provider).GetRunAsync(runId, TestContext.Current.CancellationToken)).ShouldNotBeNull();

        RunContinuationEligibility.Refusal(record).ShouldNotBeNull().ShouldContain("Step", Case.Sensitive);

        var replay = await provider.GetRequiredService<RunReplayService>()
            .PrepareAsync(runId, new RunReplayRequest(), TestContext.Current.CancellationToken);

        replay.Outcome.ShouldBe(RunReplayOutcome.NotSupported);
        replay.Detail.ShouldNotBeNull().ShouldContain("step run", Case.Sensitive);
    }

    [Fact]
    public async Task A_step_is_counted_by_the_statistics()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();

        await steps.RunAsync(new TraconStepOptions { Name = "counted" }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken);

        var statistics = await Runs(provider).GetStatisticsAsync(new RunStatisticsQuery(), TestContext.Current.CancellationToken);

        statistics.TotalRuns.ShouldBe(1);
    }

    [Fact]
    public async Task With_recording_off_the_step_runs_and_nothing_is_recorded()
    {
        await using var provider = Build(static services => services.Configure<TraconOptions>(static o => o.RunRecording.Enabled = false));
        var steps = provider.GetRequiredService<ITraconStepRunner>();

        var result = await steps.RunAsync(
            new TraconStepOptions { Name = "quiet" },
            _ => ValueTask.FromResult(TraconRunContext.Current is null),
            TestContext.Current.CancellationToken);

        result.ShouldBeTrue("no scope is opened when recording is off");
        (await Runs(provider).QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(" padded")]
    [InlineData("padded ")]
    [InlineData("line\nbreak")]
    public async Task A_name_that_is_no_name_is_refused(string name)
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();

        await Should.ThrowAsync<ArgumentException>(
            async () => await steps.RunAsync(new TraconStepOptions { Name = name }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_name_past_two_hundred_characters_is_refused()
    {
        await using var provider = Build();
        var steps = provider.GetRequiredService<ITraconStepRunner>();

        await Should.ThrowAsync<ArgumentException>(
            async () => await steps.RunAsync(new TraconStepOptions { Name = new string('a', 201) }, _ => ValueTask.CompletedTask, TestContext.Current.CancellationToken));
    }

    private static IRunStore Runs(IServiceProvider provider) => provider.GetRequiredService<IRunStore>();

    private sealed class CapturingPublisher : IWebhookPublisher
    {
        private readonly List<WebhookRunSummary> _completed = [];

        public IReadOnlyList<WebhookRunSummary> Completed
        {
            get
            {
                lock (_completed)
                {
                    return [.. _completed];
                }
            }
        }

        public ValueTask<int> PublishAsync(
            string tenantId,
            string eventType,
            WebhookEventPayload payload,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(eventType, WebhookEvents.RunCompleted, StringComparison.Ordinal) && payload.Run is { } run)
            {
                lock (_completed)
                {
                    _completed.Add(run);
                }
            }

            return new ValueTask<int>(1);
        }
    }

    private static ServiceProvider Build(Action<IServiceCollection>? configure = null, StubSpeech? speech = null)
    {
        var services = new ServiceCollection();

        var reply = new ChatResponse(new ChatMessage(ChatRole.Assistant, "written"))
        {
            Usage = new UsageDetails { InputTokenCount = 20, OutputTokenCount = 10, TotalTokenCount = 30 },
        };

        services.AddTracon()
            .AddModelProvider(new FakeModelProvider(new FakeChatClient(_ => reply)))
            .AddAgent(new AgentDefinition
            {
                Name = "writer",
                Instructions = "Write.",
                Model = new ModelBinding { Provider = "fake", Model = "fake-model" },
            })
            .AddSpeechSynthesizer(SpeechProvider, _ => speech ?? new StubSpeech())
            .ConfigureImages(static images =>
            {
                images.Provider = ImageProvider;
                images.Providers[ImageProvider] = new TraconImageProviderOptions { Model = "image-1" };
            });

        services.AddKeyedSingleton<IImageGenerator>(ImageProvider, new StubImages());
        services.Configure<TraconOptions>(static options =>
        {
            options.Pricing.Currency = "USD";
            options.Pricing.Images[ImageProvider] = new Dictionary<string, ImagePriceOverride>(StringComparer.Ordinal)
            {
                ["image-1"] = new ImagePriceOverride { PerImage = 0.5m },
            };
            options.Pricing.Voice["stub-voice"] = new Dictionary<string, VoicePriceOverride>(StringComparer.Ordinal)
            {
                ["tts-1"] = new VoicePriceOverride { PerMillionCharacters = 1000m },
            };
        });
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed class StubImages : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

            return Task.FromResult(new ImageGenerationResponse([new DataContent(png, "image/png")]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StubSpeech : ISpeechSynthesizer, ISpeechDialogueSynthesizer
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public string ProviderName => "stub-voice";

        public int MaxCharactersPerRequest => 5000;

        public SpeechDialogueCapabilities DialogueCapabilities { get; } = new() { MaxVoices = 10, MaxCharactersPerRequest = 2000 };

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(new SpeechAudio { Data = new byte[] { 1 }, MediaType = "audio/mpeg", ModelId = "tts-1" });
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            yield return new byte[] { 1 };
        }

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);

        public ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(SpeechDialogueRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return ValueTask.FromResult(new SpeechDialogueAudio
            {
                Audio = new SpeechAudio { Data = new byte[] { 1 }, MediaType = "audio/mpeg", ModelId = "tts-1" },
            });
        }
    }
}
