using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon;

// MAAI001: Microsoft.Agents.AI's loop API is marked "evaluation purposes only".
// The suppression covers the whole file because the file exists to translate
// Tracon's declarative settings into that API. Rationale: docs/KARARLAR.md,
// decision K-020.
#pragma warning disable MAAI001

/// <summary>
/// Turns a definition's declarative <see cref="LoopSettings"/> into the
/// Microsoft Agent Framework loop the harness runs.
/// </summary>
/// <remarks>
/// <para>
/// Four criterion kinds are built in and map directly onto MAF's own
/// evaluators: <c>completionMarker</c>, <c>todoCompletion</c>, <c>aiJudge</c>
/// and <c>backgroundTaskCompletion</c>. Every one of them takes DATA only. Any
/// other kind name is looked up among the criteria registered in code with
/// <c>ITraconBuilder.AddLoopEvaluator(...)</c>; if it is not there either,
/// compilation fails. It is never ignored — a stop criterion that is silently
/// dropped is a loop with no stop criterion, which is the one failure this
/// whole surface exists to prevent.
/// </para>
/// <para>
/// This is the same split <see cref="EvalCheckRegistry"/> uses for eval checks,
/// and for the same reason: a definition can arrive from the management API, so
/// it may name behavior but never define it.
/// </para>
/// </remarks>
internal sealed class LoopEvaluatorRegistry
{
    private const string CompletionMarkerKind = "completionMarker";
    private const string TodoCompletionKind = "todoCompletion";
    private const string AiJudgeKind = "aiJudge";
    private const string BackgroundTaskCompletionKind = "backgroundTaskCompletion";

    private static readonly string[] BuiltInKinds =
    [
        CompletionMarkerKind,
        TodoCompletionKind,
        AiJudgeKind,
        BackgroundTaskCompletionKind,
    ];

    /// <summary>
    /// The built-in spelling of <paramref name="kind"/>, or <paramref name="kind"/>
    /// itself when no built-in matches.
    /// </summary>
    /// <remarks>
    /// Registration rejects a built-in name case-INSENSITIVELY, so matching it
    /// case-SENSITIVELY here left names such as "aijudge" unusable from both
    /// ends: they could not be registered and they could not be resolved. One
    /// comparer on both sides closes that dead name space.
    /// </remarks>
    private static string Canonical(string kind)
        => Array.Find(BuiltInKinds, builtIn => string.Equals(builtIn, kind, StringComparison.OrdinalIgnoreCase))
           ?? kind;

