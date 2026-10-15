using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// The host and the graph of the code-graph revision tests (phase 211): a writer agent, a validation
/// function, a human review, a bounded revision loop and three media functions behind it.
/// </summary>
/// <remarks>
/// <para>
/// The graph is built in code with <c>GetWorkflowAgentStep</c> and <c>GetWorkflowFunction</c> and is
/// the shape a consumer writes for a human-approved production: the back edges (validation failure,
/// a rejected draft) lead to the writer, which is an agent step and needs no <c>TurnToken</c>.
/// </para>
/// <para>
/// The revision counter lives in the workflow's own state (<see cref="StateScope"/>), not in a field of an
/// executor: every answer is a new run and the graph is built again, so a field would start from zero
/// each time. Whether the state comes back in the run that answers is what these tests prove.
/// </para>
/// </remarks>
internal sealed class ReviewAndReviseHarness
{
    /// <summary>The workflow's name.</summary>
    public const string Workflow = "review-and-revise";

    /// <summary>The writer agent's name, also the id of its step.</summary>
    public const string WriterName = "writer";

    /// <summary>The model provider the writer runs on.</summary>
    public const string ProviderName = "writing";

    /// <summary>The model the writer runs on.</summary>
    public const string ModelName = "writer-model";

    /// <summary>The port the human review waits on.</summary>
    public const string Port = "review";

    /// <summary>The scope of the workflow state the loop keeps its numbers in.</summary>
    public const string StateScope = "review";

    /// <summary>The marker that makes the validation function refuse a draft.</summary>
    public const string ViolationMarker = "[violation]";

    private readonly List<string> _media = [];

