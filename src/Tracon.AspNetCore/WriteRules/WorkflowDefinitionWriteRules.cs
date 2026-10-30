namespace Tracon;

/// <summary>
/// The rules a workflow definition has to pass to be written: the ones <c>PUT /api/workflows/{name}</c> and its
/// rollback apply, and the ones an import applies to each workflow of a package.
/// </summary>
/// <remarks>
/// One set of rules for every writer. The agent catalog and the agent store come in as arguments, so an
/// import can hand the rule the package laid over the target (<see cref="DefinitionOverlay"/>).
/// </remarks>
internal static class WorkflowDefinitionWriteRules
{
    /// <summary>
    /// Judges a definition the way a save does: the rules the compiler applies, the
    /// registered functions and their settings, the type chain and the agent version pins.
    /// </summary>
    /// <param name="definition">The definition about to be written.</param>
    /// <param name="functions">The function registry; <see langword="null"/> when the engine is not registered.</param>
    /// <param name="runTimeoutPolicy">The host's run timeout settings; <see langword="null"/> without the engine.</param>
    /// <param name="agents">The agent catalog.</param>
    /// <param name="agentDefinitions">The store that keeps stored agents' versions.</param>
    /// <param name="because">
    /// A sentence that opens the refusal, for a write that is not a plain save (a rollback
    /// writes content nobody typed just now); <see langword="null"/> for a save.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The <c>400</c> refusal; <see langword="null"/> when the definition may be written.</returns>
    /// <remarks>
    /// One method for every writer on purpose: a rollback is a save of stored content, an import is a save of
    /// a package's content, and a
    /// version saved a month ago can name a function that was unregistered since. Judged by
    /// a second, shorter copy of these checks it would write a definition no run can use.
    /// </remarks>
    public static async ValueTask<WriteRuleFinding?> ValidateDefinitionAsync(
        WorkflowDefinition definition,
        IWorkflowFunctionCatalog? functions,
        IWorkflowRunTimeoutPolicy? runTimeoutPolicy,
        IAgentCatalog agents,
        IAgentDefinitionStore agentDefinitions,
        string? because,
        CancellationToken cancellationToken)
    {
        var name = definition.Name;

        // Validation happens AT SAVE TIME. Accepting an invalid definition
        // and blowing up at run time would force the user to start a run
        // just to find the error. The workflow compiler uses the same
        // check; the rule lives in a single place (Tracon.Core).
        // F-382: a name a fixed route uses is refused before anything else is judged.
        if ((WorkflowDefinitionValidator.ReservedNameProblem(name) ?? WorkflowDefinitionValidator.Validate(definition)) is { } message)
        {
            return Invalid(because, message);
        }

        // F-405: the ceiling is the host's setting, read through the engine's seam.
        // Without the engine there is no ceiling to read and nothing to run the value;
        // the range was checked above.
        if (runTimeoutPolicy is not null &&
            WorkflowDefinitionValidator.RunTimeoutCeilingProblem(definition, runTimeoutPolicy.RunTimeout, runTimeoutPolicy.MaxRunTimeout) is { } ceilingProblem)
        {
            return Invalid(because, ceilingProblem);
        }

        // A function node's name IS checked at save time, unlike an agent
        // name (agents live in a store that can change after save; the
        // function registry is fixed in code for the process lifetime, so
        // checking early gives an honest guarantee - phase 71).
        foreach (var node in definition.Nodes)
        {
            if (node.Kind != WorkflowNodeKind.Function)
            {
                continue;
            }

            if (functions is null || !functions.Contains(node.Name))
            {
                return Invalid(
                    because,
                    $"Workflow '{name}' uses function '{node.Name}', but no such function is " +
                    "registered. Register it with AddWorkflowFunction() before referencing it " +
                    "from a workflow definition.");
            }

            // The settings are checked by the registry that knows the function's settings
            // type (F-295): the same read the compiler repeats when a run starts. Same
            // reasoning as the name above - the registry is fixed for the process lifetime,
            // so the early check is an honest guarantee.
            if (functions.ValidateSettings(node.Name, node.Settings) is { } settingsProblem)
            {
                return Invalid(because, $"Workflow '{name}': {settingsProblem}");
            }
        }

        // F-341: each node must take what the node before it passes on.
        // Microsoft Agent Framework drops a message no handler takes, without an
        // error, so a chain that breaks this would end every run with no
        // output. Checked after the names, so an unregistered function is
        // reported by name first; the compiler repeats the check.
        if (WorkflowDefinitionValidator.ValidateNodeTypes(definition, functions) is { } typeProblem)
        {
            return Invalid(because, typeProblem);
        }

        // Phase 200: a pinned step must name a version its agent has. Unlike an agent's NAME
        // (which a definition may name before the agent exists), a version cannot appear later,
        // so refusing it here is an honest guarantee; the compiler repeats the check because the
        // agent can be deleted after the save.
        if (await AgentVersionPin.FindProblemAsync(definition, agents, agentDefinitions, cancellationToken)
                .ConfigureAwait(false) is { } pinProblem)
        {
            return Invalid(because, pinProblem);
        }

        return null;

        static WriteRuleFinding Invalid(string? because, string detail)
            => WriteRuleFinding.BadRequest(
                "Workflow definition invalid",
                because is null ? detail : $"{because} {detail}",
                WriteRuleCodes.WorkflowDefinitionInvalid);
    }
}
