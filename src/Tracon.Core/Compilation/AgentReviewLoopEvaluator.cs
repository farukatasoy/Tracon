using System.Globalization;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

// MAAI001: Microsoft.Agents.AI's loop API is marked "evaluation purposes only".
// The suppression covers the whole file because the file exists to implement
// that API. If MAF changes it, only this file and its three neighbours
// (LoopEvaluatorRegistry, RecordingLoopEvaluator, AgentDefinitionCompiler.Agents)
// need updating. Rationale: docs/KARARLAR.md, decision K-020.
#pragma warning disable MAAI001

/// <summary>
/// The built-in <c>agentReview</c> criterion: another agent of the catalog reads the draft and
/// approves it, or sends feedback back for the next iteration.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The reviewer is a child run, not a side call.</strong> It runs through
/// <see cref="ChildAgentInvoker"/> in <see cref="ChildRefusalMode.Fail"/> mode, the one door every
/// sub-agent call uses: the tenant check, the tree's depth, run, token and cost budget, the tenant's
/// quota, the wait limit with its hard cutoff, the version pin and the refusal of a tool that waits for
/// approval all apply, and the reviewer appears under the run being evaluated in the run tree. A refused
/// or failed call is a criterion failure, which <see cref="LoopSettings.OnCriterionError"/> decides.
/// </para>
/// <para>
/// <strong>The verdict is data the reviewer owns.</strong> The reviewer's last assistant message is a JSON
/// object, <c>{"approved": true|false, "feedback": "..."}</c>. The criterion cannot ask the model for that
/// shape at call time: a response format is a protected option of a run (<see cref="AgentOptionsGuard"/>),
/// so the shape lives in the reviewer's own definition. A message that is not that object is a criterion
/// failure; it is never read as approval.
/// </para>
/// <para>
/// The feedback goes to the model and is not recorded in the run event; the reviewer's whole answer is
/// in its own run. What the event does record is who reviewed: <see cref="LoopCriterionTrail"/>.
/// </para>
/// <para>
/// One instance serves every run and every tenant of the compiled agent, so it keeps nothing of a run
/// in its fields.
/// </para>
/// </remarks>
internal sealed class AgentReviewLoopEvaluator : LoopEvaluator
{
    /// <summary>The longest feedback, in characters, that goes back to the model. A longer one is cut and says so.</summary>
    internal const int FeedbackLimit = 4000;

    /// <summary>The feedback sent when the reviewer declined and wrote none.</summary>
    internal const string NoFeedback = "The reviewer did not approve the draft and gave no feedback. Revise the draft.";

    /// <summary>The note appended to a feedback that was cut at <see cref="FeedbackLimit"/>.</summary>
    internal const string TruncationNote = "\n[The reviewer's feedback was cut here.]";

    private readonly AIAgent _reviewer;
    private readonly string _reviewerName;

    /// <summary>Initializes the criterion.</summary>
    /// <param name="reviewer">The reviewer, as a sub-agent call: <see cref="ChildAgentInvoker"/> in production.</param>
    /// <param name="reviewerName">The reviewer agent's name, for the trail and the failure message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reviewer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="reviewerName"/> is empty.</exception>
    public AgentReviewLoopEvaluator(AIAgent reviewer, string reviewerName)
    {
        ArgumentNullException.ThrowIfNull(reviewer);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerName);