    /// <summary>Initializes the harness.</summary>
    /// <param name="drafts">What the writer answers, in order.</param>
    public ReviewAndReviseHarness(params string[] drafts)
    {
        Provider = new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName, ContextWindowTokens = 8_192, MaxOutputTokens = 1_024 })
            .RespondsWith(drafts);
    }

    /// <summary>Gets the provider the writer runs on; its requests show what the writer was given.</summary>
    public FakeModelProvider Provider { get; }

    /// <summary>Gets how many times the revision loop may send a draft back before the run fails.</summary>
    public int MaxRevisions { get; init; } = 2;

    /// <summary>
    /// Gets or sets whether the graph carries one more node between the approval and the media nodes. The graph is built
    /// for every run, so a test can change the structure while a run waits.
    /// </summary>
    public bool ExtraNode { get; set; }

    /// <summary>Gets or sets the writer step's own timeout. It is not part of the step's identity.</summary>
    public TimeSpan WriterTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets the names of the media functions that ran, in order.</summary>
    public IReadOnlyList<string> Media
    {
        get
        {
            lock (_media)
            {
                return [.. _media];
            }
        }
    }

    /// <summary>The draft the validation function passes on, and the reason it refuses one.</summary>
    /// <param name="Text">The draft.</param>
    /// <param name="Violation">What is wrong with it; <see langword="null"/> when nothing is.</param>
    public sealed record ReviewDraft(string Text, string? Violation);

    /// <summary>What the reviewer answers.</summary>
    /// <param name="Approved">Whether the draft is accepted.</param>
    /// <param name="Note">What to change, when it is not.</param>
    public sealed record ReviewDecision(bool Approved, string? Note);

    /// <summary>The error a draft that was rejected too often ends the run with.</summary>
    public sealed class ContentRejectedException(string message) : TraconException(message)
    {
        /// <summary>The stable error type name.</summary>
        public const string ErrorTypeName = "content_rejected";

        /// <inheritdoc />
        public override string ErrorType => ErrorTypeName;
    }

    /// <summary>Starts a host with the graph registered.</summary>
    /// <param name="configureTracon">More Tracon configuration.</param>
    /// <param name="configureServices">More service registrations, made before Tracon's own.</param>
    /// <param name="runWorker">Whether the host's own worker leases queued jobs.</param>
    /// <returns>The host.</returns>
    public Task<TraconTestHost> StartAsync(
        Action<ITraconBuilder>? configureTracon = null,
        Action<IServiceCollection>? configureServices = null,
        bool runWorker = true)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseWorkflows(options => options.RequestExpirationScanInterval = TimeSpan.FromMilliseconds(50));
                builder.AddModelProvider(Provider);

                builder.AddAgent(new AgentDefinition
                {
                    Name = WriterName,
                    Description = "Writes the script.",
                    Instructions = "Write the script.",
                    Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
                    Origin = AgentDefinitionOrigin.Code,
                });

                builder.AddWorkflowFunction<List<ChatMessage>, ReviewDraft>("validate", _ => ValidateAsync);

                foreach (var name in new[] { "tts", "render", "qa" })
                {
                    builder.AddWorkflowFunction<List<ChatMessage>, List<ChatMessage>>(name, _ => MediaAsync(name));
                }

                builder.AddWorkflow(
                    Workflow,
                    Build,
                    static options =>
                    {
                        options.ResponseMode = WorkflowResponseMode.SingleAnswer;
                        options.RequestTimeout = TimeSpan.FromMinutes(30);
                    });

                configureTracon?.Invoke(builder);
            },
            configureServices: services =>
            {
                services.Configure<TraconSchedulingOptions>(options =>
                {
                    options.PollInterval = TimeSpan.FromMilliseconds(50);
                    options.RunWorker = runWorker;
                });

                configureServices?.Invoke(services);
            });

    /// <summary>Builds the graph; the runner calls this for every run.</summary>
    /// <param name="services">The service provider.</param>
    /// <returns>The graph.</returns>
    public Workflow Build(IServiceProvider services)
    {
        var writer = services.GetWorkflowAgentStep(Workflow, WriterName, step => step.Timeout = WriterTimeout);
        var validate = services.GetWorkflowFunction("validate");
        var ask = Step<ReviewDraft, string>("ask", static (draft, _, _) => new ValueTask<string>(draft.Text));
        var review = RequestPort.Create<string, ReviewDecision>(Port).BindAsExecutor(allowWrappedRequests: false);

        var reviseAfterViolation = Step<ReviewDraft, List<ChatMessage>>(
            "revise-after-violation",
            async (draft, context, token) => await ReviseAsync(draft.Violation ?? string.Empty, context, token));

        var reviseAfterRejection = Step<ReviewDecision, List<ChatMessage>>(
            "revise-after-rejection",
            async (decision, context, token) => await ReviseAsync(decision.Note ?? string.Empty, context, token));

        var approved = Step<ReviewDecision, List<ChatMessage>>(
            "approved",
            async (_, context, token) => [new ChatMessage(
                ChatRole.User,
                await context.ReadStateAsync<string>("draft", StateScope, token) ?? string.Empty)]);

        var tts = services.GetWorkflowFunction("tts");
        var render = services.GetWorkflowFunction("render");
        var qa = services.GetWorkflowFunction("qa");

        var extra = Step<List<ChatMessage>, List<ChatMessage>>("extra", static (messages, _, _) => new ValueTask<List<ChatMessage>>(messages));

        var builder = new WorkflowBuilder(writer)
            .AddEdge(writer, validate)
            .AddEdge<ReviewDraft>(validate, reviseAfterViolation, static draft => draft?.Violation is not null)
            .AddEdge<ReviewDraft>(validate, ask, static draft => draft is not null && draft.Violation is null)
            .AddEdge(ask, review)
            .AddEdge(reviseAfterViolation, writer)
            .AddEdge<ReviewDecision>(review, reviseAfterRejection, static decision => decision is { Approved: false })
            .AddEdge<ReviewDecision>(review, approved, static decision => decision is { Approved: true })
            .AddEdge(reviseAfterRejection, writer);

        builder = ExtraNode
            ? builder.AddEdge(approved, extra).AddEdge(extra, tts)
            : builder.AddEdge(approved, tts);

        return builder
            .AddEdge(tts, render)
            .AddEdge(render, qa)
            .WithOutputFrom(qa)
            .WithName(Workflow)
            .Build();
    }

    /// <summary>Runs the workflow over the streamed endpoint until it waits for its answer or ends.</summary>
    /// <param name="host">The host.</param>
    /// <param name="parameters">The run's parameters, or <see langword="null"/>.</param>
    /// <returns>The run and the frames of its stream.</returns>
    public static async Task<(Guid RunId, List<SseFrame> Frames)> StartRunAsync(
        TraconTestHost host,
        object? parameters = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tracon/api/workflows/{Workflow}/run")
        {
            Content = JsonContent.Create(new { message = "the brief", parameters }),
        };

        using var started = await host.Client.SendAsync(request);

        started.StatusCode.ShouldBe(HttpStatusCode.OK, await started.Content.ReadAsStringAsync());

        var frames = (await SseReader.ReadAllAsync(await started.Content.ReadAsStreamAsync())).ToList();

        return (WorkflowResponseHarness.RunIdOf(frames), frames);
    }

    /// <summary>Answers the pending review of a run on the streamed path.</summary>
    /// <param name="host">The host.</param>
    /// <param name="waitingRunId">The run that waits.</param>
    /// <param name="decision">The reviewer's answer.</param>
    /// <returns>The run that carries the answer and the frames of its stream.</returns>
    public static async Task<(Guid RunId, List<SseFrame> Frames)> AnswerAsync(
        TraconTestHost host,
        Guid waitingRunId,
        ReviewDecision decision)
    {
        var requestId = (await WorkflowResponseHarness.RequestsAsync(host, waitingRunId)).ShouldHaveSingleItem();

        using var response = await WorkflowResponseHarness.RespondAsync(
            host,
            waitingRunId,
            new { requestId, data = JsonSerializer.SerializeToElement(decision, JsonSerializerOptions.Web) });

        var frames = await WorkflowResponseHarness.FramesAsync(response);

        return (WorkflowResponseHarness.RunIdOf(frames), frames);
    }

    /// <summary>Reads the text of the review request a run waits on.</summary>
    /// <param name="host">The host.</param>
    /// <param name="runId">The waiting run.</param>
    /// <returns>The draft the request carries.</returns>
    public static async Task<string> PendingDraftAsync(TraconTestHost host, Guid runId)
    {
        using var listed = await host.Client.GetAsync(new Uri($"/tracon/api/workflows/runs/{runId}/requests", UriKind.Relative));
        var requests = await TraconTestHost.ReadJsonAsync(listed);

        return requests.EnumerateArray().Single().GetProperty("prompt").GetString() ?? string.Empty;
    }

    private static FunctionExecutor<TInput, TOutput> StepExecutor<TInput, TOutput>(
        string id,
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler)
        => new(id, handler);

    private static ExecutorBinding Step<TInput, TOutput>(
        string id,
        Func<TInput, IWorkflowContext, CancellationToken, ValueTask<TOutput>> handler)
        => StepExecutor(id, handler).BindExecutor();

    private ValueTask<ReviewDraft> ValidateAsync(List<ChatMessage> messages, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var text = messages.LastOrDefault(static message => message.Role == ChatRole.Assistant)?.Text ?? string.Empty;
        var violation = text.Contains(ViolationMarker, StringComparison.Ordinal)
            ? "the script is over the length limit"
            : null;

        return Remember(context, text, violation, cancellationToken);
    }

    private static async ValueTask<ReviewDraft> Remember(IWorkflowContext context, string text, string? violation, CancellationToken cancellationToken)
    {
        await context.QueueStateUpdateAsync("draft", text, StateScope, cancellationToken);

        return new ReviewDraft(text, violation);
    }

    private Func<List<ChatMessage>, IWorkflowContext, CancellationToken, ValueTask<List<ChatMessage>>> MediaAsync(string name)
        => (messages, _, _) =>
        {
            lock (_media)
            {
                _media.Add(name);
            }

            return new ValueTask<List<ChatMessage>>(messages);
        };

    private async ValueTask<List<ChatMessage>> ReviseAsync(string feedback, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var used = await context.ReadStateAsync<int>("revisions", StateScope, cancellationToken);

        if (used >= MaxRevisions)
        {
            throw new ContentRejectedException($"The script was rejected {used} times; the limit is {MaxRevisions}.");
        }

        await context.QueueStateUpdateAsync("revisions", used + 1, StateScope, cancellationToken);

        var draft = await context.ReadStateAsync<string>("draft", StateScope, cancellationToken) ?? string.Empty;

        return [new ChatMessage(ChatRole.User, $"Revise the script. Previous draft: {draft}. Feedback: {feedback}")];
    }
}
