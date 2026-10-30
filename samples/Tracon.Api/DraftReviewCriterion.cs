using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Tracon;

namespace Tracon.Api;

// MAAI001: Microsoft Agent Framework marks its loop types "for evaluation
// purposes only"; a criterion written in code names them.
#pragma warning disable MAAI001

/// <summary>
/// A loop stop criterion created from the application's services: a reviewer
/// agent from the catalog approves the draft.
/// </summary>
/// <remarks>
/// <para>
/// Three things the declarative <c>aiJudge</c> criterion cannot carry, all
/// domain-neutral here: a check computed in code before the reviewer reads the
/// draft (a word count and a heading check), the reviewer's OWN verdict shape
/// (<c>{"decision": "pass" | "fail", "findings": [{"issue", "fix"}]}</c>), and
/// state for one loop run (the round number and the previous round's findings)
/// in <see cref="LoopContext.AdditionalProperties"/>.
/// </para>
/// <para>
/// The reviewer runs as a CHILD of the run being evaluated: it appears under
/// that run in the run tree, reserves a run from the same tree budget, and its
/// tokens are its own run's usage. One instance serves every run and every
/// tenant, so it keeps nothing of a run in its fields.
/// </para>
/// </remarks>
internal sealed class DraftReviewCriterion(IAgentCatalog catalog) : LoopEvaluator
{
    /// <summary>The kind a definition names: <c>harness.loop.criteria[].kind</c>.</summary>
    public const string Kind = "draft-reviewed";

    /// <summary>The reviewer agent this criterion asks.</summary>
    public const string ReviewerAgent = "draft-reviewer";

    private const string RoundKey = "draft-reviewed.round";
    private const string FindingsKey = "draft-reviewed.findings";

    public override async ValueTask<LoopEvaluation> EvaluateAsync(
        LoopContext context,
        CancellationToken cancellationToken = default)
    {
        // The LAST assistant message is the draft: a harness turn can write a plan or
        // an earlier version first, and LastResponse.Text joins every message of the turn.
        var draft = context.LastResponse?.Messages.LastOrDefault(static message => message.Role == ChatRole.Assistant)?.Text
            ?? string.Empty;

        // Computed in code, before the reviewer reads the draft.
        var words = draft.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var hasHeading = draft.TrimStart().StartsWith("# ", StringComparison.Ordinal);

        var round = (context.AdditionalProperties.TryGetValue(RoundKey, out var stored) && stored is int previous ? previous : 0) + 1;
        context.AdditionalProperties[RoundKey] = round;
        var previousFindings = context.AdditionalProperties.TryGetValue(FindingsKey, out var kept) ? kept as string : null;

        // The same checks a sub-agent call makes: the tree's depth limit and run budget.
        var scope = TraconRunContext.Current;

        if (scope?.Budget is { } budget && (scope.Depth + 1 > budget.MaxDepth || !budget.TryReserveRun()))
        {
            throw new InvalidOperationException("The run tree has no room left for the reviewer run.");
        }

        var reviewer = await catalog.ResolveAsync(ReviewerAgent, culture: null, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No agent named '{ReviewerAgent}' is registered.");

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