    private readonly Dictionary<string, LoopEvaluator> _custom;
    private readonly IModelProviderRegistry _models;
    private readonly ModelRunJudgeOptions? _judgeOptions;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Creates a registry from the code-side registrations.</summary>
    /// <param name="registrations">The criteria registered with <c>AddLoopEvaluator</c>.</param>
    /// <param name="models">The provider registry the <c>aiJudge</c> criterion's model comes from.</param>
    /// <param name="judgeOptions">
    /// The judge binding configured with <c>AddModelRunJudge</c>. When
    /// <see langword="null"/> or unconfigured, an <c>aiJudge</c> criterion fails
    /// to compile instead of silently borrowing the agent's own model.
    /// </param>
    /// <param name="loggerFactory">The logger factory a failing criterion reports to.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">The same kind name is registered more than once.</exception>
    public LoopEvaluatorRegistry(
        IEnumerable<TraconLoopEvaluatorRegistration> registrations,
        IModelProviderRegistry models,
        ModelRunJudgeOptions? judgeOptions = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(models);

        _models = models;
        _judgeOptions = judgeOptions;
        _loggerFactory = loggerFactory;
        _custom = new Dictionary<string, LoopEvaluator>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            if (Array.Exists(BuiltInKinds, kind => string.Equals(kind, registration.Kind, StringComparison.OrdinalIgnoreCase)))
            {
                throw new TraconException(
                    $"The loop criterion kind '{registration.Kind}' is built in and cannot be replaced. " +
                    "Shadowing a built-in kind would make the same definition mean different things in " +
                    "two applications. Register the criterion under a name of your own.");
            }

            if (!_custom.TryAdd(registration.Kind, registration.Evaluator))
            {
                throw new TraconException(
                    $"Multiple loop criteria named '{registration.Kind}' have been registered. " +
                    "Criterion kind names must be unique.");
            }
        }
    }

    /// <summary>
    /// Builds the client an <c>aiJudge</c> criterion calls, after the current
    /// tenant's egress policy allows the judge's provider.
    /// </summary>
    /// <param name="definition">The definition being compiled.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The judge client, or <see langword="null"/> when the definition has no
    /// <c>aiJudge</c> criterion or no judge model is configured. <see cref="Build"/>
    /// reports the second case together with the definition's other loop errors.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconCompilationException">
    /// The judge's provider is not registered, or the tenant's egress policy forbids it.
    /// </exception>
    /// <remarks>
    /// The judge keeps the setup-time credential: it is the host's one judge
    /// model, the same one the run judge calls.
    /// </remarks>
    // K-613 (setup-time credential, egress policy applied) and K-706 (the
    // aiJudge criterion runs on the AddModelRunJudge binding).
    public async ValueTask<IChatClient?> CreateJudgeChatClientAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (JudgeModelFor(definition) is not { } judgeModel)
        {
            return null;
        }

        // 🚨 Built here, BEFORE the agent and through the tenant-aware member,
        // not inside Build: the egress lookup is an async store read. Built
        // with the synchronous CreateChatClient inside Build, the judge read
        // every iteration's answer on a provider the tenant had forbidden
        // (2026-10-11).
        try
        {
            return await _models.CreateSetupChatClientAsync(judgeModel, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException ex)
        {
            throw JudgeUnavailable(definition, ex);
        }
    }

    /// <summary>
    /// The synchronous counterpart of <see cref="CreateJudgeChatClientAsync"/>,
    /// for the synchronous compile path only: it checks no egress policy.
    /// </summary>
    /// <param name="definition">The definition being compiled.</param>
    /// <returns>See <see cref="CreateJudgeChatClientAsync"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconCompilationException">The judge's provider is not registered.</exception>
    public IChatClient? CreateJudgeChatClient(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (JudgeModelFor(definition) is not { } judgeModel)
        {
            return null;
        }

        try
        {
            return _models.CreateChatClient(judgeModel);
        }
        catch (TraconException ex)
        {
            throw JudgeUnavailable(definition, ex);
        }
    }

    /// <summary>
    /// Builds the loop the harness runs for <paramref name="definition"/>.
    /// </summary>
    /// <param name="definition">The definition. Its <c>Harness.Loop</c> must not be <see langword="null"/>.</param>
    /// <param name="judgeClient">
    /// The client every <c>aiJudge</c> criterion calls, built beforehand with
    /// <see cref="CreateJudgeChatClientAsync"/>. <see langword="null"/> when the
    /// definition has no such criterion.
    /// </param>
    /// <returns>The single evaluator handed to the harness, and the loop options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconCompilationException">The loop settings are not usable.</exception>
    public (LoopEvaluator Evaluator, LoopAgentOptions Options) Build(AgentDefinition definition, IChatClient? judgeClient)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var loop = definition.Harness?.Loop
            ?? throw new ArgumentException("The definition carries no loop settings.", nameof(definition));

        if (loop.Criteria.Count == 0)
        {
            throw Invalid(
                definition,
                $"{nameof(LoopSettings)}.{nameof(LoopSettings.Criteria)} is empty. A loop with no stop " +
                "criterion never decides that the work is finished; it only runs until the iteration " +
                "ceiling. Give it at least one criterion, or leave " +
                $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.Loop)} null to turn the loop off.");
        }

        var maxIterations = loop.MaxIterations ?? LoopSettings.DefaultMaxIterations;

        if (maxIterations <= 0)
        {
            throw Invalid(
                definition,
                $"{nameof(LoopSettings)}.{nameof(LoopSettings.MaxIterations)} must be greater than zero. " +
                $"Actual value: {loop.MaxIterations}.");
        }

        // AOT-safe generic form. A number that is not a member would otherwise act
        // like the default without a word, and the definition said something else.
        foreach (var (field, outcome) in new[]
                 {
                     (nameof(LoopSettings.OnExhausted), loop.OnExhausted),
                     (nameof(LoopSettings.OnCriterionError), loop.OnCriterionError),
                 })
        {
            if (!Enum.IsDefined(outcome))
            {
                throw Invalid(
                    definition,
                    $"{nameof(LoopSettings)}.{field} must be {nameof(LoopOutcome.ReturnLastResponse)} or " +
                    $"{nameof(LoopOutcome.FailRun)}. Actual value: {(int)outcome}.");
            }
        }

        var criteria = new List<LoopCriterionBinding>(loop.Criteria.Count);

        foreach (var criterion in loop.Criteria)
        {
            criteria.Add(new LoopCriterionBinding(criterion.Kind, BuildCriterion(definition, criterion, judgeClient)));
        }

        var evaluator = new RecordingLoopEvaluator(
            definition.Name,
            criteria,
            maxIterations,
            loop.OnExhausted,
            loop.OnCriterionError,
            _loggerFactory?.CreateLogger<RecordingLoopEvaluator>() ?? (ILogger)NullLogger.Instance);

        var options = new LoopAgentOptions
        {
            // Phase 192: the framework does not evaluate the turn that reaches its
            // ceiling. To fail the run there, that turn has to be evaluated, so the
            // framework gets one iteration more and RecordingLoopEvaluator decides at
            // the definition's ceiling: it stops or throws, and turn N+1 never runs.
            // ReturnLastResponse keeps the ceiling, the events and the cost as they were.
            MaxIterations = loop.OnExhausted == LoopOutcome.FailRun && maxIterations < int.MaxValue ? maxIterations + 1 : maxIterations,
            FreshContextPerIteration = loop.FreshContextPerIteration,

            // Set explicitly rather than left to the framework default (measured
            // false on MAF 1.20.0). The caller gets the FINAL iteration's answer;
            // the usage stays the sum of every iteration (F-317, user decision
            // 2026-10-12). Phase 192 set this to false so the run record would
            // carry every iteration, but a defect hid that the transcript then
            // also became the answer every text surface returns: the OpenAI-
            // compatible endpoints, MCP and a calling agent would have read the
            // drafts and the loop's feedback as the reply. The evidence of the
            // loop is its LoopIterationCompleted events, one per iteration.
            NonStreamingReturnsLastResponseOnly = true,
        };

        return (evaluator, options);
    }

    private LoopEvaluator BuildCriterion(AgentDefinition definition, LoopCriterion criterion, IChatClient? judgeClient)
    {
        if (string.IsNullOrWhiteSpace(criterion.Kind))
        {
            throw Invalid(definition, $"Every loop criterion must carry a '{nameof(LoopCriterion.Kind)}'.");
        }

        switch (Canonical(criterion.Kind))
        {
            case CompletionMarkerKind:
                if (string.IsNullOrWhiteSpace(criterion.Marker))
                {
                    throw Invalid(
                        definition,
                        $"The '{CompletionMarkerKind}' loop criterion requires " +
                        $"'{nameof(LoopCriterion.Marker)}'. Without it there is no text to look for and " +
                        "the criterion can never be satisfied.");
                }

                return new CompletionMarkerLoopEvaluator(criterion.Marker, new CompletionMarkerLoopEvaluatorOptions());

            case TodoCompletionKind:
                var todoOptions = new TodoCompletionLoopEvaluatorOptions();

                if (criterion.Modes.Count > 0)
                {
                    todoOptions.Modes = criterion.Modes;
                }

                return new TodoCompletionLoopEvaluator(todoOptions);

            case AiJudgeKind:
                return BuildJudgeCriterion(definition, criterion, judgeClient);

            case BackgroundTaskCompletionKind:
                return new BackgroundTaskCompletionLoopEvaluator(new BackgroundTaskCompletionLoopEvaluatorOptions());

            default:
                if (_custom.TryGetValue(criterion.Kind, out var custom))
                {
                    return custom;
                }

                throw Invalid(
                    definition,
                    $"Unknown loop criterion kind: '{criterion.Kind}'. Built-in kinds: " +
                    $"{string.Join(", ", BuiltInKinds)}. If this is a criterion of your own, register it " +
                    $"with 'ITraconBuilder.AddLoopEvaluator(\"{criterion.Kind}\", ...)'.");
        }
    }

    private AIJudgeLoopEvaluator BuildJudgeCriterion(AgentDefinition definition, LoopCriterion criterion, IChatClient? judgeClient)
    {
        if (criterion.JudgeCriteria.Count == 0)
        {
            throw Invalid(
                definition,
                $"The '{AiJudgeKind}' loop criterion requires at least one " +
                $"'{nameof(LoopCriterion.JudgeCriteria)}' entry. A judge with nothing to judge against " +
                "cannot decide that the work is finished.");
        }

        // The judge runs on the SEPARATELY configured judge binding, never on
        // the agent's own model: this criterion calls a model on every
        // iteration, and charging that to an expensive agent model multiplies
        // the loop's bill. It is also the same binding the run judge already
        // uses, so a host configures one judge model, not two.
        if (_judgeOptions?.Model is null)
        {
            throw Invalid(
                definition,
                $"The '{AiJudgeKind}' loop criterion needs a judge model, but none is configured. " +
                "Call 'AddModelRunJudge(options => options.Model = ...)' on the Tracon builder, " +
                "or use a criterion kind that does not call a model.");
        }

        if (judgeClient is null)
        {
            throw new InvalidOperationException(
                $"Agent '{definition.Name}' has an '{AiJudgeKind}' loop criterion, but its judge client was " +
                $"not built. Call {nameof(CreateJudgeChatClientAsync)} before {nameof(Build)}.");
        }

        var judgeOptions = new AIJudgeLoopEvaluatorOptions
        {
            Criteria = criterion.JudgeCriteria,
        };

        if (!string.IsNullOrWhiteSpace(criterion.JudgeInstructions))
        {
            judgeOptions.Instructions = criterion.JudgeInstructions;
        }

        return new AIJudgeLoopEvaluator(judgeClient, judgeOptions);
    }

    /// <summary>
    /// The judge binding, when the definition's loop has an <c>aiJudge</c>
    /// criterion; otherwise <see langword="null"/>.
    /// </summary>
    private ModelBinding? JudgeModelFor(AgentDefinition definition)
    {
        if (definition.Harness?.Loop is not { } loop)
        {
            return null;
        }

        foreach (var criterion in loop.Criteria)
        {
            if (string.Equals(Canonical(criterion.Kind), AiJudgeKind, StringComparison.Ordinal))
            {
                return _judgeOptions?.Model;
            }
        }

        return null;
    }

    private static TraconCompilationException JudgeUnavailable(AgentDefinition definition, TraconException inner)
        => new($"Agent '{definition.Name}' could not build its '{AiJudgeKind}' loop criterion: {inner.Message}", inner)
        {
            AgentName = definition.Name,
        };

    private static TraconCompilationException Invalid(AgentDefinition definition, string detail)
        => new($"Agent '{definition.Name}' has invalid loop settings: {detail}")
        {
            AgentName = definition.Name,
        };
}
#pragma warning restore MAAI001
