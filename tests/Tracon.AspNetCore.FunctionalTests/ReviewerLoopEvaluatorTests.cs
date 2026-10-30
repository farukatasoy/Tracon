using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Failure mode 18: the reviewer pattern a factory-registered criterion
/// enables carries what <c>aiJudge</c> cannot — the reviewer's own verdict schema,
/// an input computed in code before the reviewer reads the draft, and state kept
/// for one loop run in <c>LoopContext.AdditionalProperties</c>.
/// </summary>
/// <remarks>
/// A unit test cannot answer this: the reviewer is a second agent resolved from
/// the real catalog, the feedback has to reach the writer's NEXT model request
/// through MAF's loop, and the per-run state has to stay inside one run.
/// </remarks>
// MAAI001: the loop API is marked "evaluation purposes only"
#pragma warning disable MAAI001
public sealed class ReviewerLoopEvaluatorTests
{
    internal const string WriterProvider = "writing";
    internal const string WriterModel = "writer-model";
    internal const string ReviewerProvider = "reviewing";
    internal const string ReviewerModel = "reviewer-model";
    internal const string WriterName = "writer";
    internal const string ReviewerName = "reviewer";
    internal const string Kind = "reviewed";

    [Fact]
    public async Task The_reviewer_reads_the_code_check_returns_its_own_verdict_and_keeps_state_for_one_run()
    {
        var writer = new FakeModelProvider(WriterProvider);
        writer.ForModel(WriterModel, cfg => cfg.RespondsWith(
            "A draft without a heading.",
            "# Heading\nA draft with a heading.",
            "# Second run\nAlready fine."));

        var reviewer = new FakeModelProvider(ReviewerProvider);
        reviewer.ForModel(ReviewerModel, cfg => cfg.RespondsWith(
            Verdict("fail", ("Missing heading", "Start with a '# ' heading")),
            Verdict("pass"),
            Verdict("pass")));

        await using var host = await StartAsync(writer, reviewer);

        await RunAsync(host);

        // The writer's second turn carries the reviewer's findings as feedback.
        writer.Requests.Count.ShouldBe(2);
        Text(writer.Requests[1].Messages).ShouldContain("Missing heading -> Start with a '# ' heading");

        // The reviewer saw the code-computed check, and on round 2 the state kept
        // from round 1.
        reviewer.Requests.Count.ShouldBe(2);
        var first = Text(reviewer.Requests[0].Messages);
        first.ShouldContain("Round 1.");
        first.ShouldContain("hasHeading=False");
        first.ShouldContain("Previous findings: none.");

        var second = Text(reviewer.Requests[1].Messages);
        second.ShouldContain("Round 2.");
        second.ShouldContain("hasHeading=True");
        second.ShouldContain("Previous findings: Missing heading -> Start with a '# ' heading.");

        var runs = host.Services.GetRequiredService<IRunStore>();
        var root = (await runs.QueryRunsAsync(new RunQuery { AgentName = WriterName })).ShouldHaveSingleItem();
        root.Status.ShouldBe(RunStatus.Completed);

        var iterations = await IterationEventsAsync(runs, root.Id);
        iterations.Count.ShouldBe(2);
        iterations[0].GetProperty("continued").GetBoolean().ShouldBeTrue();
        iterations[0].GetProperty("continuedBy").GetString().ShouldBe(Kind);
        iterations[0].GetProperty("hasFeedback").GetBoolean().ShouldBeTrue();
        iterations[1].GetProperty("continued").GetBoolean().ShouldBeFalse();

        // A second run starts from round 1 again: the state belongs to ONE loop run,
        // although one criterion instance serves both.
        await RunAsync(host);

        reviewer.Requests.Count.ShouldBe(3);
        var third = Text(reviewer.Requests[2].Messages);
        third.ShouldContain("Round 1.");
        third.ShouldContain("Previous findings: none.");
    }

    internal static string Verdict(string decision, params (string Issue, string Fix)[] findings)
        => JsonSerializer.Serialize(new
        {
            decision,
            findings = findings.Select(static finding => new { issue = finding.Issue, fix = finding.Fix }).ToArray(),
        });

    internal static Task<TraconTestHost> StartAsync(
        FakeModelProvider writer,
        FakeModelProvider reviewer,
        ConcurrentBag<AgentRunScope>? scopes = null,
        int maxIterations = 3)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.AddModelProvider(writer);
                builder.AddModelProvider(reviewer);

                // The factory takes the agent catalog: the registry builds inside the
                // catalog's own construction chain, so calling it at setup would close
                // the catalog -> compiler -> catalog cycle (plan 201.1).
                builder.AddLoopEvaluator(
                    Kind,
                    services => new ReviewerCriterion(services.GetRequiredService<IAgentCatalog>(), ReviewerName, scopes),
                    "The reviewer agent approves the draft.");

