using Tracon.Workflows.UnitTests.Fakes;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Workflows.UnitTests;

/// <summary>
/// <see cref="WorkflowNodeRetryPolicy"/>'s wrapping contract (Phase 87.5):
/// only a transient provider error is retried, cancellation is never
/// retried, and <c>MaxAttempts</c> bounds the loop.
/// </summary>
public sealed class WorkflowNodeRetryTests
{
    [Fact]
    public async Task A_transient_error_in_a_function_node_is_retried_and_costs_no_extra_super_step()
    {
        // Measured (Phase 87.5, Open Question 2): the retry loop runs INSIDE
        // the function node's own call, which Microsoft Agent Framework
        // invokes exactly once per routed message regardless of how many
        // attempts Tracon's wrapper makes internally - so a retried node
        // must produce the SAME super-step count as one that succeeded on
        // its first attempt. Measured here by running the identical
        // one-agent-one-function chain twice and comparing SuperStepStarted counts.
        var attempts = 0;

        var flaky = WorkflowNodeRetry.Wrap(
            (List<ChatMessage> messages, IWorkflowContext _, CancellationToken _) =>
            {
                attempts++;

                return attempts < 3
                    ? throw new TraconProviderUnavailableException("flaky-provider")
                    : new ValueTask<List<ChatMessage>>(messages);
            },
            new WorkflowNodeRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            TimeProvider.System);

        var flakyHost = new WorkflowTestHost("one");
        flakyHost.AddFunction("maybe-fails", flaky);

        await flakyHost.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "maybe-fails", Kind = WorkflowNodeKind.Function },
            ],
        });

        var flakyEvents = await Collect(flakyHost.CreateRunner(), "chain", "hello");

        attempts.ShouldBe(3);

        var flakyRoot = (await flakyHost.RunStore.QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 100 }))
            .Single(run => run.Kind == RunKind.Workflow);

        flakyRoot.Status.ShouldBe(RunStatus.Completed);

        // Baseline: the SAME chain shape, no failures at all.
        var baselineHost = new WorkflowTestHost("one");
        baselineHost.AddFunction<List<ChatMessage>, List<ChatMessage>>(
            "maybe-fails",
            static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

        await baselineHost.SaveAsync(new WorkflowDefinition
        {
            Name = "chain",
            Kind = WorkflowKind.Sequential,
            Nodes =
            [
                new WorkflowNodeReference { Name = "one", Kind = WorkflowNodeKind.Agent },
                new WorkflowNodeReference { Name = "maybe-fails", Kind = WorkflowNodeKind.Function },
            ],
        });

        var baselineEvents = await Collect(baselineHost.CreateRunner(), "chain", "hello");

        var flakySuperSteps = flakyEvents.Count(runEvent => runEvent.Type == RunEventType.SuperStepStarted);
        var baselineSuperSteps = baselineEvents.Count(runEvent => runEvent.Type == RunEventType.SuperStepStarted);

        flakySuperSteps.ShouldBe(baselineSuperSteps);
    }

    private static async Task<List<RunEvent>> Collect(WorkflowRunner runner, string name, string message)
    {
        var events = new List<RunEvent>();

        await foreach (var runEvent in runner.RunStreamingAsync(new WorkflowRunRequest { WorkflowName = name, Message = message }))
        {
            events.Add(runEvent);
        }

        return events;
    }

    [Fact]
    public async Task A_transient_error_is_retried_until_it_succeeds()
    {
        var attempts = 0;

        Func<string, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (input, _, _) =>
            {
                attempts++;

                return attempts < 3
                    ? throw new TraconProviderUnavailableException("provider-x")
                    : ValueTask.FromResult(input.ToUpperInvariant());
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            TimeProvider.System);

        var result = await wrapped("hello", null!, TestContext.Current.CancellationToken);

        result.ShouldBe("HELLO");
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task A_permanent_error_is_never_retried()
    {
        var attempts = 0;

        Func<string, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (_, _, _) =>
            {
                attempts++;
                throw new TraconException("bad configuration");
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.CompilationFailed),
            TimeProvider.System);

        await Should.ThrowAsync<TraconException>(
            () => wrapped("hello", null!, TestContext.Current.CancellationToken).AsTask());

        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task MaxAttempts_bounds_a_persistently_transient_error()
    {
        var attempts = 0;

        Func<string, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (_, _, _) =>
            {
                attempts++;
                throw new TraconProviderUnavailableException("provider-x");
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            TimeProvider.System);

        await Should.ThrowAsync<TraconProviderUnavailableException>(
            () => wrapped("hello", null!, TestContext.Current.CancellationToken).AsTask());

        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task A_real_cancellation_is_never_retried_even_if_the_classifier_would_call_it_transient()
    {
        var attempts = 0;

        Func<string, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (_, _, _) =>
            {
                attempts++;
                throw new OperationCanceledException();
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            TimeProvider.System);

        // 🚨 Phase 157 (K-737): the token is ACTUALLY cancelled. It used to be
        // enough for the exception to be an OperationCanceledException, and
        // that is what made this policy useless for the one failure it lists as
        // transient - HttpClient reports its own request timeout as a
        // TaskCanceledException, so "any cancellation type stops the retry"
        // stopped every provider timeout too. Cancellation is now read from the
        // token; the sibling test below is the other half of that split.
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => wrapped("hello", null!, cancelled.Token).AsTask());

        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_provider_timeout_is_retried_even_though_its_type_is_a_cancellation()
    {
        var attempts = 0;

        Func<string, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (_, _, _) =>
            {
                attempts++;

                // Exactly what HttpClient raises when ITS OWN deadline elapses.
                throw new TaskCanceledException(
                    "The request to https://api.example/v1 timed out after 30s.",
                    new TimeoutException("The request timed out."));
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.Timeout),
            TimeProvider.System);

        await Should.ThrowAsync<TaskCanceledException>(
            () => wrapped("hello", null!, TestContext.Current.CancellationToken).AsTask());

        // Timeout is in the policy's transient set; nothing was cancelled, so
        // the node gets its configured attempts.
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task MaxAttempts_of_one_means_no_retry_at_all()
    {
        var attempts = 0;

        Func<string, Microsoft.Agents.AI.Workflows.IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (_, _, _) =>
            {
                attempts++;
                throw new TraconProviderUnavailableException("provider-x");
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 1 },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            TimeProvider.System);

        await Should.ThrowAsync<TraconProviderUnavailableException>(
            () => wrapped("hello", null!, TestContext.Current.CancellationToken).AsTask());

        attempts.ShouldBe(1);
    }

    private sealed class FixedClassifier(RunErrorClass value) : IRunErrorClassifier
    {
        public RunErrorClassification Classify(RunError runError) => new() { Class = value, Fingerprint = "fixed" };
    }
}