        _reviewer = reviewer;
        _reviewerName = reviewerName;
    }

    /// <inheritdoc />
    public override async ValueTask<LoopEvaluation> EvaluateAsync(
        LoopContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The LAST assistant message is the draft: a harness turn can write a plan or an earlier
        // version first, and LastResponse.Text joins every message of the turn.
        var draft = LastAssistantText(context.LastResponse) ?? string.Empty;
        var request = context.InitialMessages.LastOrDefault(static message => message.Role == ChatRole.User)?.Text
            ?? string.Empty;

        var report = new ChildRunReport();
        var trail = TraconRunContext.Current?.CriterionTrail;
        AgentResponse answer;

        try
        {
            answer = await _reviewer
                .RunAsync(BuildPrompt(request, context.Iteration, draft), session: null, report, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // Also when the call is refused, times out or fails: the trail names the run it opened, if any.
            trail?.RecordReviewer(new LoopReviewerTrail(_reviewerName, report.AgentVersion, report.RunId));
        }

        var (approved, feedback) = ReadVerdict(_reviewerName, LastAssistantText(answer));

        return approved
            ? LoopEvaluation.Stop()
            : LoopEvaluation.Continue(FeedbackFor(feedback));
    }

    /// <summary>Builds the one text the reviewer receives: the request, the iteration and the draft.</summary>
    /// <param name="request">The text of the last user message the loop started with.</param>
    /// <param name="iteration">The iteration that just finished.</param>
    /// <param name="draft">The iteration's last assistant message.</param>
    /// <returns>The text. It carries no instruction: the reviewer's own definition holds those.</returns>
    internal static string BuildPrompt(string request, int iteration, string draft)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"Request:\n{request}\n\nIteration: {iteration}\n\nDraft:\n{draft}");

    /// <summary>Reads the reviewer's verdict.</summary>
    /// <param name="reviewer">The reviewer's name, for the failure message.</param>
    /// <param name="text">The reviewer's last assistant message.</param>
    /// <returns>Whether the reviewer approved, and the feedback it gave, if any.</returns>
    /// <exception cref="TraconException">
    /// The text is not a JSON object with a boolean <c>approved</c> and, when present, a string
    /// <c>feedback</c>. The message names the defect and never repeats the text.
    /// </exception>
    internal static (bool Approved, string? Feedback) ReadVerdict(string reviewer, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Unreadable(reviewer, "its answer is empty");
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw Unreadable(reviewer, "its answer is not JSON");
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Unreadable(reviewer, "its answer is not a JSON object");
            }

            if (!root.TryGetProperty("approved", out var approved)
                || approved.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                throw Unreadable(reviewer, "'approved' is missing or is not a boolean");
            }

            string? feedback = null;

            if (root.TryGetProperty("feedback", out var written))
            {
                if (written.ValueKind == JsonValueKind.String)
                {
                    feedback = written.GetString();
                }
                else if (written.ValueKind != JsonValueKind.Null)
                {
                    throw Unreadable(reviewer, "'feedback' is not a string");
                }
            }

            return (approved.GetBoolean(), feedback);
        }
    }

    /// <summary>The feedback the writer's next iteration receives.</summary>
    /// <param name="feedback">What the reviewer wrote, if anything.</param>
    /// <returns>The feedback, a fixed sentence when there is none, cut at <see cref="FeedbackLimit"/> when it is longer.</returns>
    internal static string FeedbackFor(string? feedback)
    {
        if (string.IsNullOrWhiteSpace(feedback))
        {
            return NoFeedback;
        }

        var text = feedback.Trim();

        if (text.Length <= FeedbackLimit)
        {
            return text;
        }

        return string.Concat(text.AsSpan(0, SurrogateSafeText.PrefixLength(text, FeedbackLimit)), TruncationNote);
    }

    /// <summary>
    /// Says whether a response format makes a reviewer answer in the shape this criterion reads: a JSON
    /// schema with a boolean <c>approved</c> and a string <c>feedback</c>.
    /// </summary>
    /// <param name="format">The reviewer's response format.</param>
    /// <returns>
    /// <see langword="true"/> when the schema declares both properties. A reviewer without them may
    /// still answer correctly, so the validator only warns; at run time a message that is not the
    /// verdict is a criterion failure.
    /// </returns>
    internal static bool DeclaresVerdict(AgentResponseFormat? format)
    {
        if (format is not { Kind: AgentResponseFormatKind.JsonSchema, Schema: { ValueKind: JsonValueKind.Object } schema }
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        return HasType(properties, "approved", "boolean") && HasType(properties, "feedback", "string");
    }

    private static bool HasType(JsonElement properties, string name, string type)
    {
        if (!properties.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.Object
            || !property.TryGetProperty("type", out var declared))
        {
            return false;
        }

        // JSON Schema allows a list of types, for a nullable property.
        return declared.ValueKind switch
        {
            JsonValueKind.String => string.Equals(declared.GetString(), type, StringComparison.Ordinal),
            JsonValueKind.Array => declared.EnumerateArray().Any(
                entry => entry.ValueKind == JsonValueKind.String && string.Equals(entry.GetString(), type, StringComparison.Ordinal)),
            _ => false,
        };
    }

    private static string? LastAssistantText(AgentResponse? response)
        => response?.Messages.LastOrDefault(static message => message.Role == ChatRole.Assistant)?.Text;

    private static TraconException Unreadable(string reviewer, string reason)
        => new(
            $"Reviewer agent '{reviewer}' did not give a readable verdict: {reason}. The answer must be a JSON " +
            "object with a boolean 'approved' and, optionally, a string 'feedback'. Give the reviewer agent " +
            "a JSON schema response format with those two properties.");
}
#pragma warning restore MAAI001
