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
/// <para>
/// <strong>One rule set, two readers.</strong> <see cref="FindProblems"/> is the
/// only place a loop is judged; <see cref="Build"/> refuses with its list and the
/// validation report prints it entry by entry, with a code and a path each.
/// </para>
/// </remarks>
internal sealed class LoopEvaluatorRegistry
{
    /// <summary>The validation code of an empty <see cref="LoopSettings.Criteria"/> list.</summary>
    internal const string CriteriaEmptyCode = "loop_criteria_empty";

    /// <summary>The validation code of a <see cref="LoopSettings.MaxIterations"/> that is not positive.</summary>
    internal const string MaxIterationsInvalidCode = "loop_max_iterations_invalid";

    /// <summary>The validation code of a <see cref="LoopOutcome"/> value that is not a member.</summary>
    internal const string OutcomeInvalidCode = "loop_outcome_invalid";

    /// <summary>The validation code of a criterion without a <see cref="LoopCriterion.Kind"/>.</summary>
    internal const string CriterionKindMissingCode = "loop_criterion_kind_missing";

    /// <summary>The validation code of a kind that is neither built in nor registered in code.</summary>
    internal const string CriterionUnknownCode = "loop_criterion_unknown";

    /// <summary>The validation code of a <c>completionMarker</c> criterion without a marker.</summary>
    internal const string MarkerMissingCode = "loop_marker_missing";

    /// <summary>The validation code of an <c>aiJudge</c> criterion without judge criteria.</summary>
    internal const string JudgeCriteriaMissingCode = "loop_judge_criteria_missing";

    /// <summary>The validation code of an <c>aiJudge</c> criterion on a host with no judge model.</summary>
    internal const string JudgeUnconfiguredCode = "loop_judge_unconfigured";

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

    private static string BuiltInDescription(string kind) => kind switch
    {
        CompletionMarkerKind => "Stops once the answer contains the marker text.",
        TodoCompletionKind => "Stops once the agent's todo list has no open item.",
        AiJudgeKind => "A judge model decides whether the answer meets the listed criteria. It calls the host's judge model on every iteration.",
        _ => "Stops once no background task is still running.",
    };

    private readonly Dictionary<string, CustomCriterion> _custom;
    private readonly IModelProviderRegistry _models;
    private readonly ModelRunJudgeOptions? _judgeOptions;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IServiceProvider _services;

    /// <summary>Creates a registry from the code-side registrations.</summary>
    /// <param name="registrations">The criteria registered with <c>AddLoopEvaluator</c>.</param>
    /// <param name="models">The provider registry the <c>aiJudge</c> criterion's model comes from.</param>
    /// <param name="judgeOptions">
    /// The judge binding configured with <c>AddModelRunJudge</c>. When
    /// <see langword="null"/> or unconfigured, an <c>aiJudge</c> criterion fails
    /// to compile instead of silently borrowing the agent's own model.
    /// </param>
    /// <param name="loggerFactory">The logger factory a failing criterion reports to.</param>
    /// <param name="services">
    /// The root service provider a registered criterion factory receives. When
    /// <see langword="null"/>, a factory receives a provider that resolves nothing.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The same kind name is registered more than once, or a registration shadows a
    /// built-in kind. Both are refused here, before any factory runs.
    /// </exception>
    public LoopEvaluatorRegistry(
        IEnumerable<TraconLoopEvaluatorRegistration> registrations,
        IModelProviderRegistry models,
        ModelRunJudgeOptions? judgeOptions = null,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(models);

        _models = models;
        _judgeOptions = judgeOptions;
        _loggerFactory = loggerFactory;
        _services = services ?? NoServices.Instance;
        _custom = new Dictionary<string, CustomCriterion>(StringComparer.OrdinalIgnoreCase);

        foreach (var registration in registrations)
        {
            if (Array.Exists(BuiltInKinds, kind => string.Equals(kind, registration.Kind, StringComparison.OrdinalIgnoreCase)))
            {
                throw new TraconException(
                    $"The loop criterion kind '{registration.Kind}' is built in and cannot be replaced. " +
                    "Shadowing a built-in kind would make the same definition mean different things in " +
                    "two applications. Register the criterion under a name of your own.");
            }

            if (!_custom.TryAdd(registration.Kind, new CustomCriterion(registration)))
            {
                throw new TraconException(
                    $"Multiple loop criteria named '{registration.Kind}' have been registered. " +
                    "Criterion kind names must be unique.");
            }
        }

        var kinds = new List<LoopCriterionKindInfo>(BuiltInKinds.Length + _custom.Count);

        foreach (var builtIn in BuiltInKinds)
        {
            kinds.Add(new LoopCriterionKindInfo(builtIn, BuiltIn: true, BuiltInDescription(builtIn)));
        }

        foreach (var custom in _custom.Values.OrderBy(static custom => custom.Kind, StringComparer.OrdinalIgnoreCase))
        {
            kinds.Add(new LoopCriterionKindInfo(custom.Kind, BuiltIn: false, custom.Description));
        }

        Kinds = kinds;
    }

