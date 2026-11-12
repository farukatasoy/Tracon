namespace Tracon;

/// <summary>
/// Decides which interrupted runs automatic continuation may pick up. The one rule
/// both <see cref="RunReconciliationService"/> and <see cref="RunContinuationJobHandler"/>
/// apply.
/// </summary>
/// <remarks>
/// <para>
/// Continuation re-sends an agent turn's recorded input to its agent, in the run's
/// session. Only a <strong>root agent run</strong> is such a turn:
/// </para>
/// <list type="bullet">
/// <item>
/// A workflow run records its parameter values and no message, and it resumes from its
/// checkpoints. Treated as an agent turn, it opened a continuation under the workflow's
/// name that could only fail - or, with an agent of the same name, ran that agent in the
/// workflow's session.
/// </item>
/// <item>
/// A sub-run (a workflow step, a callable sub-agent) carries its root's session id and
/// nobody waits for its answer. Continued on its own, it repeated its tools and wrote
/// its turn into the root's session.
/// </item>
/// </list>
/// <para>
/// The reconciler checks this before it queues a continuation; the handler checks it
/// again, for a job queued before this rule existed or by hand.
/// </para>
/// <para>
/// A workflow root is not continued here even when workflow recovery is on:
/// the job that started the run owns it and resumes it from its
/// checkpoint when it is leased again (<see cref="WorkflowRecovery"/>). Two owners
/// would resume the same run twice.
/// </para>
/// </remarks>
internal static class RunContinuationEligibility
{
    /// <summary>Returns why the definition an interrupted run used cannot be continued.</summary>
    /// <param name="definition">The definition the run used; <see langword="null"/> when there is none in the store.</param>
    /// <param name="agentName">The run's agent.</param>
    /// <param name="agentVersion">The version the run was pinned to, if any.</param>
    /// <returns>
    /// A sentence that names the agent and says why, as the continuation job fails with it;
    /// <see langword="null"/> when the definition may be continued.
    /// </returns>
    /// <remarks>
    /// The rules the job applies after it has read the definition, kept here so the reconciler applies the same ones
    /// BEFORE it queues the job: a job refused by its handler leaves a placeholder run that is <c>Failed</c> and was never
    /// started, and the run that was interrupted learns nothing. Skills, callable sub-agents, agent extensions and a
    /// session workspace come through a context provider, not through the tool-calling pipeline that tool playback wraps,
    /// so a continuation could repeat a call that already completed.
    /// </remarks>
    public static string? DefinitionRefusal(AgentDefinition? definition, string agentName, int? agentVersion)
    {
        if (definition is null)
        {
            return $"Agent '{agentName}' has no persistent definition" +
                   (agentVersion is { } version ? $" version {version}" : string.Empty) +
                   "; a code-defined or deleted agent cannot be continued.";
        }

        if (definition.SkillNames.Count > 0 || definition.CallableAgents.Count > 0)
        {
            return $"Agent '{agentName}' uses skills or callable sub-agents, which do not go " +
                   "through tool playback; continuing it could silently repeat a completed skill or " +
                   "sub-agent call. This run cannot be continued.";
        }

        if (definition.ExtensionNames.Count > 0)
        {
            return $"Agent '{agentName}' uses agent extensions, whose context providers can add tools " +
                   "that do not go through tool playback; continuing it could silently repeat a completed " +
                   "tool call. This run cannot be continued.";
        }

        if (definition.Workspace is { Enabled: true })
        {
            return $"Agent '{agentName}' has a session workspace, whose file tools come from a context " +
                   "provider and do not go through tool playback; continuing it could silently repeat a " +
                   "completed write or delete. This run cannot be continued.";
        }

        return null;
    }

