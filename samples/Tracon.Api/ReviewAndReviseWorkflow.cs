using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Tracon.Api;

/// <summary>
/// The <c>review-and-revise</c> workflow: a writer agent, a code check of its draft, a human review with a bounded
/// revision loop, and three media nodes behind the approval. A graph built in code (phase 211).
/// </summary>
/// <remarks>
/// <para>
/// A pipeline that produces something a person approves (a script, a render) needs three things a stored chain cannot
/// give: a way back (a rejected draft returns to the writer), a human gate, and the capabilities of a function node on
/// every step. The graph is built with <see cref="WorkflowBuilder"/> and gets the capabilities from two helpers:
/// <c>GetWorkflowAgentStep</c> (the writer, with its own timeout) and <c>GetWorkflowFunction</c> (the check and the media
/// nodes: time limit, retry, budget gate, a cost line that names the node, strict settings).
/// </para>
/// <para>
/// 🚨 <strong>Three traps this graph is built around.</strong> (1) The writer is an agent STEP, not an agent binding:
/// a binding that is not the graph's entry never runs when a message returns to it over a back edge, and the run ends with
/// no output and no error. (2) The revision counter lives in the workflow's STATE, not in a field: every answer is a new
/// run and the graph is built again, so a field starts from zero each time. (3) The request is a <see cref="string"/>:
/// the console shows its text, and a record would show its type name.
/// </para>
/// <para>
/// The run's parameters are the production's identity (<c>productionId</c>) and the script's language; the nodes load
/// what they need by that id and carry no payload. A rejected draft goes back <see cref="MaxRevisions"/> times, then the
/// run fails with the error type <see cref="ContentRejectedException.ErrorTypeName"/> and the media nodes never run.
/// </para>
/// </remarks>
internal static class ReviewAndReviseWorkflow
{
    /// <summary>The workflow's name.</summary>
    public const string Name = "review-and-revise";

    /// <summary>The writer agent.</summary>
    public const string Writer = "episode-writer";

    /// <summary>How many times a draft may be sent back before the run fails.</summary>
    public const int MaxRevisions = 2;

    /// <summary>The longest script the check accepts, in characters.</summary>
    public const int MaxScriptLength = 400;

