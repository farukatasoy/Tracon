namespace Tracon;

/// <summary>What an export is asked to write.</summary>
/// <param name="Agents">The agents to start from, by name. Together with <paramref name="Workflows"/> they select by name.</param>
/// <param name="Workflows">The workflows to start from, by name.</param>
/// <param name="Types">
/// The kinds to write in full (<see cref="DefinitionKinds"/>). Used on its own: a selection by name and a selection
/// by kind do not mix.
/// </param>
internal sealed record DefinitionExportSelection(
    IReadOnlyList<string> Agents,
    IReadOnlyList<string> Workflows,
    IReadOnlySet<string>? Types)
{
    /// <summary>Gets a value that tells whether the selection names definitions.</summary>
    public bool SelectsByName => Agents.Count > 0 || Workflows.Count > 0;
}

/// <summary>An export was asked for a definition by name and no stored definition has that name.</summary>
internal sealed class DefinitionNotFoundException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="kind">The kind of the definition (<see cref="DefinitionKinds"/>).</param>
    /// <param name="name">The name that no stored definition has.</param>
    public DefinitionNotFoundException(string kind, string name)
        : base($"There is no stored {kind} named '{name}'.")
    {
        Kind = kind;
        DefinitionName = name;
    }

    /// <summary>Gets the kind of the definition.</summary>
    public string Kind { get; }

    /// <summary>Gets the name that no stored definition has.</summary>
    public string DefinitionName { get; }
}

/// <summary>
/// Writes the database definitions of the caller's tenant as a <see cref="DefinitionBundle"/>.
/// </summary>
/// <remarks>
/// <para>
/// A selection by name brings the definitions the named ones need (the closure): a skill an agent loads, the
/// agent it calls, reviews with or shares instructions from, and the MCP server whose tool it uses. A selection
/// by kind brings every definition of the kinds and nothing else, because the caller chose exactly that.
/// </para>
/// <para>
/// An agent defined in code, or in a custom source, is never written: the importing host has to carry it in its
/// own code. A closure that reaches one leaves it out and says so in <see cref="DefinitionBundle.Warnings"/>.
/// The bundle is sorted by name, so the same definitions always write the same bytes.
/// </para>
/// </remarks>
internal sealed class DefinitionExporter
{
    private readonly IAgentDefinitionStore _agents;
    private readonly IWorkflowDefinitionStore _workflows;
    private readonly IAgentSkillStore _skills;
    private readonly IMcpServerStore _servers;
    private readonly ITenantContext _tenant;
    private readonly TimeProvider _time;

