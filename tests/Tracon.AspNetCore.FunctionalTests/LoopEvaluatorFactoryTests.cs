using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 201 (F-301): <c>AddLoopEvaluator(kind, factory)</c> through a real host —
/// the factory reaches the container, may take the agent catalog without a
/// dependency cycle, fails a save with the kind named and recovers, and a run
/// cancelled during its evaluation ends as cancelled.
/// </summary>
/// <remarks>
/// Each question crosses the DI boundary (the factory receives the real root
/// provider, and the compiler singleton is built inside the catalog's chain) or the
/// HTTP boundary (the save path); a registry unit test answers none of them.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only" — see K-020.
#pragma warning disable MAAI001
public sealed class LoopEvaluatorFactoryTests
{
    private const string ProviderName = "looping";
    private const string ModelName = "loop-model";
    private const string AgentName = "looper";

    [Fact]
    public async Task A_factory_criterion_reaches_an_application_service_and_runs_once()
    {
        var calls = 0;
        var provider = Provider("still drafting", "APPROVED", "APPROVED");

        await using var host = await StartAsync(
            provider,
            "approved",
            configureServices: static services => services.AddSingleton(new ApprovalMarker("APPROVED")),
            configure: builder => builder.AddLoopEvaluator(
                "approved",
                services =>
                {
                    Interlocked.Increment(ref calls);
                    return new MarkerCriterion(services.GetRequiredService<ApprovalMarker>());
                },
                "The answer carries the host's approval marker."));

        await RunAsync(host);
        await RunAsync(host);

        provider.Requests.Count.ShouldBe(3);
        calls.ShouldBe(1);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var first = (await runs.QueryRunsAsync(new RunQuery { Take = 10 })).MinBy(static run => run.StartedAt).ShouldNotBeNull();
        var iterations = await ReviewerLoopEvaluatorTests.IterationEventsAsync(runs, first.Id);

        iterations[0].GetProperty("continuedBy").GetString().ShouldBe("approved");
    }

    [Fact]
    public async Task A_factory_criterion_takes_the_catalog_and_its_reviewer_runs_as_a_child_of_the_evaluated_run()
    {
        // The reviewer factory takes IAgentCatalog. Called at setup, it would have
        // asked for the catalog while the catalog was being built.
        var writer = new FakeModelProvider(ReviewerLoopEvaluatorTests.WriterProvider);
        writer.ForModel(ReviewerLoopEvaluatorTests.WriterModel, cfg => cfg
            .RespondsWith("draft one", inputTokens: 6, outputTokens: 4)
            .RespondsWith("# draft two", inputTokens: 6, outputTokens: 4));

        var reviewer = new FakeModelProvider(ReviewerLoopEvaluatorTests.ReviewerProvider);
        reviewer.ForModel(ReviewerLoopEvaluatorTests.ReviewerModel, cfg => cfg
            .RespondsWith(ReviewerLoopEvaluatorTests.Verdict("fail", ("Missing heading", "Add one")), inputTokens: 3, outputTokens: 2)
            .RespondsWith(ReviewerLoopEvaluatorTests.Verdict("pass"), inputTokens: 3, outputTokens: 2));

        var scopes = new ConcurrentBag<AgentRunScope>();
        await using var host = await ReviewerLoopEvaluatorTests.StartAsync(writer, reviewer, scopes);

        await ReviewerLoopEvaluatorTests.RunAsync(host);

        var runs = await host.Services.GetRequiredService<IRunStore>()
            .QueryRunsAsync(new RunQuery { OnlyRootRuns = false, Take = 10 });
        var root = runs.Single(static run => string.Equals(run.AgentName, ReviewerLoopEvaluatorTests.WriterName, StringComparison.Ordinal));
        var reviews = runs.Where(static run => string.Equals(run.AgentName, ReviewerLoopEvaluatorTests.ReviewerName, StringComparison.Ordinal)).ToList();

        root.Status.ShouldBe(RunStatus.Completed);
        reviews.Count.ShouldBe(2);
        reviews.ShouldAllBe(review => review.ParentRunId == root.Id && review.RootRunId == root.Id && review.Depth == 1);

        // Measured, plan 201.1: the reviewer's tokens are its own run's usage; the
        // evaluated run keeps only the writer's two turns (the aiJudge side channel,
        // K-893, is not involved).
        root.Usage.ShouldNotBeNull().TotalTokens.ShouldBe(20);
        reviews.ShouldAllBe(static review => review.Usage!.TotalTokens == 5);

        // Measured, plan 201.1: the tree budget is the root's own instance. The
        // reviewer's reservations count as started runs because the criterion takes
        // them itself, and its tokens reach the same budget through its child run.
        var budget = scopes.Select(static scope => scope.Budget).Distinct().ShouldHaveSingleItem().ShouldNotBeNull();
        budget.StartedRuns.ShouldBe(2);
        budget.ConsumedTokens.ShouldBe(30);
    }