    /// <summary>Returns why the input an interrupted run recorded cannot be sent again.</summary>
    /// <param name="runId">The interrupted run.</param>
    /// <param name="input">The recorded input; <see langword="null"/> when there is none in the store.</param>
    /// <returns>A sentence that names the run and says why; <see langword="null"/> when there is a turn to send.</returns>
    /// <remarks>
    /// Input recording may have been off, or a retention policy may have deleted the row. Kept here with the other rules, so
    /// the reconciler refuses BEFORE it queues the job, as the job's handler would refuse after it: the handler's refusal
    /// leaves a placeholder run that is <c>Failed</c> and was never started.
    /// </remarks>
    public static string? InputRefusal(Guid runId, RunInputRecord? input)
    {
        if (input is null)
        {
            return $"Run '{runId}' has no recorded input (input recording may have been disabled, " +
                   "or a retention policy deleted it); it cannot be continued.";
        }

        return input.Messages.Count == 0
            ? $"Run '{runId}' recorded an empty input; it cannot be continued."
            : null;
    }

    /// <summary>Decides the parameter values a continuation runs with, or says why it cannot.</summary>
    /// <param name="runId">The interrupted run.</param>
    /// <param name="agentName">The run's agent.</param>
    /// <param name="input">The recorded input of the run.</param>
    /// <param name="definition">The definition the run used.</param>
    /// <param name="parameterFactory">Resolves the recorded values against the definition's schema; <see langword="null"/> without parameters support.</param>
    /// <returns>
    /// The reason, or the values to bind (<see langword="null"/> when the definition takes no parameters).
    /// </returns>
    /// <remarks>
    /// The source run recorded the values it was bound with, and the turn continues with the SAME ones. Without a record the
    /// turn would run on the defaults (or fail on a required parameter): a different prompt halfway through the
    /// conversation. So a definition that declares parameters and a run that recorded none stays refused.
    /// </remarks>
    public static (string? Refusal, IReadOnlyDictionary<string, string>? Values) ParameterDecision(
        Guid runId,
        string agentName,
        RunInputRecord input,
        AgentDefinition definition,
        ParameterizedAgentFactory? parameterFactory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.Parameters.Count == 0)
        {
            return (null, null);
        }

        var recorded = parameterFactory?.ResolveRecorded(definition.Parameters, input.Parameters, overrides: null)
            ?? new RecordedValues(null, RecordedValuesProblem.NotRecorded, null);

        return recorded.Problem switch
        {
            RecordedValuesProblem.NotRecorded =>
                ($"Agent '{agentName}' declares parameters, and run '{runId}' did not record the " +
                 "values it was bound with ('Tracon:RunRecording:RecordRunInput' was off, or the run predates " +
                 "parameter recording); continuing it would run with different instructions. " +
                 "This run cannot be continued.", null),
            RecordedValuesProblem.Invalid =>
                ($"Run '{runId}' cannot be continued: agent '{agentName}' no longer accepts the " +
                 $"values it was bound with. {recorded.Detail}", null),
            _ => (null, recorded.Values),
        };
    }

    /// <summary>Returns why a run is not continued.</summary>
    /// <param name="record">The interrupted run.</param>
    /// <returns>
    /// The reason, as a clause that follows "Automatic continuation was refused: ";
    /// <see langword="null"/> when the run is a root agent turn.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="record"/> is <see langword="null"/>.</exception>
    public static string? Refusal(RunRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (record.Kind == RunKind.Workflow)
        {
            return $"run '{record.Id}' is a workflow run, and continuation replays an agent turn, not a " +
                   "workflow graph. Resume it from its latest checkpoint with " +
                   $"POST /api/workflows/runs/{record.Id}/resume (GET /api/workflows/runs/{record.Id}/checkpoints " +
                   "lists the checkpoints it wrote). A workflow run a job started is resumed by that job when it " +
                   "is leased again, if 'Tracon:Workflows:ResumeInterruptedRuns' is on.";
        }

        // Either field marks a sub-run: a store that dropped one of them must not
        // turn a workflow step into an agent turn of its own.
        if (record.ParentRunId is not null || record.Depth > 0)
        {
            var root = record.RootRunId ?? record.ParentRunId;

            return $"run '{record.Id}' is a sub-run" + (root is { } rootRunId ? $" of run '{rootRunId}'" : string.Empty) +
                   ". Only the run at the root of a tree is continued or resumed: on its own, a sub-run would run " +
                   "in its root's session with nobody waiting for its answer.";
        }

        return record.Kind == RunKind.Agent
            ? null
            : $"run '{record.Id}' is a '{record.Kind}' run; only an agent turn is continued.";
    }
}
