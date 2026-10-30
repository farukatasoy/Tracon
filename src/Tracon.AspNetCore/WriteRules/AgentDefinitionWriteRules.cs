using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>The result of the entity validation of an agent: the refusal, if any, and the whole report.</summary>
/// <param name="Finding">The refusal; <see langword="null"/> when the definition is valid.</param>
/// <param name="Report">The validator's report, whose warnings an import reports too.</param>
internal readonly record struct AgentEntityCheck(WriteRuleFinding? Finding, AgentValidationReport Report);

/// <summary>
/// The rules an agent definition has to pass to be written: the ones <c>POST</c> and <c>PUT /api/agents</c>
/// apply, and the ones an import applies to each agent of a package.
/// </summary>
/// <remarks>
/// One set of rules for both writers. The rules that read other agents take the catalog and the
/// validator as arguments, so an import can hand them the package laid over the target
/// (<see cref="DefinitionOverlay"/>) and still apply the same code.
/// </remarks>
internal static class AgentDefinitionWriteRules
{
    /// <summary>Checks the fields every agent needs.</summary>
    /// <param name="request">The definition to write.</param>
    /// <returns>The refusal; <see langword="null"/> when the request has a name and a model.</returns>
    public static WriteRuleFinding? Validate(AgentDefinitionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return WriteRuleFinding.BadRequest(
                "Agent name empty",
                "'name' is required.",
                WriteRuleCodes.AgentNameEmpty,
                "name");
        }

        if (string.IsNullOrWhiteSpace(request.Model?.Provider) || string.IsNullOrWhiteSpace(request.Model.Model))
        {
            return WriteRuleFinding.BadRequest(
                "Model binding missing",
                "'model.provider' and 'model.model' are required.",
                WriteRuleCodes.AgentModelMissing,
                "model");
        }

        return null;
    }

    /// <summary>Validates the definition's call graph.</summary>
    /// <param name="catalog">The agent catalog; an overlay catalog for an import.</param>
    /// <param name="request">The definition to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refusal; <see langword="null"/> when the graph is valid.</returns>
    /// <remarks>
    /// The check happens <strong>at save time</strong>. If it were left to run
    /// time, the user would only see the error after running the agent and
    /// after the depth counter filled up - that is, after spending tokens.
    /// </remarks>
    public static async ValueTask<WriteRuleFinding?> ValidateCallGraphAsync(
        IAgentCatalog catalog,
        AgentDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(request);

        if (request.CallableAgents.Count == 0)
        {
            return null;
        }

        var descriptors = await catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var names = request.CallableAgents.Select(static agent => agent.Name).ToList();

        return AgentCallGraph.ValidateDetailed(request.Name, names, descriptors) is { } problem
            ? WriteRuleFinding.BadRequest("Call graph invalid", problem.Message, problem.Code, "callableAgents")
            : null;
    }

    /// <summary>
    /// Fully validates the definition before saving: model, tool, skill, and callable-agent EXISTENCE checks.
    /// </summary>
    /// <param name="validator">The agent validator.</param>
    /// <param name="request">The definition to write.</param>
    /// <param name="overlay">
    /// The agents and skills of an import package, read as if saved; <see langword="null"/> for a save endpoint.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refusal, if any, and the validator's report.</returns>
    /// <remarks>
    /// Before this check existed, only the separate <c>POST /api/agents/validate</c> endpoint was
    /// called; the SAVE path itself would save an unknown skill/tool name without any error.
    /// </remarks>
    public static async ValueTask<AgentEntityCheck> ValidateEntitiesAsync(
        AgentDefinitionValidator validator,
        AgentDefinitionRequest request,
        DefinitionOverlay? overlay,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(request);

        var definition = request.ToDefinition();
        var report = overlay is null
            ? await validator.ValidateAsync(definition, cancellationToken).ConfigureAwait(false)
            : await validator.ValidateAsync(definition, overlay, cancellationToken).ConfigureAwait(false);

        if (report.Valid)
        {
            return new AgentEntityCheck(null, report);
        }

        var errors = report.Messages
            .Where(static message => message.Severity == ValidationSeverity.Error)
            .ToList();

        var finding = new WriteRuleFinding
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Definition invalid",
            Detail = string.Join(" ", errors.Select(static message => message.Message)),
            Code = WriteRuleCodes.AgentDefinitionInvalid,
            Messages = errors,
        };

        return new AgentEntityCheck(finding, report);
    }

    /// <summary>Refuses a write to a name an agent defined in code, or in a custom source, holds.</summary>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="name">The agent name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The refusal; <see langword="null"/> when the name is free or a stored agent's.</returns>
    public static async ValueTask<WriteRuleFinding?> GuardCodeAgentAsync(
        IAgentCatalog catalog,
        string name,
        CancellationToken cancellationToken)
        => GuardCodeAgent(
            await AgentEndpoints.FindDescriptorAsync(catalog, name, cancellationToken).ConfigureAwait(false),
            name);

    /// <summary>Refuses a write to a name an agent defined in code, or in a custom source, holds.</summary>
    /// <param name="descriptor">The catalog's entry for the name; <see langword="null"/> when there is none.</param>
    /// <param name="name">The agent name.</param>
    /// <returns>The refusal; <see langword="null"/> when the name is free or a stored agent's.</returns>
    public static WriteRuleFinding? GuardCodeAgent(AgentDescriptor? descriptor, string name)
        => descriptor?.Origin switch
        {
            AgentDefinitionOrigin.Code => WriteRuleFinding.Conflict(
                "Code-defined agent cannot be modified",
                $"'{name}' is defined in code. Code definitions are validated at compile time " +
                "and cannot be changed from the management API; update the application code to change it.",
                WriteRuleCodes.AgentCodeDefined),
            AgentDefinitionOrigin.Custom => WriteRuleFinding.Conflict(
                "Custom-source agent cannot be modified",
                $"'{name}' belongs to the '{descriptor.SourceName}' agent source and cannot be changed from the management API.",
                WriteRuleCodes.AgentCustomSource),
            _ => null,
        };

    /// <summary>Refuses to create an agent under a name the catalog already holds.</summary>
    /// <param name="existing">The catalog's entry for the name.</param>
    /// <param name="name">The agent name.</param>
    /// <returns>The refusal; the create endpoint refuses a stored agent's name as well, and tells the caller to use <c>PUT</c>.</returns>
    public static WriteRuleFinding NameInUse(AgentDescriptor existing, string name)
    {
        ArgumentNullException.ThrowIfNull(existing);

        return existing.Origin switch
        {
            AgentDefinitionOrigin.Code => WriteRuleFinding.Conflict(
                "Agent name in use",
                $"'{name}' is an agent defined in code and cannot be changed from the " +
                "management API. Code wins name conflicts, so a definition written with the same " +
                "name would never resolve.",
                WriteRuleCodes.AgentCodeDefined),
            AgentDefinitionOrigin.Custom => WriteRuleFinding.Conflict(
                "Agent name in use",
                $"'{name}' belongs to the '{existing.SourceName}' agent source and cannot " +
                "be changed from the management API.",
                WriteRuleCodes.AgentCustomSource),
            _ => WriteRuleFinding.Conflict(
                "Agent name in use",
                $"A definition named '{name}' already exists. Use PUT to update it.",
                WriteRuleCodes.AgentNameInUse),
        };
    }
}
