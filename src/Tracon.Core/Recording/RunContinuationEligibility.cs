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