    [Fact]
    public async Task A_factory_that_throws_refuses_the_save_with_the_kind_named_and_the_next_save_passes()
    {
        var calls = 0;
        await using var host = await StartAsync(
            Provider("DONE"),
            "flaky",
            configure: builder => builder.AddLoopEvaluator(
                "flaky",
                _ => ++calls == 1
                    ? throw new InvalidOperationException("The rule service is not ready.")
                    : new DelegateLoopEvaluator(static (_, _) => new ValueTask<LoopEvaluation>(LoopEvaluation.Stop()))));

        var body = new
        {
            name = "uses-flaky",
            instructions = "Work.",
            model = new { provider = ProviderName, model = ModelName },
            harness = new { loop = new { criteria = new[] { new { kind = "flaky" } } } },
        };

        using (var refused = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents", UriKind.Relative), body))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

            var detail = (await TraconTestHost.ReadJsonAsync(refused)).GetProperty("detail").GetString();
            detail.ShouldNotBeNull().ShouldContain("'flaky'");
            detail.ShouldNotContain("not ready");
        }

        using (var saved = await host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents", UriKind.Relative), body))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.Created, await saved.Content.ReadAsStringAsync());
        }

        using (var run = await host.Client.PostAsJsonAsync(
                   new Uri("/tracon/api/agents/uses-flaky/run", UriKind.Relative),
                   new AgentRunRequest { Message = "go" }))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.OK, await run.Content.ReadAsStringAsync());
        }

        calls.ShouldBe(2);
    }

    [Fact]
    public async Task A_run_cancelled_while_a_factory_criterion_evaluates_ends_as_cancelled_not_as_a_failed_criterion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observedCancellation = false;

        await using var host = await StartAsync(
            Provider("draft"),
            "slow",
            configure: builder => builder.AddLoopEvaluator(
                "slow",
                _ => new DelegateLoopEvaluator(async (_, cancellationToken) =>
                {
                    entered.TrySetResult();

                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated
                    }
                    catch (OperationCanceledException)
                    {
                        observedCancellation = true;
                        throw;
                    }

                    return LoopEvaluation.Stop();
                })));

        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
                .ResolveAsync(AgentName, culture: null, CancellationToken.None))
            .ShouldNotBeNull();
        var session = await agent.CreateSessionAsync();

        using var cancellation = new CancellationTokenSource();
        var running = agent.RunAsync([new ChatMessage(ChatRole.User, "go")], session, cancellationToken: cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(30)));
        observedCancellation.ShouldBeTrue("the criterion received the run's own token");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery())).ShouldHaveSingleItem();
        root.Status.ShouldBe(RunStatus.Canceled);

        // The run's own cancellation is not a criterion failure: nothing names the
        // criterion as failed (K-840's filter in RecordingLoopEvaluator).
        (await ReviewerLoopEvaluatorTests.IterationEventsAsync(runs, root.Id))
            .ShouldAllBe(static payload => payload.GetProperty("failedCriterion").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    private static async Task RunAsync(TraconTestHost host)
    {
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
                .ResolveAsync(AgentName, culture: null, CancellationToken.None))
            .ShouldNotBeNull();
        var session = await agent.CreateSessionAsync();

        await agent.RunAsync([new ChatMessage(ChatRole.User, "go")], session).WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static FakeModelProvider Provider(params string[] responses)
    {
        var provider = new FakeModelProvider(ProviderName);
        provider.ForModel(ModelName, cfg => cfg.RespondsWith(responses));

        return provider;
    }

    /// <summary>Starts a host whose code agent's loop names <paramref name="kind"/> alone.</summary>
    private static Task<TraconTestHost> StartAsync(
        FakeModelProvider provider,
        string kind,
        Action<ITraconBuilder> configure,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(provider);
                configure(builder);

                builder.AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Description = "Works until its criterion agrees.",
                    Instructions = "Work.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    Harness = new HarnessSettings
                    {
                        Loop = new LoopSettings { Criteria = [new LoopCriterion { Kind = kind }], MaxIterations = 3 },
                    },
                    Origin = AgentDefinitionOrigin.Code,
                });
            },
            configureServices: configureServices);

    private sealed record ApprovalMarker(string Text);

    private sealed class MarkerCriterion(ApprovalMarker marker) : LoopEvaluator
    {
        public override ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
            => new(context.LastResponse?.Text?.Contains(marker.Text, StringComparison.Ordinal) == true
                ? LoopEvaluation.Stop()
                : LoopEvaluation.Continue($"Finish with {marker.Text}."));
    }
}
#pragma warning restore MAAI001