                builder.AddAgent(new AgentDefinition
                {
                    Name = ReviewerName,
                    Description = "Reviews drafts and answers with a JSON verdict.",
                    Instructions = "Review the draft. Answer with {\"decision\":\"pass|fail\",\"findings\":[{\"issue\":\"\",\"fix\":\"\"}]}.",
                    Model = new ModelBinding { Provider = ReviewerProvider, Model = ReviewerModel },
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddAgent(new AgentDefinition
                {
                    Name = WriterName,
                    Description = "Writes a draft until the reviewer approves it.",
                    Instructions = "Write the draft.",
                    Model = new ModelBinding { Provider = WriterProvider, Model = WriterModel },
                    Harness = new HarnessSettings
                    {
                        Loop = new LoopSettings
                        {
                            Criteria = [new LoopCriterion { Kind = Kind }],
                            MaxIterations = maxIterations,
                        },
                    },
                    Origin = AgentDefinitionOrigin.Code,
                });
            });

    internal static async Task RunAsync(TraconTestHost host)
    {
        var agent = (await host.Services.GetRequiredService<IAgentCatalog>()
                .ResolveAsync(WriterName, culture: null, CancellationToken.None))
            .ShouldNotBeNull();
        var session = await agent.CreateSessionAsync();

        await agent
            .RunAsync([new ChatMessage(ChatRole.User, "Write the piece.")], session)
            .WaitAsync(TimeSpan.FromSeconds(30));
    }

    internal static async Task<List<JsonElement>> IterationEventsAsync(IRunStore runs, Guid runId)
    {
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

    private static string Text(IReadOnlyList<ChatMessage> messages)
        => string.Join("\n", messages.Select(static message => message.Text));
}

/// <summary>
/// The reviewer pattern of <c>AddLoopEvaluator</c>'s factory example, as a host
/// would write it: a check computed in code, a reviewer agent from the catalog run
/// as a CHILD of the run being evaluated, the reviewer's own JSON verdict, and
/// state for one loop run in <see cref="LoopContext.AdditionalProperties"/>.
/// </summary>
internal sealed class ReviewerCriterion(
    IAgentCatalog catalog,
    string reviewerName,
    ConcurrentBag<AgentRunScope>? scopes = null) : LoopEvaluator
{
    private const string RoundKey = "reviewed.round";
    private const string FindingsKey = "reviewed.findings";

    public override async ValueTask<LoopEvaluation> EvaluateAsync(LoopContext context, CancellationToken cancellationToken = default)
    {
        // The last assistant message is the draft; LastResponse.Text joins every message of the turn.
        var draft = context.LastResponse?.Messages.LastOrDefault(static message => message.Role == ChatRole.Assistant)?.Text
            ?? string.Empty;

        // Computed in code, before the reviewer reads the draft.
        var hasHeading = draft.StartsWith("# ", StringComparison.Ordinal);
        var words = draft.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        var round = (context.AdditionalProperties.TryGetValue(RoundKey, out var stored) && stored is int previousRound ? previousRound : 0) + 1;
        context.AdditionalProperties[RoundKey] = round;
        var previousFindings = context.AdditionalProperties.TryGetValue(FindingsKey, out var kept) ? kept as string : null;

        var scope = TraconRunContext.Current;

        if (scope is not null)
        {
            scopes?.Add(scope);
        }

        if (scope?.Budget is { } budget && (scope.Depth + 1 > budget.MaxDepth || !budget.TryReserveRun()))
        {
            throw new InvalidOperationException("The run tree has no room for the reviewer.");
        }

        var reviewer = await catalog.ResolveAsync(reviewerName, culture: null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No agent named '{reviewerName}'.");

        var options = scope is null
            ? null
            : new TraconRunOptions
            {
                ParentRunId = scope.RunId,
                RootRunId = scope.RootRunId,
                Depth = scope.Depth + 1,
                Budget = scope.Budget,
                SessionId = scope.SessionId,
            };

        var prompt = string.Create(
            CultureInfo.InvariantCulture,
            $"Round {round}. Code check: words={words}, hasHeading={hasHeading}. " +
            $"Previous findings: {previousFindings ?? "none"}.\n---\n{draft}");

        var review = await reviewer.RunAsync(prompt, session: null, options, cancellationToken).ConfigureAwait(false);

        using var verdict = JsonDocument.Parse(review.Text);
        var findings = string.Join(
            "; ",
            verdict.RootElement.GetProperty("findings").EnumerateArray()
                .Select(static finding => $"{finding.GetProperty("issue").GetString()} -> {finding.GetProperty("fix").GetString()}"));

        context.AdditionalProperties[FindingsKey] = findings;

        return string.Equals(verdict.RootElement.GetProperty("decision").GetString(), "pass", StringComparison.Ordinal)
            ? LoopEvaluation.Stop()
            : LoopEvaluation.Continue($"Reviewer findings (round {round}): {findings}");
    }
}
#pragma warning restore MAAI001
