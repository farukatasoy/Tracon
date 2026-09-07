using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentPrism;

// MAAI001: Microsoft.Agents.AI's loop API is marked "evaluation purposes only".
// The suppression covers the whole file because the file exists to translate
// AgentPrism's declarative settings into that API. Rationale: docs/KARARLAR.md,
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
/// <c>IAgentPrismBuilder.AddLoopEvaluator(...)</c>; if it is not there either,
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
    /// <exception cref="AgentPrismException">The same kind name is registered more than once.</exception>
    public LoopEvaluatorRegistry(
        IEnumerable<AgentPrismLoopEvaluatorRegistration> registrations,
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
                throw new AgentPrismException(
                    $"The loop criterion kind '{registration.Kind}' is built in and cannot be replaced. " +
                    "Shadowing a built-in kind would make the same definition mean different things in " +
                    "two applications. Register the criterion under a name of your own.");
            }

            if (!_custom.TryAdd(registration.Kind, registration.Evaluator))
            {
                throw new AgentPrismException(
                    $"Multiple loop criteria named '{registration.Kind}' have been registered. " +
                    "Criterion kind names must be unique.");
            }
        }
    }

    /// <summary>
    /// Builds the loop the harness runs for <paramref name="definition"/>.
    /// </summary>
    /// <param name="definition">The definition. Its <c>Harness.Loop</c> must not be <see langword="null"/>.</param>
    /// <returns>The single evaluator handed to the harness, and the loop options.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="AgentPrismCompilationException">The loop settings are not usable.</exception>
    public (LoopEvaluator Evaluator, LoopAgentOptions Options) Build(AgentDefinition definition)
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

        var criteria = new List<LoopCriterionBinding>(loop.Criteria.Count);

        foreach (var criterion in loop.Criteria)
        {
            criteria.Add(new LoopCriterionBinding(criterion.Kind, BuildCriterion(definition, criterion)));
        }

        var evaluator = new RecordingLoopEvaluator(
            definition.Name,
            criteria,
            maxIterations,
            _loggerFactory?.CreateLogger<RecordingLoopEvaluator>() ?? (ILogger)NullLogger.Instance);

        var options = new LoopAgentOptions
        {
            MaxIterations = maxIterations,
            FreshContextPerIteration = loop.FreshContextPerIteration,

            // Set explicitly rather than left to the framework default (measured
            // false on MAF 1.20.0): the run record must carry every iteration's
            // messages, not only the last one, or the evidence of a loop is the
            // one thing the loop does not leave behind.
            NonStreamingReturnsLastResponseOnly = false,
        };

        return (evaluator, options);
    }

    private LoopEvaluator BuildCriterion(AgentDefinition definition, LoopCriterion criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion.Kind))
        {
            throw Invalid(definition, $"Every loop criterion must carry a '{nameof(LoopCriterion.Kind)}'.");
        }

        switch (criterion.Kind)
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
                return BuildJudgeCriterion(definition, criterion);

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
                    $"with 'IAgentPrismBuilder.AddLoopEvaluator(\"{criterion.Kind}\", ...)'.");
        }
    }

    private AIJudgeLoopEvaluator BuildJudgeCriterion(AgentDefinition definition, LoopCriterion criterion)
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
        if (_judgeOptions?.Model is not { } judgeModel)
        {
            throw Invalid(
                definition,
                $"The '{AiJudgeKind}' loop criterion needs a judge model, but none is configured. " +
                "Call 'AddModelRunJudge(options => options.Model = ...)' on the AgentPrism builder, " +
                "or use a criterion kind that does not call a model.");
        }

        IChatClient judgeClient;

        try
        {
            judgeClient = _models.CreateChatClient(judgeModel);
        }
        catch (AgentPrismException ex)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' could not build its '{AiJudgeKind}' loop criterion: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
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

    private static AgentPrismCompilationException Invalid(AgentDefinition definition, string detail)
        => new($"Agent '{definition.Name}' has invalid loop settings: {detail}")
        {
            AgentName = definition.Name,
        };
}
#pragma warning restore MAAI001