    /// <summary>Initializes an exporter.</summary>
    /// <param name="agents">The agent store.</param>
    /// <param name="workflows">The workflow store.</param>
    /// <param name="skills">The skill store.</param>
    /// <param name="servers">The MCP server store.</param>
    /// <param name="tenant">The caller's tenant.</param>
    /// <param name="time">The clock; <see langword="null"/> uses the system clock.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public DefinitionExporter(
        IAgentDefinitionStore agents,
        IWorkflowDefinitionStore workflows,
        IAgentSkillStore skills,
        IMcpServerStore servers,
        ITenantContext tenant,
        TimeProvider? time)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(tenant);

        _agents = agents;
        _workflows = workflows;
        _skills = skills;
        _servers = servers;
        _tenant = tenant;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Writes the bundle a selection asks for.</summary>
    /// <param name="selection">What to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The bundle.</returns>
    /// <exception cref="DefinitionNotFoundException">A definition the selection names is not stored.</exception>
    public async ValueTask<DefinitionBundle> ExportAsync(DefinitionExportSelection selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);

        var agents = new Dictionary<string, AgentDefinition>(StringComparer.Ordinal);
        var workflows = new Dictionary<string, WorkflowDefinition>(StringComparer.Ordinal);
        var skills = new Dictionary<string, AgentSkillDefinition>(StringComparer.Ordinal);
        var servers = new Dictionary<string, McpServerDefinition>(StringComparer.Ordinal);
        var warnings = new List<string>();

        if (selection.SelectsByName)
        {
            await CollectByNameAsync(selection, agents, workflows, skills, servers, warnings, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await CollectByKindAsync(selection.Types, agents, workflows, skills, servers, cancellationToken).ConfigureAwait(false);
        }

        warnings.AddRange(FindUntranslatablePins(agents, workflows));

        return new DefinitionBundle
        {
            Format = DefinitionBundleFormat.Name,
            FormatVersion = DefinitionBundleFormat.CurrentVersion,
            ExportedAt = _time.GetUtcNow(),
            Agents =
            [
                .. agents.Values
                    .OrderBy(static agent => agent.Name, StringComparer.Ordinal)
                    .Select(static agent => new ExportedAgent
                    {
                        SourceVersion = agent.Version,
                        Definition = DefinitionProjection.ToRequest(agent),
                    }),
            ],
            Workflows =
            [
                .. workflows.Values
                    .OrderBy(static workflow => workflow.Name, StringComparer.Ordinal)
                    .Select(static workflow => new ExportedWorkflow
                    {
                        Name = workflow.Name,
                        SourceVersion = workflow.Version,
                        Definition = DefinitionProjection.ToRequest(workflow),
                    }),
            ],
            Skills =
            [
                .. skills.Values
                    .OrderBy(static skill => skill.Name, StringComparer.Ordinal)
                    .Select(static skill => new ExportedSkill
                    {
                        SourceVersion = skill.Version,
                        Definition = DefinitionProjection.ToRequest(skill),
                    }),
            ],
            McpServers =
            [
                .. servers.Values
                    .OrderBy(static server => server.Name, StringComparer.Ordinal)
                    .Select(static server => new ExportedMcpServer
                    {
                        Name = server.Name,
                        Definition = DefinitionProjection.ToRequest(server),
                        HeaderNames = DefinitionProjection.HeaderNames(server),
                    }),
            ],
            Warnings = [.. warnings.Order(StringComparer.Ordinal)],
        };
    }

    private async ValueTask CollectByKindAsync(
        IReadOnlySet<string>? types,
        Dictionary<string, AgentDefinition> agents,
        Dictionary<string, WorkflowDefinition> workflows,
        Dictionary<string, AgentSkillDefinition> skills,
        Dictionary<string, McpServerDefinition> servers,
        CancellationToken cancellationToken)
    {
        bool Wants(string kind) => types is null || types.Contains(kind);

        if (Wants(DefinitionKinds.Agent))
        {
            foreach (var agent in await _agents.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                agents[agent.Name] = agent;
            }
        }

        if (Wants(DefinitionKinds.Workflow))
        {
            foreach (var workflow in await _workflows.ListAsync(_tenant.TenantId, cancellationToken).ConfigureAwait(false))
            {
                workflows[workflow.Name] = workflow;
            }
        }

        if (Wants(DefinitionKinds.Skill))
        {
            foreach (var skill in await _skills.ListAsync(_tenant.TenantId, cancellationToken).ConfigureAwait(false))
            {
                skills[skill.Name] = skill;
            }
        }

        if (Wants(DefinitionKinds.McpServer))
        {
            foreach (var server in await _servers.ListAsync(_tenant.TenantId, cancellationToken).ConfigureAwait(false))
            {
                servers[server.Name] = server;
            }
        }
    }

    private async ValueTask CollectByNameAsync(
        DefinitionExportSelection selection,
        Dictionary<string, AgentDefinition> agents,
        Dictionary<string, WorkflowDefinition> workflows,
        Dictionary<string, AgentSkillDefinition> skills,
        Dictionary<string, McpServerDefinition> servers,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var serverNames = (await _servers.ListAsync(_tenant.TenantId, cancellationToken).ConfigureAwait(false))
            .Select(static server => server.Name)
            .ToList();

        var pendingAgents = new Queue<string>();

        foreach (var name in selection.Agents)
        {
            if (!agents.ContainsKey(name))
            {
                // A named agent that is not stored is a request that cannot be met, not a reference to skip.
                agents[name] = await _agents.GetAsync(name, cancellationToken).ConfigureAwait(false)
                    ?? throw new DefinitionNotFoundException(DefinitionKinds.Agent, name);

                pendingAgents.Enqueue(name);
            }
        }

        foreach (var name in selection.Workflows)
        {
            if (workflows.ContainsKey(name))
            {
                continue;
            }

            var workflow = await _workflows.GetAsync(_tenant.TenantId, name, cancellationToken).ConfigureAwait(false)
                ?? throw new DefinitionNotFoundException(DefinitionKinds.Workflow, name);

            workflows[name] = workflow;

            foreach (var reference in DefinitionReferenceGraph.OfWorkflow(workflow))
            {
                await EnqueueAgentAsync(reference, workflow.Name, pendingAgents, agents, warnings, cancellationToken).ConfigureAwait(false);
            }
        }

        while (pendingAgents.Count > 0)
        {
            var agent = agents[pendingAgents.Dequeue()];

            foreach (var reference in DefinitionReferenceGraph.OfAgent(agent, serverNames))
            {
                switch (reference.Kind)
                {
                    case DefinitionReferenceKind.Agent:
                        await EnqueueAgentAsync(reference, agent.Name, pendingAgents, agents, warnings, cancellationToken).ConfigureAwait(false);
                        break;
                    case DefinitionReferenceKind.Skill:
                        if (!skills.ContainsKey(reference.Name) &&
                            await _skills.GetAsync(_tenant.TenantId, reference.Name, cancellationToken).ConfigureAwait(false) is { } skill)
                        {
                            // A skill defined in code or on disk is not stored; the target carries it itself.
                            skills[reference.Name] = skill;
                        }

                        break;
                    case DefinitionReferenceKind.McpServer:
                        if (!servers.ContainsKey(reference.Name) &&
                            await _servers.GetAsync(_tenant.TenantId, reference.Name, cancellationToken).ConfigureAwait(false) is { } server)
                        {
                            servers[reference.Name] = server;
                        }

                        break;
                }
            }
        }
    }

    private async ValueTask EnqueueAgentAsync(
        DefinitionReference reference,
        string owner,
        Queue<string> pending,
        Dictionary<string, AgentDefinition> agents,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (agents.ContainsKey(reference.Name))
        {
            return;
        }

        if (await _agents.GetAsync(reference.Name, cancellationToken).ConfigureAwait(false) is { } stored)
        {
            agents[reference.Name] = stored;
            pending.Enqueue(reference.Name);

            return;
        }

        warnings.Add(
            $"'{owner}' refers to agent '{reference.Name}' ({reference.Path}), which is not a stored agent. It is defined " +
            "in code, or in a custom source, or it does not exist; it is not in the file, and the importing host has to " +
            "carry it in its own code.");
    }

    // A pin names the version the SOURCE host gave an agent. The importing host can only translate a pin to an
    // agent the file holds at exactly that version, so any other pin is refused there; saying so here puts the
    // problem next to its cause, in the file's author's hands.
    private static IEnumerable<string> FindUntranslatablePins(
        Dictionary<string, AgentDefinition> agents,
        Dictionary<string, WorkflowDefinition> workflows)
    {
        bool Translatable(string name, int version)
            => agents.TryGetValue(name, out var agent) && agent.Version == version;

        foreach (var agent in agents.Values)
        {
            foreach (var reference in agent.CallableAgents)
            {
                if (reference.Version is { } version && !Translatable(reference.Name, version))
                {
                    yield return PinWarning(agent.Name, "callable agent", reference.Name, version, agents);
                }
            }

            if (agent.Harness?.Loop is { } loop)
            {
                foreach (var criterion in loop.Criteria)
                {
                    if (criterion.ReviewerAgent is { } reviewer && criterion.ReviewerAgentVersion is { } version &&
                        !Translatable(reviewer, version))
                    {
                        yield return PinWarning(agent.Name, "reviewer agent", reviewer, version, agents);
                    }
                }
            }
        }

        foreach (var workflow in workflows.Values)
        {
            foreach (var node in workflow.Nodes)
            {
                if (node is { Kind: WorkflowNodeKind.Agent, AgentVersion: { } version } && !Translatable(node.Name, version))
                {
                    yield return PinWarning(workflow.Name, "agent node", node.Name, version, agents);
                }
            }
        }
    }

    private static string PinWarning(string owner, string role, string name, int version, Dictionary<string, AgentDefinition> agents)
        => agents.TryGetValue(name, out var held)
            ? $"'{owner}' pins {role} '{name}' to version {version}, but the file holds version {held.Version} of '{name}'. " +
              "An import refuses this pin."
            : $"'{owner}' pins {role} '{name}' to version {version}, but the file does not hold '{name}'. An import refuses this pin.";
}