    // The text the agent is bound with. {{language}} is the agent's own parameter, so the placeholder and the JSON braces are
    // written doubled inside this interpolated string, and the length limit is the check's own constant.
    private static readonly string WriterInstructions = string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"Write a short podcast episode script in the language {{{{language}}}}. The script must be shorter than {MaxScriptLength} " +
        $"characters. Answer ONLY with a JSON object: {{\"title\": string, \"script\": string}}. When the message asks you to " +
        $"revise a draft, keep what is good and apply the feedback.");

    private const string Port = "episode-review";
    private const string StateScope = "review";

    /// <summary>Registers the writer, the functions and the workflow.</summary>
    /// <param name="tracon">The Tracon chain.</param>
    /// <param name="writerModel">The model the writer runs on.</param>
    /// <param name="requestTimeout">How long a review may wait for its answer.</param>
    /// <param name="runTimeout">How long one run may take.</param>
    /// <param name="renderSeconds">How long the pretend render takes; long enough to stop the process in the middle of it.</param>
    /// <returns>The chain.</returns>
    public static ITraconBuilder AddReviewAndRevise(
        this ITraconBuilder tracon,
        ModelBinding writerModel,
        TimeSpan requestTimeout,
        TimeSpan runTimeout,
        int renderSeconds)
    {
        tracon.AddAgent(new AgentDefinition
        {
            Name = Writer,
            DisplayName = "Episode Writer (review-and-revise demo)",
            Description = "Writes a short episode script as JSON.",
            Instructions = WriterInstructions,
            Parameters = [new AgentParameter { Name = "language", Kind = AgentParameterKind.Text, DefaultValue = "en" }],
            Model = writerModel,
        });

        // Validation is code: it reads the writer's JSON and refuses a script that is too long. A draft it refuses goes
        // back to the writer and is never shown to the reviewer.
        tracon.AddWorkflowFunction<List<ChatMessage>, ScriptCheck>(
            "episode-validate",
            static _ => async (messages, context, cancellationToken) =>
            {
                var answer = messages.LastOrDefault(static message => message.Role == ChatRole.Assistant)?.Text ?? string.Empty;
                var (title, script, problem) = Read(answer);

                // The draft the revision steps and the approval read back: a later super-step sees it.
                await context.QueueStateUpdateAsync("draft", script, StateScope, cancellationToken).ConfigureAwait(false);
                await context.QueueStateUpdateAsync("title", title, StateScope, cancellationToken).ConfigureAwait(false);

                return new ScriptCheck(script, problem ?? (script.Length > MaxScriptLength
                    ? $"The script is {script.Length} characters; the limit is {MaxScriptLength}."
                    : null));
            },
            static options => options.Description = "Reads the writer's JSON and refuses a script over the length limit.");

        // The three media nodes are fakes that behave like the real ones: each reports what it spent beyond tokens, honours
        // its token, and carries the limits a costly node needs. tts runs one call at a time, render two.
        tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, SpeakSettings>(
            "episode-tts",
            static provider =>
            {
                var rates = TonePricing.From(provider);

                return async (messages, settings, context, cancellationToken) =>
                {
                    var text = LastText(messages);

                    await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
                    await context.ReportUsageAsync(rates.Characters(text.Length, isEstimated: true)).ConfigureAwait(false);

                    return [new ChatMessage(ChatRole.User, $"{text}\n\n[spoken by {settings.Voice}]")];
                };
            },
            static options =>
            {
                options.Description = "Pretends to read the script aloud: reports the characters as an estimated, priced item.";
                options.Timeout = TimeSpan.FromMinutes(2);
                options.MaxConcurrency = 1;
            });

        tracon.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>, RenderSettings>(
            "episode-render",
            static _ => async (messages, settings, context, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.Seconds, 0, 3600)), cancellationToken).ConfigureAwait(false);
                await context.ReportUsageAsync(new ToolCallUsage
                {
                    Unit = "gpu-seconds",
                    Quantity = Math.Clamp(settings.Seconds, 0, 3600),
                    Provider = "local-gpu",
                }).ConfigureAwait(false);

                return [new ChatMessage(ChatRole.User, $"{LastText(messages)}\n\n[rendered in {settings.Seconds} s]")];
            },
            static options =>
            {
                options.Description = "Pretends to render the episode: waits the node's 'seconds' and reports them as an unpriced GPU item.";
                options.Timeout = TimeSpan.FromMinutes(5);
                options.MaxConcurrency = 2;
            });

        tracon.AddWorkflowFunction<List<ChatMessage>, string>(
            "episode-qa",
            static _ => (messages, context, _) =>
            {
                var values = context.GetRunParameters();

                return new ValueTask<string>(
                    $"Episode {values.GetValueOrDefault("productionId", "?")} ({values.GetValueOrDefault("language", "en")}) passed QA:\n{LastText(messages)}");
            },
            static options => options.Description = "Closes the production: names it by the run's parameters.");

        tracon.AddWorkflow(
            Name,
            services => Build(services, renderSeconds),
            options =>
            {
                options.Description = "A writer, a code check, a human review with a bounded revision loop, then tts, render and qa. Defined in code.";

                // One answer per review: a double click follows the run the first answer started.
                options.ResponseMode = WorkflowResponseMode.SingleAnswer;
                options.RequestTimeout = requestTimeout;

                // Shorter than the installation's ten minutes, and counted per run: a review that waits for a person
                // belongs to no run, so the time a reviewer takes is not in it.
                options.RunTimeout = runTimeout;

                // The production's identity, not its payload: the nodes load what they need by the id.
                options.Parameters =
                [
                    new AgentParameter { Name = "productionId", Kind = AgentParameterKind.Text, Required = true, Description = "The production the episode belongs to." },
                    new AgentParameter { Name = "language", Kind = AgentParameterKind.Text, DefaultValue = "en", Description = "The language of the script." },
                ];
            });

        return tracon;
    }

    private static Workflow Build(IServiceProvider services, int renderSeconds)
    {
        // The writer is a plain step: it runs whenever a message arrives, a back edge included. Its own timeout replaces
        // Tracon:AgentGraph:ChildDeadline (two minutes) for this step.
        var writer = services.GetWorkflowAgentStep(Name, Writer, static step => step.Timeout = TimeSpan.FromMinutes(3));
        var validate = services.GetWorkflowFunction("episode-validate");

        var ask = Step<ScriptCheck, string>("ask", static (check, _, _) => new ValueTask<string>(check.Text));
        var review = RequestPort.Create<string, EpisodeReview>(Port).BindAsExecutor(allowWrappedRequests: false);

        // Both ways back to the writer count against the same limit.
        var reviseAfterViolation = Step<ScriptCheck, List<ChatMessage>>(
            "revise-after-violation",
            static (check, context, cancellationToken) => ReviseAsync(check.Violation ?? string.Empty, context, cancellationToken));

        var reviseAfterRejection = Step<EpisodeReview, List<ChatMessage>>(
            "revise-after-rejection",
            static (decision, context, cancellationToken) => ReviseAsync(decision.Note ?? string.Empty, context, cancellationToken));

        var approved = Step<EpisodeReview, List<ChatMessage>>(
            "approved",
            static async (_, context, cancellationToken) =>
                [new ChatMessage(ChatRole.User, await context.ReadStateAsync<string>("draft", StateScope, cancellationToken).ConfigureAwait(false) ?? string.Empty)]);

        var tts = services.GetWorkflowFunction("episode-tts", static step => step.SetSettings(new SpeakSettings { Voice = "alloy" }));
        var render = services.GetWorkflowFunction("episode-render", step => step.SetSettings(new RenderSettings { Seconds = renderSeconds }));
        var qa = services.GetWorkflowFunction("episode-qa");

        return new WorkflowBuilder(writer)
            .AddEdge(writer, validate)
            .AddEdge<ScriptCheck>(validate, reviseAfterViolation, static check => check?.Violation is not null)
            .AddEdge<ScriptCheck>(validate, ask, static check => check is not null && check.Violation is null)
            .AddEdge(reviseAfterViolation, writer)
            .AddEdge(ask, review)
            .AddEdge<EpisodeReview>(review, reviseAfterRejection, static decision => decision is { Approved: false })
            .AddEdge<EpisodeReview>(review, approved, static decision => decision is { Approved: true })
            .AddEdge(reviseAfterRejection, writer)
            .AddEdge(approved, tts)
            .AddEdge(tts, render)
            .AddEdge(render, qa)
            .WithOutputFrom(qa)
            .WithName(Name)

            // The steps of this graph become spans of the run trace. Message sends and edge groups are left out: a
            // graph with a way back sends many messages, and a run keeps at most MaxSpansPerRun spans.
            .WithOpenTelemetry(static options =>
            {
                options.DisableMessageSend = true;
                options.DisableEdgeGroupProcess = true;
            })
            .Build();
    }

    /// <summary>Counts a revision, enforces the limit and builds the writer's next input.</summary>
    private static async ValueTask<List<ChatMessage>> ReviseAsync(string feedback, IWorkflowContext context, CancellationToken cancellationToken)
    {
        // The counter is read from the workflow's state: the run that answers is a new run on a graph built again, and the
        // state comes back from the checkpoint.
        var used = await context.ReadStateAsync<int>("revisions", StateScope, cancellationToken).ConfigureAwait(false);

        if (used >= MaxRevisions)
        {
            throw new ContentRejectedException(
                $"The script was sent back {used} times and is still not accepted; the limit is {MaxRevisions}.");
        }

        await context.QueueStateUpdateAsync("revisions", used + 1, StateScope, cancellationToken).ConfigureAwait(false);

        var draft = await context.ReadStateAsync<string>("draft", StateScope, cancellationToken).ConfigureAwait(false) ?? string.Empty;

        // The step has no session: the input carries the draft and the feedback itself.
        return [new ChatMessage(ChatRole.User, $"Revise the script. Previous draft: {draft}. Feedback: {feedback}")];
    }

    private static ExecutorBinding Step<TInput, TOutput>(
        string id,
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler)
        => new FunctionExecutor<TInput, TOutput>(id, handler).BindExecutor();

    private static string LastText(List<ChatMessage> messages)
        => messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text ?? string.Empty;

    private static (string Title, string Script, string? Problem) Read(string answer)
    {
        try
        {
            using var document = JsonDocument.Parse(answer);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("script", out var script) && script.ValueKind == JsonValueKind.String)
            {
                var title = root.TryGetProperty("title", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() ?? string.Empty : string.Empty;

                return (title, script.GetString() ?? string.Empty, null);
            }
        }
        catch (JsonException)
        {
            // Not JSON: reported below as a violation the writer is asked to fix.
        }

        return (string.Empty, answer, "The answer is not the JSON object {\"title\", \"script\"} that was asked for.");
    }
}

/// <summary>What the check found: the script, and what is wrong with it.</summary>
/// <param name="Text">The script.</param>
/// <param name="Violation">What is wrong with it; <see langword="null"/> when nothing is.</param>
internal sealed record ScriptCheck(string Text, string? Violation);

/// <summary>What the reviewer answers.</summary>
/// <param name="Approved">Whether the script is accepted.</param>
/// <param name="Note">What to change, when it is not.</param>
internal sealed record EpisodeReview(bool Approved, string? Note);

/// <summary>The error a script that was sent back too often ends the run with.</summary>
/// <param name="message">What happened.</param>
internal sealed class ContentRejectedException(string message) : TraconException(message)
{
    /// <summary>The stable error type name.</summary>
    public const string ErrorTypeName = "content_rejected";

    /// <inheritdoc />
    public override string ErrorType => ErrorTypeName;
}