    /// <summary>
    /// Gets every kind a definition can name: the built-in kinds in their fixed
    /// order, then the kinds registered in code, ordered by name.
    /// </summary>
    /// <remarks>Reading it calls no registered factory.</remarks>
    public IReadOnlyList<LoopCriterionKindInfo> Kinds { get; }

    /// <summary>
    /// Gets the model an <c>aiJudge</c> criterion calls, or <see langword="null"/>
    /// when the host configured none and that criterion does not compile.
    /// </summary>
    public ModelBinding? JudgeModel => _judgeOptions?.Model;

    /// <summary>
    /// Finds every problem in <paramref name="loop"/> that stops it from
    /// compiling. <see cref="Build"/> refuses a loop with the same list, so the
    /// validation report and the compiler can never apply different rules.
    /// </summary>
    /// <param name="loop">The loop settings.</param>
    /// <returns>The problems, in the order of the settings; empty when there is none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// It calls no registered factory: a kind registered in code counts as known
    /// whether or not its factory has run. A factory that fails reports at
    /// compilation, in <see cref="Build"/>.
    /// </remarks>
    public IReadOnlyList<LoopSettingsProblem> FindProblems(LoopSettings loop)
    {
        ArgumentNullException.ThrowIfNull(loop);

        var problems = new List<LoopSettingsProblem>();

        if (loop.Criteria.Count == 0)
        {
            problems.Add(new LoopSettingsProblem(
                CriteriaEmptyCode,
                "harness.loop.criteria",
                $"{nameof(LoopSettings)}.{nameof(LoopSettings.Criteria)} is empty. A loop with no stop " +
                "criterion never decides that the work is finished; it only runs until the iteration " +
                "ceiling. Give it at least one criterion, or leave " +
                $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.Loop)} null to turn the loop off."));
        }

        if (loop.MaxIterations is <= 0)
        {
            problems.Add(new LoopSettingsProblem(
                MaxIterationsInvalidCode,
                "harness.loop.maxIterations",
                $"{nameof(LoopSettings)}.{nameof(LoopSettings.MaxIterations)} must be greater than zero. " +
                $"Actual value: {loop.MaxIterations}."));
        }

        // AOT-safe generic form. A number that is not a member would otherwise act
        // like the default without a word, and the definition said something else.
        foreach (var (field, path, outcome) in new[]
                 {
                     (nameof(LoopSettings.OnExhausted), "harness.loop.onExhausted", loop.OnExhausted),
                     (nameof(LoopSettings.OnCriterionError), "harness.loop.onCriterionError", loop.OnCriterionError),
                 })
        {
            if (!Enum.IsDefined(outcome))
            {
                problems.Add(new LoopSettingsProblem(
                    OutcomeInvalidCode,
                    path,
                    $"{nameof(LoopSettings)}.{field} must be {nameof(LoopOutcome.ReturnLastResponse)} or " +
                    $"{nameof(LoopOutcome.FailRun)}. Actual value: {(int)outcome}."));
            }
        }

        for (var index = 0; index < loop.Criteria.Count; index++)
        {
            AddCriterionProblems(loop.Criteria[index], index, problems);
        }

        return problems;
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
    /// <exception cref="TraconCompilationException">The judge's provider is not registered.</exception>
    /// <exception cref="TraconTenantProviderRefusedException">The tenant's egress policy forbids the judge's provider.</exception>
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

        // F-442: a tenant's refusal keeps its type (see AgentDefinitionCompiler).
        catch (TraconTenantProviderRefusedException ex)
        {
            throw new TraconTenantProviderRefusedException(
                $"Agent '{definition.Name}' could not build its '{AiJudgeKind}' loop criterion: {ex.Message}",
                ex);
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
    /// <exception cref="TraconCompilationException">
    /// The loop settings are not usable (every problem <see cref="FindProblems"/>
    /// reports, in one message), or the factory of a criterion registered in code failed.
    /// </exception>
    public (LoopEvaluator Evaluator, LoopAgentOptions Options) Build(AgentDefinition definition, IChatClient? judgeClient)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var loop = definition.Harness?.Loop
            ?? throw new ArgumentException("The definition carries no loop settings.", nameof(definition));

        if (FindProblems(loop) is { Count: > 0 } problems)
        {
            throw Invalid(definition, string.Join(" ", problems.Select(static problem => problem.Message)));
        }

        var maxIterations = loop.MaxIterations ?? LoopSettings.DefaultMaxIterations;
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

    private void AddCriterionProblems(LoopCriterion criterion, int index, List<LoopSettingsProblem> problems)
    {
        var path = $"harness.loop.criteria[{index}]";

        if (string.IsNullOrWhiteSpace(criterion.Kind))
        {
            problems.Add(new LoopSettingsProblem(
                CriterionKindMissingCode,
                path + ".kind",
                $"Every loop criterion must carry a '{nameof(LoopCriterion.Kind)}'. Criterion {index + 1} has none."));

            return;
        }

        switch (Canonical(criterion.Kind))
        {
            case CompletionMarkerKind:
                if (string.IsNullOrWhiteSpace(criterion.Marker))
                {
                    problems.Add(new LoopSettingsProblem(
                        MarkerMissingCode,
                        path + ".marker",
                        $"The '{CompletionMarkerKind}' loop criterion requires " +
                        $"'{nameof(LoopCriterion.Marker)}'. Without it there is no text to look for and " +
                        "the criterion can never be satisfied."));
                }

                return;

            case AiJudgeKind:
                if (criterion.JudgeCriteria.Count == 0)
                {
                    problems.Add(new LoopSettingsProblem(
                        JudgeCriteriaMissingCode,
                        path + ".judgeCriteria",
                        $"The '{AiJudgeKind}' loop criterion requires at least one " +
                        $"'{nameof(LoopCriterion.JudgeCriteria)}' entry. A judge with nothing to judge against " +
                        "cannot decide that the work is finished."));
                }

                // A blank entry reaches the judge as a criterion with no text: it
                // checks nothing, and an all-blank list is a judge with nothing to
                // judge against. Each one is named, so the report points at it.
                for (var entry = 0; entry < criterion.JudgeCriteria.Count; entry++)
                {
                    if (string.IsNullOrWhiteSpace(criterion.JudgeCriteria[entry]))
                    {
                        problems.Add(new LoopSettingsProblem(
                            JudgeCriteriaMissingCode,
                            $"{path}.judgeCriteria[{entry}]",
                            $"Entry {entry + 1} of the '{AiJudgeKind}' loop criterion's " +
                            $"'{nameof(LoopCriterion.JudgeCriteria)}' is blank. A blank criterion gives the judge " +
                            "nothing to check; remove it."));
                    }
                }

                // The judge runs on the SEPARATELY configured judge binding, never on
                // the agent's own model: this criterion calls a model on every
                // iteration, and charging that to an expensive agent model multiplies
                // the loop's bill. It is also the same binding the run judge already
                // uses, so a host configures one judge model, not two.
                if (_judgeOptions?.Model is null)
                {
                    problems.Add(new LoopSettingsProblem(
                        JudgeUnconfiguredCode,
                        path + ".kind",
                        $"The '{AiJudgeKind}' loop criterion needs a judge model, but none is configured. " +
                        "Call 'AddModelRunJudge(options => options.Model = ...)' on the Tracon builder, " +
                        "or use a criterion kind that does not call a model."));
                }

                return;

            case TodoCompletionKind:
            case BackgroundTaskCompletionKind:
                return;

            default:
                if (!_custom.ContainsKey(criterion.Kind))
                {
                    problems.Add(new LoopSettingsProblem(
                        CriterionUnknownCode,
                        path + ".kind",
                        $"Unknown loop criterion kind: '{criterion.Kind}'. Built-in kinds: " +
                        $"{string.Join(", ", BuiltInKinds)}. If this is a criterion of your own, register it " +
                        $"with 'ITraconBuilder.AddLoopEvaluator(\"{criterion.Kind}\", ...)'."));
                }

                return;
        }
    }

    // Runs only after FindProblems found nothing, so every branch has what it
    // needs; the one check left guards a programming error.
    private LoopEvaluator BuildCriterion(AgentDefinition definition, LoopCriterion criterion, IChatClient? judgeClient)
    {
        switch (Canonical(criterion.Kind))
        {
            case CompletionMarkerKind:
                return new CompletionMarkerLoopEvaluator(criterion.Marker!, new CompletionMarkerLoopEvaluatorOptions());

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
                return _custom[criterion.Kind].Resolve(definition, _services, _loggerFactory);
        }
    }

    private AIJudgeLoopEvaluator BuildJudgeCriterion(AgentDefinition definition, LoopCriterion criterion, IChatClient? judgeClient)
    {
        if (judgeClient is null || _judgeOptions?.Model is not { } judgeModel)
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

        // 🚨 MAF drops the judge's response, and with it the usage: without the
        // tracker the judge's tokens reached the tree budget but not the run's
        // usage, cost or quota entry (F-349). Wrapped here, once, so the async and
        // the synchronous compile path both get it.
        return new AIJudgeLoopEvaluator(
            SideChannelUsageTrackingChatClient.ForLoopJudge(judgeClient, judgeModel),
            judgeOptions);
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

    /// <summary>
    /// One criterion registered in code, and the single instance its factory
    /// produced once it has run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The factory runs on the FIRST compilation that names the kind, never at
    /// setup: the registry is built inside the compiler's constructor, and a
    /// criterion that asks for the agent catalog would close the catalog →
    /// compiler → catalog cycle there. By the first compilation the catalog is
    /// built.
    /// </para>
    /// <para>
    /// It runs at most once successfully, under a lock, so concurrent first
    /// compilations see one call. A factory that throws is NOT cached: the next
    /// compilation calls it again, so a dependency that was not ready yet does
    /// not poison the kind for the life of the process.
    /// </para>
    /// <para>
    /// The instance serves every tenant. Whatever the factory reads from an
    /// ambient tenant belongs to the first compilation's tenant; a criterion
    /// reads the tenant when it evaluates, not when it is created.
    /// </para>
    /// </remarks>
    private sealed class CustomCriterion(TraconLoopEvaluatorRegistration registration)
    {
        // The lock is taken on the registration this type alone holds: a separate
        // object field would trigger MA0158 on net9.0+ (the InMemoryJobStore pattern).
        private LoopEvaluator? _instance;
        private bool _creating;

        public string Kind => registration.Kind;

        public string? Description => registration.Description;

        public LoopEvaluator Resolve(AgentDefinition definition, IServiceProvider services, ILoggerFactory? loggerFactory)
        {
            if (Volatile.Read(ref _instance) is { } existing)
            {
                return existing;
            }

            lock (registration)
            {
                if (_instance is not null)
                {
                    return _instance;
                }

                // The lock is re-entrant on its own thread: a factory that compiles
                // an agent naming this same kind would otherwise call itself until
                // the stack runs out.
                if (_creating)
                {
                    throw new TraconCompilationException(
                        $"Agent '{definition.Name}' could not build its '{Kind}' loop criterion: the factory " +
                        "registered with AddLoopEvaluator asked for the same criterion while creating it.")
                    {
                        AgentName = definition.Name,
                    };
                }

                _creating = true;

                try
                {
                    var created = registration.Factory(services)
                        ?? throw new TraconCompilationException(
                            $"Agent '{definition.Name}' could not build its '{Kind}' loop criterion: the factory " +
                            "registered with AddLoopEvaluator returned null.")
                        {
                            AgentName = definition.Name,
                        };

                    Volatile.Write(ref _instance, created);

                    return created;
                }
                catch (Exception ex) when (ex is not TraconCompilationException)
                {
                    // The exception's message is not repeated: it is the host's own text
                    // and may carry what it read from configuration, while this message
                    // reaches the management API. The log carries the whole exception.
                    (loggerFactory?.CreateLogger<LoopEvaluatorRegistry>() ?? (ILogger)NullLogger.Instance).LogError(
                        ex,
                        "The factory of Tracon loop criterion '{Kind}' threw while agent '{Agent}' was compiled. " +
                        "The next compilation that names the kind calls it again.",
                        Kind,
                        definition.Name);

                    throw new TraconCompilationException(
                        $"Agent '{definition.Name}' could not build its '{Kind}' loop criterion: the factory " +
                        $"registered with AddLoopEvaluator threw {ex.GetType().Name}. The host log carries the " +
                        "details; the next compilation calls the factory again.",
                        ex)
                    {
                        AgentName = definition.Name,
                    };
                }
                finally
                {
                    _creating = false;
                }
            }
        }
    }

    /// <summary>The provider a factory receives when the registry was built without one.</summary>
    private sealed class NoServices : IServiceProvider
    {
        public static readonly NoServices Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
#pragma warning restore MAAI001

/// <summary>A criterion kind a definition can name, as the console lists it.</summary>
/// <param name="Kind">The kind name.</param>
/// <param name="BuiltIn">Whether the kind is built in rather than registered in code.</param>
/// <param name="Description">What the kind checks, or <see langword="null"/> when its registration gave none.</param>
internal sealed record LoopCriterionKindInfo(string Kind, bool BuiltIn, string? Description);

/// <summary>One reason a definition's loop settings do not compile.</summary>
/// <param name="Code">The stable validation code, <c>loop_</c> prefixed.</param>
/// <param name="Path">The JSON path of the offending field in the definition, for example <c>harness.loop.criteria[1].marker</c>.</param>
/// <param name="Message">The explanation, written for the definition's author.</param>
internal sealed record LoopSettingsProblem(string Code, string Path, string Message);
