using System.Net;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.Workflows.UnitTests.Fakes;

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

    /// <summary>
    /// F-407: a budget refusal never changes inside a run (cost and time only grow), so a
    /// retry cannot fix it. A consumer's classifier that files <c>run_budget_exceeded</c>
    /// under a transient class would otherwise make the node wait and ask <c>MaxAttempts</c>
    /// times for the same answer; the retry does not ask the classifier.
    /// </summary>
    [Fact]
    public async Task A_budget_refusal_is_never_retried_even_when_the_classifier_calls_it_transient()
    {
        var attempts = await CountAttemptsAsync(
            new TraconRunBudgetExceededException("The run tree's cost budget is exhausted."),
            new FixedClassifier(RunErrorClass.RateLimited));

        attempts.ShouldBe(1);
    }

    /// <summary>
    /// F-529: a tenant's spent quota is not given back between two attempts a few milliseconds apart,
    /// whatever class a registered classifier files it under.
    /// </summary>
    [Fact]
    public async Task A_tenant_quota_refusal_is_never_retried_even_when_the_classifier_calls_it_transient()
    {
        var attempts = await CountAttemptsAsync(
            new TraconTenantQuotaExceededException("The daily cost quota for the whole tenant has been exceeded."),
            new FixedClassifier(RunErrorClass.RateLimited));

        attempts.ShouldBe(1);
    }

    /// <summary>
    /// F-338: <see cref="RunErrorClass.ProviderError"/> also holds a permanent 4xx (K-817
    /// widened it on purpose), so the class alone cannot say whether a retry helps. A node
    /// that calls <c>EnsureSuccessStatusCode()</c> and gets a 401 used to run
    /// <c>MaxAttempts</c> times; a retried 409 on a non-idempotent call repeats its side
    /// effect. The status is read from <see cref="HttpRequestException.StatusCode"/>, because
    /// the message .NET writes for it carries no "HTTP nnn" text.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task A_permanent_http_status_is_never_retried(HttpStatusCode status)
    {
        var attempts = await CountAttemptsAsync(EnsureSuccessStatusCodeFailure(status), new DefaultRunErrorClassifier());

        attempts.ShouldBe(1);
    }

    /// <summary>
    /// F-338: the same status when only the message carries it — a provider failure that
    /// Tracon's chat client normalized to <c>upstream_error</c>, and an SDK exception that a
    /// node let escape without normalizing.
    /// </summary>
    [Theory]
    [InlineData("HTTP 401 (invalid_api_key)", true)]
    [InlineData("HTTP 404 (invalid_request_error: model_not_found)", true)]
    [InlineData("HTTP 400 (invalid_request_error)", false)]
    [InlineData("HTTP 409 (conflict)", false)]
    public async Task A_permanent_status_in_the_message_is_never_retried(string providerMessage, bool normalized)
    {
        var attempts = await CountAttemptsAsync(ProviderFailure(providerMessage, normalized), new DefaultRunErrorClassifier());

        attempts.ShouldBe(1);
    }

    [Fact]
    public async Task A_consumer_classifier_cannot_make_a_permanent_status_transient()
    {
        // The same rule as cancellation in the fallback chain: the status is a fact about
        // the request, so no classifier can turn it into a retry.
        var attempts = await CountAttemptsAsync(
            EnsureSuccessStatusCodeFailure(HttpStatusCode.Unauthorized),
            new FixedClassifier(RunErrorClass.ProviderError));

        attempts.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task A_transient_http_status_is_still_retried(HttpStatusCode status)
    {
        var attempts = await CountAttemptsAsync(EnsureSuccessStatusCodeFailure(status), new DefaultRunErrorClassifier());

        attempts.ShouldBe(3);
    }

    [Theory]
    [InlineData("HTTP 503 (service_unavailable)", true)]
    [InlineData("HTTP 429 (rate_limit_exceeded)", true)]
    [InlineData("Connection refused (127.0.0.1:1)", false)]
    public async Task A_transient_status_in_the_message_or_no_status_is_still_retried(string providerMessage, bool normalized)
    {
        var attempts = await CountAttemptsAsync(ProviderFailure(providerMessage, normalized), new DefaultRunErrorClassifier());

        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task A_connection_failure_with_no_status_is_still_retried()
    {
        var attempts = await CountAttemptsAsync(
            new HttpRequestException("Connection refused (127.0.0.1:1)"),
            new DefaultRunErrorClassifier());

        attempts.ShouldBe(3);
    }

    private static async Task<int> CountAttemptsAsync(Exception failure, IRunErrorClassifier classifier)
    {
        var attempts = 0;

        var wrapped = WorkflowNodeRetry.Wrap(
            (string _, IWorkflowContext _, CancellationToken _) =>
            {
                attempts++;

                return ValueTask.FromException<string>(failure);
            },
            new WorkflowNodeRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(1) },
            classifier,
            TimeProvider.System);

        var thrown = await Should.ThrowAsync<Exception>(
            () => wrapped("hello", null!, TestContext.Current.CancellationToken).AsTask());

        thrown.ShouldBeSameAs(failure);

        return attempts;
    }

    // Exactly what HttpResponseMessage.EnsureSuccessStatusCode() throws.
    private static HttpRequestException EnsureSuccessStatusCodeFailure(HttpStatusCode status)
        => new(
            $"Response status code does not indicate success: {(int)status} ({status}).",
            inner: null,
            status);

    private static Exception ProviderFailure(string providerMessage, bool normalized)
        => normalized
            ? ProviderInvocationException.UpstreamFailure(new InvalidOperationException(providerMessage), "openai")
            : new FakeClientResultException(providerMessage);

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

    [Fact]
    public async Task A_classifier_that_throws_hands_over_to_the_built_in_one()
    {
        // 🚨 IsTransient runs INSIDE an exception filter, and C# swallows an
        // exception thrown in a filter - the filter just evaluates to false.
        // A throwing classifier therefore meant the node was silently NOT
        // retried, with nothing logged, while the shipped store/classifier
        // guide promises the built-in classifier takes over and the failure is
        // logged. The built-in classifier reads
        // TraconProviderUnavailableException as ProviderUnavailable, which IS
        // retryable, so a working fallback shows up as a retry happening.
        var attempts = 0;

        Func<string, IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (_, _, _) =>
            {
                attempts++;

                return attempts < 3
                    ? throw new TraconProviderUnavailableException("provider-x")
                    : new ValueTask<string>("done");
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 5, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new ThrowingClassifier(),
            TimeProvider.System);

        var result = await wrapped("hello", null!, TestContext.Current.CancellationToken);

        result.ShouldBe("done");
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task Every_failed_attempt_that_is_retried_is_logged_with_its_error()
    {
        // F-527: a retried failure left no trace. When the last attempt then ended
        // in a different error (a spent budget refused the next attempt), the run
        // row held that error only, and the transient error that started the
        // retry was nowhere in the record.
        using var logs = new CapturingLoggerProvider();
        var failures = new List<TraconProviderUnavailableException>();

        Func<string, IWorkflowContext, CancellationToken, ValueTask<string>> handler =
            (_, _, _) =>
            {
                if (failures.Count < 2)
                {
                    var failure = new TraconProviderUnavailableException($"provider-x attempt {failures.Count + 1}");
                    failures.Add(failure);

                    throw failure;
                }

                return new ValueTask<string>("done");
            };

        var wrapped = WorkflowNodeRetry.Wrap(
            handler,
            new WorkflowNodeRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            TimeProvider.System,
            logs.CreateLogger("retry"));

        (await wrapped("hello", null!, TestContext.Current.CancellationToken)).ShouldBe("done");

        var warnings = logs.Entries.Where(static entry => entry.Level == LogLevel.Warning).ToList();

        warnings.Count.ShouldBe(2);
        warnings[0].Exception.ShouldBeSameAs(failures[0]);
        warnings[0].Message.ShouldContain("attempt 1 of 3");
        warnings[1].Exception.ShouldBeSameAs(failures[1]);
        warnings[1].Message.ShouldContain("attempt 2 of 3");
    }

    [Fact]
    public async Task The_last_failed_attempt_is_not_logged_because_it_is_thrown()
    {
        using var logs = new CapturingLoggerProvider();

        var wrapped = WorkflowNodeRetry.Wrap(
            (string _, IWorkflowContext _, CancellationToken _) =>
                ValueTask.FromException<string>(new TraconProviderUnavailableException("provider-x")),
            new WorkflowNodeRetryPolicy { MaxAttempts = 2, InitialDelay = TimeSpan.FromMilliseconds(1) },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            TimeProvider.System,
            logs.CreateLogger("retry"));

        await Should.ThrowAsync<TraconProviderUnavailableException>(
            () => wrapped("hello", null!, TestContext.Current.CancellationToken).AsTask());

        // Two attempts: the first is retried and logged; the second is the thrown one.
        logs.Entries.Count(static entry => entry.Level == LogLevel.Warning).ShouldBe(1);
    }

    /// <summary>
    /// F-280: the delay doubles on every attempt and used to meet the timer's ceiling
    /// unchecked; past about 49.7 days the next <c>Task.Delay</c> threw
    /// <see cref="ArgumentOutOfRangeException"/> in the middle of a run instead of
    /// retrying. The delay is now capped where a delay can still wait.
    /// </summary>
    [Fact]
    public async Task A_delay_that_outgrows_the_timer_is_capped_instead_of_throwing()
    {
        var time = new TriggerableTimeProvider();
        var attempts = 0;

        var handler = WorkflowNodeRetry.Wrap(
            (string input, IWorkflowContext _, CancellationToken _) =>
            {
                attempts++;

                return ValueTask.FromException<string>(new TraconProviderUnavailableException("still-down"));
            },
            new WorkflowNodeRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromDays(30), BackoffMultiplier = 2 },
            new FixedClassifier(RunErrorClass.ProviderUnavailable),
            time);

        var call = handler("x", null!, TestContext.Current.CancellationToken).AsTask();

        // Each probe fires the retry's pending delay; the call ends after the third attempt.
        await WaitUntil.TrueAsync(() =>
        {
            time.TriggerAll();

            return call.IsCompleted;
        });

        await Should.ThrowAsync<TraconProviderUnavailableException>(call);
        attempts.ShouldBe(3);
    }

    [Theory]
    [InlineData(-1.0, 2.0)]
    [InlineData(50 * 24 * 60 * 60.0, 2.0)]
    [InlineData(1.0, -1.0)]
    [InlineData(1.0, double.NaN)]
    [InlineData(1.0, double.PositiveInfinity)]
    public void A_policy_a_delay_cannot_follow_is_refused_when_the_node_is_registered(double initialSeconds, double multiplier)
    {
        var builder = new ServiceCollection().AddTracon();
        var policy = new WorkflowNodeRetryPolicy
        {
            MaxAttempts = 3,
            InitialDelay = TimeSpan.FromSeconds(initialSeconds),
            BackoffMultiplier = multiplier,
        };

        Should.Throw<ArgumentOutOfRangeException>(() => builder.AddWorkflowFunction<string, string>(
            "fragile",
            static _ => static (input, _, _) => ValueTask.FromResult(input),
            retryPolicy: policy));
    }

    private sealed class FixedClassifier(RunErrorClass value) : IRunErrorClassifier
    {
        public RunErrorClassification Classify(RunError runError) => new() { Class = value, Fingerprint = "fixed" };
    }

    private sealed class ThrowingClassifier : IRunErrorClassifier
    {
        public RunErrorClassification Classify(RunError runError)
            => throw new InvalidOperationException("the consumer's classifier has a bug");
    }

    /// <summary>Mimics <c>System.ClientModel.ClientResultException</c> BY NAME; the workflow tests cannot reference the real SDK type.</summary>
    private sealed class FakeClientResultException(string message) : Exception(message);
}
