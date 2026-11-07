using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Binds the parameter values of a workflow run: checks them against the schema of the workflow, proves that every
/// participant can be bound, and records the final values beside the run row.
/// </summary>
/// <remarks>
/// The values a run was started with are the only input a later resume or answer needs, because a workflow continues
/// from its checkpoints. A failure to record them is logged and counted, never thrown, and the consequence is stated
/// where it bites: the run cannot be continued.
/// </remarks>
internal sealed class WorkflowParameterBinder
{
    private readonly ParameterizedAgentFactory? _parameterFactory;
    private readonly RunRecordingServices _services;
    private readonly IOptions<TraconOptions> _traconOptions;
    private readonly ILogger _logger;

    /// <summary>Creates the binder.</summary>
    /// <param name="parameterFactory">
    /// Looks up the participants' definitions for the start-of-run coverage check and applies the configured value length
    /// limit. When <see langword="null"/>, the coverage check is skipped.
    /// </param>
    /// <param name="services">The run store, the input store, the clock and the metrics of the run path.</param>
    /// <param name="traconOptions">The general settings, for the value length limit and the input recording switch.</param>
    /// <param name="logger">The logger of the runner that owns the run.</param>
    public WorkflowParameterBinder(
        ParameterizedAgentFactory? parameterFactory,
        RunRecordingServices services,
        IOptions<TraconOptions> traconOptions,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(traconOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _parameterFactory = parameterFactory;
        _services = services;
        _traconOptions = traconOptions;
        _logger = logger;
    }

    /// <summary>
    /// Checks the run's parameter values against the workflow's schema, proves
    /// every participant can be bound, records the final values and returns the
    /// scope the graph runs under.
    /// </summary>
    /// <param name="execution">What is being run.</param>
    /// <param name="found">The workflow the graph came from: a stored definition, or a registration in code.</param>
    /// <param name="scope">The run's scope so far.</param>
    /// <param name="cancellationToken">The run's token.</param>
    /// <returns>
    /// <paramref name="scope"/>, carrying the final values when the workflow
    /// declares parameters.
    /// </returns>
    /// <exception cref="TraconException">
    /// A required value is missing, a name is not declared, a value is too long,
    /// or a participant needs a required parameter nobody supplies.
    /// </exception>
    internal async ValueTask<AgentRunScope> BindAsync(
        WorkflowExecution execution,
        WorkflowCatalog.FoundWorkflow found,
        AgentRunScope scope,
        CancellationToken cancellationToken)
    {
        // A stored workflow declares its schema in its definition, a workflow defined in code in its
        // registration: the same schema, checked by the same code below.
        var definition = found.Definition;
        var schema = definition?.Parameters ?? found.Code?.Parameters ?? [];

        // The runner is the authority (the HTTP endpoint only answers 400
        // early): a consumer can call IWorkflowRunner directly and skip HTTP.
        var validation = _parameterFactory is { } factory
            ? factory.Validate(schema, execution.Parameters)
            : AgentParameterValidator.ValidateValues(
                schema,
                execution.Parameters,
                _traconOptions.Value.MaxParameterValueLength);

        if (!validation.IsValid)
        {
            throw new TraconException(
                $"The run's parameters are invalid for workflow '{execution.WorkflowName}'. " +
                ParameterValueSet.Describe(validation));
        }

        if (schema.Count == 0)
        {
            return scope;
        }

        var final = ParameterValueSet.Resolve(schema, execution.Parameters);

        // The participants of a stored workflow are named by its definition. A graph built in code has
        // no definition to read them from, so its agent steps are checked when they run (the step
        // validates the values it receives and fails by name).
        if (definition is not null)
        {
            await RequireParticipantCoverageAsync(execution.WorkflowName, definition, final, cancellationToken)
                .ConfigureAwait(false);
        }

        await SaveRunInputAsync(execution, scope, final, cancellationToken).ConfigureAwait(false);

        return scope with
        {
            ParameterValues = final,
            ParameterNames = schema.Select(static parameter => parameter.Name).ToHashSet(StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// Proves, before the first model call, that every required parameter of
    /// every participant has a value or a default.
    /// </summary>
    /// <remarks>
    /// Participants resolve lazily, one step at a time. Without this check the
    /// third step's missing parameter surfaced only after the first two steps had
    /// spent their tokens. The definitions are read here in the order code first,
    /// then the store (the same lookup <c>/run</c> uses); this check is
    /// skipped for a workflow that declares no parameters, which is the common
    /// case and pays nothing.
    /// </remarks>
    private async ValueTask RequireParticipantCoverageAsync(
        string workflowName,
        WorkflowDefinition definition,
        IReadOnlyDictionary<string, string> final,
        CancellationToken cancellationToken)
    {
        if (_parameterFactory is not { } factory)
        {
            return;
        }

        foreach (var (participant, pinnedVersion) in ParticipantNames(definition))
        {
            // A pinned step is covered against ITS version's schema: that is the definition it runs.
            var (agentDefinition, _) = await factory.FindAsync(participant, pinnedVersion, cancellationToken)
                .ConfigureAwait(false);

            if (agentDefinition is not { Parameters.Count: > 0 })
            {
                continue;
            }

            var stepValues = ParameterValueSet.Intersect(agentDefinition.Parameters, final);
            var check = factory.Validate(agentDefinition.Parameters, stepValues);

            if (check.IsValid)
            {
                continue;
            }

            var missing = string.Join(
                ", ",
                check.Errors
                    .Where(static error => string.Equals(error.Code, AgentParameterValidator.MissingParameterCode, StringComparison.Ordinal))
                    .Select(static error => $"'{error.ParameterName}'"));

            throw new TraconException(
                $"Workflow '{workflowName}' cannot start: agent '{participant}' requires parameter(s) " +
                $"{(missing.Length > 0 ? missing : "it cannot be given")} with no default, and the workflow " +
                "does not supply them. Declare the parameter in the workflow's parameters, or give it a " +
                "default value in the agent.");
        }
    }

    /// <summary>Lists every agent the definition's graph can call, with the version a step pins.</summary>
    private static IEnumerable<(string Name, int? PinnedVersion)> ParticipantNames(WorkflowDefinition definition)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in definition.AgentNames)
        {
            if (seen.Add(name))
            {
                yield return (name, null);
            }
        }

        foreach (var node in definition.Nodes)
        {
            if (node.Kind == WorkflowNodeKind.Agent && seen.Add(node.Name))
            {
                yield return (node.Name, node.AgentVersion);
            }
        }

        if (definition.ManagerAgentName is { Length: > 0 } manager && seen.Add(manager))
        {
            yield return (manager, null);
        }
    }

    /// <summary>
    /// Records the run's final parameter values beside its row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The workflow row stores NO messages: the agent path masks its input
    /// through the content guard before it records it, and this
    /// path never sees a guarded copy. Nothing reads a workflow's message back;
    /// a workflow resumes from its checkpoints, and only the values it was
    /// started with are needed.
    /// </para>
    /// <para>
    /// A failure is logged and counted, never thrown: the same contract
    /// <c>IRunInputStore</c> has for the agent path. The consequence is stated
    /// where it bites - a later resume or response finds no values and refuses.
    /// </para>
    /// </remarks>
    private async ValueTask SaveRunInputAsync(
        WorkflowExecution execution,
        AgentRunScope scope,
        IReadOnlyDictionary<string, string> final,
        CancellationToken cancellationToken)
    {
        if (_services.RunInputStore is null || !_traconOptions.Value.RunRecording.RecordRunInput)
        {
            return;
        }

        try
        {
            await _services.RunInputStore.SaveAsync(
                new RunInputRecord
                {
                    RunId = execution.RunId,
                    TenantId = scope.TenantId ?? _services.TenantContext.TenantId,
                    Messages = [],
                    Parameters = final,
                    CreatedAt = _services.TimeProvider.GetUtcNow(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (OperationCancellation.IsFailure(exception, cancellationToken))
        {
            _services.Metrics?.RecordRunRecordingFailure(scope.TenantId, RunRecordingStages.Input);

            _logger.LogWarning(
                exception,
                "Failed to save the parameter values of workflow run {RunId}. The run continues, but it " +
                "cannot be resumed or answered, because the values it started with were not recorded.",
                execution.RunId);
        }
    }
}
