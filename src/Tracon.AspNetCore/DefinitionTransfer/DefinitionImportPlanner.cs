using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Tracon;

/// <summary>One item of an import, as the planner understood it.</summary>
internal sealed class PlannedItem
{
    private readonly List<DefinitionImportMessage> _messages = [];

    /// <summary>Gets the kind (<see cref="DefinitionKinds"/>).</summary>
    public required string Type { get; init; }

    /// <summary>Gets the name of the definition.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the version the item had in the file.</summary>
    public int? SourceVersion { get; set; }

    /// <summary>Gets or sets the action (<see cref="DefinitionImportActions"/>).</summary>
    public string Action { get; set; } = DefinitionImportActions.Skipped;

    /// <summary>Gets or sets the version the item has, or is expected to have, in the target.</summary>
    public int? TargetVersion { get; set; }

    /// <summary>Gets or sets the canonical definition the target holds now.</summary>
    public JsonElement? Current { get; set; }

    /// <summary>Gets or sets the canonical definition the import would write.</summary>
    public JsonElement? Incoming { get; set; }

    /// <summary>Gets or sets the paths where <see cref="Current"/> and <see cref="Incoming"/> differ.</summary>
    public IReadOnlyList<string> ChangedPaths { get; set; } = [];

    /// <summary>Gets or sets the agent as the file carries it, before its pins are translated.</summary>
    public AgentDefinitionRequest? AgentRequest { get; set; }

    /// <summary>Gets or sets the workflow as the file carries it, before its pins are translated.</summary>
    public WorkflowSaveRequest? WorkflowRequest { get; set; }

    /// <summary>Gets or sets the skill as the file carries it.</summary>
    public AgentSkillRequest? SkillRequest { get; set; }

    /// <summary>Gets or sets the definition the import saves, for a server.</summary>
    public McpServerDefinition? McpServer { get; set; }

    /// <summary>Gets or sets the agent as it would be saved, with its pins translated.</summary>
    public AgentDefinition? AgentCandidate { get; set; }

    /// <summary>Gets the findings of this item.</summary>
    public IReadOnlyList<DefinitionImportMessage> Messages => _messages;

    /// <summary>Gets a value that tells whether the import writes this item.</summary>
    public bool Writes => Action is DefinitionImportActions.Create or DefinitionImportActions.NewVersion or DefinitionImportActions.Update;

    /// <summary>Gets a value that tells whether a finding of this item stops the import.</summary>
    public bool HasError => _messages.Exists(static message => message.Severity == ValidationSeverity.Error);

    /// <summary>Adds a finding.</summary>
    /// <param name="message">The finding.</param>
    public void Add(DefinitionImportMessage message) => _messages.Add(message);

    /// <summary>Adds the findings of a write rule and turns the item into a refused one.</summary>
    /// <param name="finding">The refusal.</param>
    public void Reject(WriteRuleFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);

        foreach (var message in finding.ToMessages())
        {
            Add(DefinitionImportMessage.From(message));
        }

        Action = DefinitionImportActions.Rejected;
    }

    /// <summary>Adds an error and turns the item into a refused one.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The description.</param>
    /// <param name="path">The offending field, if one is known.</param>
    public void Reject(string code, string message, string? path = null)
    {
        Add(new DefinitionImportMessage { Severity = ValidationSeverity.Error, Code = code, Message = message, Path = path });
        Action = DefinitionImportActions.Rejected;
    }

    /// <summary>Adds a warning.</summary>
    /// <param name="code">The stable code.</param>
    /// <param name="message">The description.</param>
    /// <param name="path">The field, if one is known.</param>
    public void Warn(string code, string message, string? path = null)
        => Add(new DefinitionImportMessage { Severity = ValidationSeverity.Warning, Code = code, Message = message, Path = path });
}

/// <summary>What the planner found: the items in the order an import writes them, and what it knows for the write.</summary>
internal sealed class DefinitionImportPlan
{
    /// <summary>Gets the items, in writing order: skills, MCP servers, agents (callees first), workflows.</summary>
    public required IReadOnlyList<PlannedItem> Items { get; init; }

    /// <summary>Gets the version each agent of the file had in the source host.</summary>
    public required IReadOnlyDictionary<string, int> SourceVersions { get; init; }

    /// <summary>Gets the version each imported agent has, or will have, in the target.</summary>
    public required IReadOnlyDictionary<string, int> TargetVersions { get; init; }

    /// <summary>Gets a value that tells whether every item can be written.</summary>
    public bool Valid => !Items.Any(static item => item.HasError);
}

/// <summary>
/// Reads a bundle against the target and decides, for every item, what an import would do and whether it may.
/// </summary>
/// <remarks>
/// <para>
/// The planner writes nothing. It applies the same write rules the save endpoints apply
/// (<see cref="AgentDefinitionWriteRules"/> and its siblings) to every item, with the agents and skills
/// of the bundle laid over the target (<see cref="DefinitionOverlay"/>) so a reference between two items of the
/// bundle is judged as it will be once both are saved.
/// </para>
/// <para>
/// The order matters. A version pin names the version the source host gave an agent, which means nothing
/// in the target: it is translated to the version the target will hold. That version depends on whether the agent
/// is new, changed or the same, which depends on its own pins, so agents are planned callees first.
/// </para>
/// </remarks>
internal sealed class DefinitionImportPlanner
{
    private readonly IAgentDefinitionStore _agents;
    private readonly IAgentCatalog _catalog;
    private readonly AgentDefinitionValidator _validator;
    private readonly IWorkflowDefinitionStore _workflows;
    private readonly IWorkflowFunctionCatalog? _functions;
    private readonly IWorkflowRunTimeoutPolicy? _runTimeoutPolicy;
    private readonly IAgentSkillStore _skills;
    private readonly AgentSkillCatalog _skillCatalog;
    private readonly TraconOptions _options;
    private readonly IMcpServerStore _servers;
    private readonly TraconEgressOptions _egress;
    private readonly TraconMcpSecurityOptions _mcpSecurity;
    private readonly ITenantContext _tenant;
    private readonly HttpContext _httpContext;

    /// <summary>Initializes a planner.</summary>
    /// <param name="agents">The agent store.</param>
    /// <param name="catalog">The agent catalog.</param>
    /// <param name="validator">The agent validator.</param>
    /// <param name="workflows">The workflow store.</param>
    /// <param name="functions">The workflow function registry; <see langword="null"/> when no engine is registered.</param>
    /// <param name="runTimeoutPolicy">The workflow run timeout policy; <see langword="null"/> without the engine.</param>
    /// <param name="skills">The skill store.</param>
    /// <param name="skillCatalog">The skill catalog.</param>
    /// <param name="options">The core options.</param>
    /// <param name="servers">The MCP server store.</param>
    /// <param name="egress">The egress options.</param>
    /// <param name="mcpSecurity">The MCP security options.</param>
    /// <param name="tenant">The caller's tenant.</param>
    /// <param name="httpContext">The request, whose API key scopes the MCP rules read.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public DefinitionImportPlanner(
        IAgentDefinitionStore agents,
        IAgentCatalog catalog,
        AgentDefinitionValidator validator,
        IWorkflowDefinitionStore workflows,
        IWorkflowFunctionCatalog? functions,
        IWorkflowRunTimeoutPolicy? runTimeoutPolicy,
        IAgentSkillStore skills,
        AgentSkillCatalog skillCatalog,
        TraconOptions options,
        IMcpServerStore servers,
        TraconEgressOptions egress,
        TraconMcpSecurityOptions mcpSecurity,
        ITenantContext tenant,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(workflows);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(skillCatalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(egress);
        ArgumentNullException.ThrowIfNull(mcpSecurity);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(httpContext);

        _agents = agents;
        _catalog = catalog;
        _validator = validator;
        _workflows = workflows;
        _functions = functions;
        _runTimeoutPolicy = runTimeoutPolicy;
        _skills = skills;
        _skillCatalog = skillCatalog;
        _options = options;
        _servers = servers;
        _egress = egress;
        _mcpSecurity = mcpSecurity;
        _tenant = tenant;
        _httpContext = httpContext;
    }

    /// <summary>Plans an import.</summary>
    /// <param name="bundle">The bundle.</param>
    /// <param name="types">The kinds the call imports; <see langword="null"/> imports all of them.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The plan.</returns>
    public async ValueTask<DefinitionImportPlan> PlanAsync(
        DefinitionBundle bundle,
        IReadOnlySet<string>? types,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        bool Selected(string kind) => types is null || types.Contains(kind);

        var skillItems = await PlanSkillsAsync(bundle.Skills, Selected(DefinitionKinds.Skill), cancellationToken).ConfigureAwait(false);
        var serverItems = await PlanServersAsync(bundle.McpServers, Selected(DefinitionKinds.McpServer), cancellationToken).ConfigureAwait(false);

        var pendingServers = serverItems
            .Where(static item => item.Action is DefinitionImportActions.Create or DefinitionImportActions.Update)
            .Select(static item => item.Name)
            .ToHashSet(StringComparer.Ordinal);

        var sourceVersions = bundle.Agents
            .Where(static agent => !string.IsNullOrWhiteSpace(agent.Definition.Name))
            .GroupBy(static agent => agent.Definition.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().SourceVersion, StringComparer.Ordinal);

        var targetVersions = new Dictionary<string, int>(StringComparer.Ordinal);

        var agentItems = await PlanAgentsAsync(
            bundle.Agents,
            Selected(DefinitionKinds.Agent),
            sourceVersions,
            targetVersions,
            cancellationToken).ConfigureAwait(false);

        var overlay = CreateOverlay(agentItems, skillItems);

        if (Selected(DefinitionKinds.Agent))
        {
            await ValidateAgentsAsync(agentItems, overlay, pendingServers, cancellationToken).ConfigureAwait(false);
        }

        var workflowItems = await PlanWorkflowsAsync(
            bundle.Workflows,
            Selected(DefinitionKinds.Workflow),
            overlay,
            sourceVersions,
            targetVersions,
            cancellationToken).ConfigureAwait(false);

        return new DefinitionImportPlan
        {
            Items = [.. skillItems, .. serverItems, .. agentItems, .. workflowItems],
            SourceVersions = sourceVersions,
            TargetVersions = targetVersions,
        };
    }

    /// <summary>Translates the version pins of an agent to the versions the target holds.</summary>
    /// <param name="request">The agent as the file carries it.</param>
    /// <param name="sourceVersions">The version each agent of the file had in the source host.</param>
    /// <param name="targetVersions">The version each imported agent has, or will have, in the target.</param>
    /// <param name="errors">Receives a finding for every pin that cannot be translated.</param>
    /// <returns>The agent with every pin translated; the pins that cannot be translated are left as they were.</returns>
    public static AgentDefinitionRequest TranslatePins(
        AgentDefinitionRequest request,
        IReadOnlyDictionary<string, int> sourceVersions,
        IReadOnlyDictionary<string, int> targetVersions,
        List<DefinitionImportMessage> errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        var callable = request.CallableAgents;

        if (callable.Any(static reference => reference.Version is not null))
        {
            callable =
            [
                .. callable.Select((reference, index) => reference.Version is { } version
                    ? reference with
                    {
                        Version = Translate(request.Name, "callable agent", reference.Name, version, $"callableAgents[{index}].version", sourceVersions, targetVersions, errors),
                    }
                    : reference),
            ];
        }

        var harness = request.Harness;

        if (harness?.Loop is { } loop && loop.Criteria.Any(static criterion => criterion.ReviewerAgentVersion is not null))
        {
            harness = harness with
            {
                Loop = loop with
                {
                    Criteria =
                    [
                        .. loop.Criteria.Select((criterion, index) => criterion is { ReviewerAgent: { } reviewer, ReviewerAgentVersion: { } version }
                            ? criterion with
                            {
                                ReviewerAgentVersion = Translate(
                                    request.Name,
                                    "reviewer agent",
                                    reviewer,
                                    version,
                                    $"harness.loop.criteria[{index}].reviewerAgentVersion",
                                    sourceVersions,
                                    targetVersions,
                                    errors),
                            }
                            : criterion),
                    ],
                },
            };
        }

        return request with { CallableAgents = callable, Harness = harness };
    }

    /// <summary>Translates the version pins of a workflow's agent nodes to the versions the target holds.</summary>
    /// <param name="name">The workflow name.</param>
    /// <param name="request">The workflow as the file carries it.</param>
    /// <param name="sourceVersions">The version each agent of the file had in the source host.</param>
    /// <param name="targetVersions">The version each imported agent has, or will have, in the target.</param>
    /// <param name="errors">Receives a finding for every pin that cannot be translated.</param>
    /// <returns>The workflow with every pin translated.</returns>
    public static WorkflowSaveRequest TranslatePins(
        string name,
        WorkflowSaveRequest request,
        IReadOnlyDictionary<string, int> sourceVersions,
        IReadOnlyDictionary<string, int> targetVersions,
        List<DefinitionImportMessage> errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(errors);

        if (request.Nodes is not { } nodes || !nodes.Any(static node => node.AgentVersion is not null))
        {
            return request;
        }

        return request with
        {
            Nodes =
            [
                .. nodes.Select((node, index) => node is { Kind: WorkflowNodeKind.Agent, AgentVersion: { } version }
                    ? node with
                    {
                        AgentVersion = Translate(name, "agent node", node.Name, version, $"nodes[{index}].agentVersion", sourceVersions, targetVersions, errors),
                    }
                    : node),
            ],
        };
    }

    private static int Translate(
        string owner,
        string role,
        string agent,
        int version,
        string path,
        IReadOnlyDictionary<string, int> sourceVersions,
        IReadOnlyDictionary<string, int> targetVersions,
        List<DefinitionImportMessage> errors)
    {
        if (sourceVersions.TryGetValue(agent, out var held) && held == version && targetVersions.TryGetValue(agent, out var target))
        {
            return target;
        }

        var reason = !sourceVersions.TryGetValue(agent, out held)
            ? $"the file does not hold agent '{agent}'"
            : held != version
                ? $"the file holds version {held} of agent '{agent}'"
                : $"agent '{agent}' is not imported by this call";

        errors.Add(new DefinitionImportMessage
        {
            Severity = ValidationSeverity.Error,
            Code = DefinitionImportCodes.PinUntranslatable,
            Message = $"'{owner}' pins {role} '{agent}' to version {version}, but {reason}. A version number is the source " +
                      "host's own, so an import can only translate a pin to an agent the file holds at exactly that version. " +
                      "Leave out the version to run the current one, or put the version in the file.",
            Path = path,
        });

        return version;
    }

    private async ValueTask<List<PlannedItem>> PlanSkillsAsync(
        IReadOnlyList<ExportedSkill> exported,
        bool selected,
        CancellationToken cancellationToken)
    {
        var items = new List<PlannedItem>(exported.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in exported)
        {
            var request = entry.Definition;
            var item = new PlannedItem { Type = DefinitionKinds.Skill, Name = request.Name, SourceVersion = entry.SourceVersion };

            items.Add(item);

            if (!selected)
            {
                continue;
            }

            if (!seen.Add(request.Name))
            {
                item.Reject(DefinitionImportCodes.DuplicateName, $"The file holds skill '{request.Name}' more than once.");

                continue;
            }

            if ((AgentSkillWriteRules.CodeDefinedName(_skillCatalog, request.Name)
                 ?? AgentSkillWriteRules.Validate(request, _options.Skills)
                 ?? AgentSkillWriteRules.ValidateScripts(request, _options.Skills.Scripts)) is { } finding)
            {
                item.Reject(finding);

                continue;
            }

            item.SkillRequest = request;

            var existing = await _skills.GetAsync(_tenant.TenantId, request.Name, cancellationToken).ConfigureAwait(false);
            var incoming = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(request.ToDefinition(_tenant.TenantId)));

            item.Incoming = incoming;
            item.TargetVersion = (existing?.Version ?? 0) + 1;

            if (existing is null)
            {
                item.Action = DefinitionImportActions.Create;

                continue;
            }

            var current = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(existing));

            item.Current = current;

            if (DefinitionCanonicalJson.AreEqual(current, incoming))
            {
                item.Action = DefinitionImportActions.Unchanged;
                item.TargetVersion = existing.Version;

                continue;
            }

            item.Action = DefinitionImportActions.Update;
            item.ChangedPaths = DefinitionCanonicalJson.ChangedPaths(current, incoming);

            if (item.ChangedPaths.Any(static path => path.StartsWith("scripts", StringComparison.Ordinal)))
            {
                item.Warn(
                    DefinitionImportCodes.ScriptPermissionReset,
                    $"A script of skill '{request.Name}' changed. A permission to run it pins the content it was granted for, " +
                    "so the old permission no longer holds: grant it again in the target.",
                    "scripts");
            }
        }

        return items;
    }

    private async ValueTask<List<PlannedItem>> PlanServersAsync(
        IReadOnlyList<ExportedMcpServer> exported,
        bool selected,
        CancellationToken cancellationToken)
    {
        var items = new List<PlannedItem>(exported.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in exported)
        {
            var name = entry.Name;
            var request = entry.Definition;
            var item = new PlannedItem { Type = DefinitionKinds.McpServer, Name = name };

            items.Add(item);

            if (!selected)
            {
                continue;
            }

            if (!seen.Add(name))
            {
                item.Reject(DefinitionImportCodes.DuplicateName, $"The file holds MCP server '{name}' more than once.");

                continue;
            }

            var tenantId = _tenant.TenantId;

            if (McpServerWriteRules.Validate(name, request, tenantId, _options.DefaultTenantId, _egress, _mcpSecurity) is { } invalid)
            {
                item.Reject(invalid);

                continue;
            }

            var previous = await _servers.GetAsync(tenantId, name, cancellationToken).ConfigureAwait(false);
            var (headers, headerKeys) = McpServerWriteRules.EffectiveHeaders(request, previous);

            if (McpServerWriteRules.ValidateHeaders(request, headers, headerKeys, tenantId, _options.DefaultTenantId, _mcpSecurity) is { } invalidHeaders)
            {
                item.Reject(invalidHeaders);

                continue;
            }

            var endpoint = new Uri(request.Endpoint, UriKind.Absolute);

            if (McpServerWriteRules.RejectApprovalWaiverWithoutSecurityAdmin(_httpContext, name, previous, endpoint, request) is { } denied)
            {
                item.Reject(denied);

                continue;
            }

            var candidate = McpServerWriteRules.ToDefinition(name, request, endpoint, tenantId, headers, headerKeys);
            var incoming = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(candidate));

            item.McpServer = candidate;
            item.Incoming = incoming;

            if (previous is null)
            {
                item.Action = DefinitionImportActions.Create;
            }
            else
            {
                var current = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(previous));
                var paths = DefinitionCanonicalJson.ChangedPaths(current, incoming).ToList();

                // A plain header value is not in the projection, so a file that carries values is compared on them
                // here, and the report names the field without the value.
                if (request.Headers is not null && !SameHeaders(headers, previous.Headers))
                {
                    paths.Add("headers");
                }

                item.Current = current;
                item.ChangedPaths = paths;
                item.Action = paths.Count == 0 ? DefinitionImportActions.Unchanged : DefinitionImportActions.Update;
            }

            if (!string.Equals(item.Action, DefinitionImportActions.Unchanged, StringComparison.Ordinal))
            {
                AddServerWarnings(item, entry, request, previous, endpoint, headers);
            }
        }

        return items;
    }

    private static bool SameHeaders(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count &&
           left.All(pair => right.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));

    private static void AddServerWarnings(
        PlannedItem item,
        ExportedMcpServer entry,
        McpServerRequest request,
        McpServerDefinition? previous,
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers)
    {
        var missing = entry.HeaderNames.Where(header => !headers.ContainsKey(header)).ToList();

        if (missing.Count > 0)
        {
            item.Warn(
                DefinitionImportCodes.McpHeaderValuesNeeded,
                $"MCP server '{item.Name}' sends the plain headers {string.Join(", ", missing)}. A file never carries a plain " +
                "header value: enter the values in the target before an agent calls the server.",
                "headers");
        }

        if (request.OAuthEnabled)
        {
            item.Warn(
                DefinitionImportCodes.McpOAuthReauthorize,
                $"MCP server '{item.Name}' uses OAuth. A token is not carried across hosts: authorize the server again in the target.",
                "oauthEnabled");
        }

        var kept = McpServerWriteRules.HeaderNamesKeptAcrossHosts(previous, endpoint, request);

        if (kept.Count > 0)
        {
            item.Warn(
                DefinitionImportCodes.McpHeadersKeptAcrossHosts,
                $"MCP server '{item.Name}' moves to host '{endpoint.Host}' and keeps the stored headers ({string.Join(", ", kept)}); " +
                "they are sent to the new host.",
                "endpoint");
        }
    }

    private async ValueTask<List<PlannedItem>> PlanAgentsAsync(
        IReadOnlyList<ExportedAgent> exported,
        bool selected,
        IReadOnlyDictionary<string, int> sourceVersions,
        Dictionary<string, int> targetVersions,
        CancellationToken cancellationToken)
    {
        var items = new List<PlannedItem>(exported.Count);

        if (!selected)
        {
            items.AddRange(exported.Select(static entry => new PlannedItem
            {
                Type = DefinitionKinds.Agent,
                Name = entry.Definition.Name,
                SourceVersion = entry.SourceVersion,
            }));

            return items;
        }

        var unique = new Dictionary<string, ExportedAgent>(StringComparer.Ordinal);

        foreach (var entry in exported)
        {
            var name = entry.Definition.Name;
            var item = new PlannedItem { Type = DefinitionKinds.Agent, Name = name, SourceVersion = entry.SourceVersion };

            if (string.IsNullOrWhiteSpace(name))
            {
                item.Reject(AgentDefinitionWriteRules.Validate(entry.Definition)
                    ?? WriteRuleFinding.BadRequest("Agent name empty", "'name' is required.", WriteRuleCodes.AgentNameEmpty, "name"));
                items.Add(item);
            }
            else if (!unique.TryAdd(name, entry))
            {
                item.Reject(DefinitionImportCodes.DuplicateName, $"The file holds agent '{name}' more than once.");
                items.Add(item);
            }
        }

        var incoming = unique.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Definition.ToDefinition(),
            StringComparer.Ordinal);

        var descriptors = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var name in DefinitionReferenceGraph.OrderAgents(incoming))
        {
            var entry = unique[name];
            var request = entry.Definition;
            var item = new PlannedItem
            {
                Type = DefinitionKinds.Agent,
                Name = name,
                SourceVersion = entry.SourceVersion,
                AgentRequest = request,
            };

            items.Add(item);

            var existing = await _agents.GetAsync(name, cancellationToken).ConfigureAwait(false);
            var descriptor = descriptors.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

            // The name is spoken for either way: an agent that refers to this one still has a version to be pinned to,
            // and it should not be told the pin cannot be translated because of something else that is wrong.
            targetVersions[name] = existing?.Version ?? 1;

            if (AgentDefinitionWriteRules.GuardCodeAgent(descriptor, name) is { } guard)
            {
                item.Reject(guard);

                continue;
            }

            var pinErrors = new List<DefinitionImportMessage>();
            var translated = TranslatePins(request, sourceVersions, targetVersions, pinErrors);

            foreach (var error in pinErrors)
            {
                item.Add(error);
            }

            var candidate = translated.ToDefinition();
            var incomingCanonical = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(candidate));

            item.Incoming = incomingCanonical;

            if (existing is null)
            {
                item.Action = DefinitionImportActions.Create;
                item.TargetVersion = 1;
            }
            else
            {
                var current = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(existing));

                item.Current = current;

                if (DefinitionCanonicalJson.AreEqual(current, incomingCanonical))
                {
                    item.Action = DefinitionImportActions.Unchanged;
                    item.TargetVersion = existing.Version;
                }
                else
                {
                    item.Action = DefinitionImportActions.NewVersion;
                    item.TargetVersion = existing.Version + 1;
                    item.ChangedPaths = DefinitionCanonicalJson.ChangedPaths(current, incomingCanonical);
                }
            }

            targetVersions[name] = item.TargetVersion!.Value;
            item.AgentCandidate = candidate with { Version = item.TargetVersion.Value };

            if (item.HasError)
            {
                item.Action = DefinitionImportActions.Rejected;
            }
        }

        return items;
    }

    private static DefinitionOverlay CreateOverlay(IReadOnlyList<PlannedItem> agentItems, IReadOnlyList<PlannedItem> skillItems)
        => new(
            agentItems.Where(static item => item.AgentCandidate is not null).Select(static item => item.AgentCandidate!),
            skillItems
                .Where(static item => item.SkillRequest is not null && !string.Equals(item.Action, DefinitionImportActions.Rejected, StringComparison.Ordinal))
                .Select(static item => item.Name));

    private async ValueTask ValidateAgentsAsync(
        List<PlannedItem> agentItems,
        DefinitionOverlay overlay,
        IReadOnlySet<string> pendingServers,
        CancellationToken cancellationToken)
    {
        var catalog = new OverlayAgentCatalog(overlay, _catalog);

        foreach (var item in agentItems)
        {
            // An agent the import does not write is not judged again: it is what the target already holds.
            if (item.AgentCandidate is null || item.Action is not (DefinitionImportActions.Create or DefinitionImportActions.NewVersion))
            {
                continue;
            }

            var request = DefinitionProjection.ToRequest(item.AgentCandidate);

            if (FindPendingServers(item.AgentCandidate, pendingServers) is { Count: > 0 } pending)
            {
                item.Action = DefinitionImportActions.Blocked;
                item.Add(new DefinitionImportMessage
                {
                    Severity = ValidationSeverity.Error,
                    Code = DefinitionImportCodes.McpStageRequired,
                    Message = $"Agent '{item.Name}' uses a tool of the MCP server {string.Join(", ", pending.Select(static server => $"'{server}'"))}, " +
                              "which this import would create or change. A server's tool list comes from the live server, so the " +
                              "import cannot check the tool before the server is saved. Import in two steps: first the servers " +
                              "(types=mcpServers), authorizing OAuth if a server uses it, then the rest.",
                    Path = "toolNames",
                });

                continue;
            }

            var finding = AgentDefinitionWriteRules.Validate(request)
                ?? await AgentDefinitionWriteRules.ValidateCallGraphAsync(catalog, request, cancellationToken).ConfigureAwait(false);

            if (finding is not null)
            {
                item.Reject(finding);

                continue;
            }

            var check = await AgentDefinitionWriteRules
                .ValidateEntitiesAsync(_validator, request, overlay, cancellationToken)
                .ConfigureAwait(false);

            if (check.Finding is { } entities)
            {
                item.Reject(entities);
            }

            foreach (var warning in check.Report.Messages.Where(static message => message.Severity != ValidationSeverity.Error))
            {
                item.Add(DefinitionImportMessage.From(warning));
            }
        }
    }

    private static List<string> FindPendingServers(AgentDefinition agent, IReadOnlySet<string> pendingServers)
    {
        if (pendingServers.Count == 0)
        {
            return [];
        }

        return
        [
            .. DefinitionReferenceGraph.OfAgent(agent, [.. pendingServers])
                .Where(static reference => reference.Kind == DefinitionReferenceKind.McpServer && reference.Path.StartsWith("toolNames", StringComparison.Ordinal))
                .Select(static reference => reference.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private async ValueTask<List<PlannedItem>> PlanWorkflowsAsync(
        IReadOnlyList<ExportedWorkflow> exported,
        bool selected,
        DefinitionOverlay overlay,
        IReadOnlyDictionary<string, int> sourceVersions,
        IReadOnlyDictionary<string, int> targetVersions,
        CancellationToken cancellationToken)
    {
        var items = new List<PlannedItem>(exported.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var catalog = new OverlayAgentCatalog(overlay, _catalog);
        var store = new OverlayAgentDefinitionStore(overlay, _agents);

        foreach (var entry in exported)
        {
            var name = entry.Name;
            var item = new PlannedItem
            {
                Type = DefinitionKinds.Workflow,
                Name = name,
                SourceVersion = entry.SourceVersion,
                WorkflowRequest = entry.Definition,
            };

            items.Add(item);

            if (!selected)
            {
                continue;
            }

            if (!seen.Add(name))
            {
                item.Reject(DefinitionImportCodes.DuplicateName, $"The file holds workflow '{name}' more than once.");

                continue;
            }

            var pinErrors = new List<DefinitionImportMessage>();
            var translated = TranslatePins(name, entry.Definition, sourceVersions, targetVersions, pinErrors);

            foreach (var error in pinErrors)
            {
                item.Add(error);
            }

            var candidate = translated.ToDefinition(name);
            var incoming = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(candidate));
            var existing = await _workflows.GetAsync(_tenant.TenantId, name, cancellationToken).ConfigureAwait(false);

            item.Incoming = incoming;

            if (existing is null)
            {
                item.Action = DefinitionImportActions.Create;
                item.TargetVersion = 1;
            }
            else
            {
                var current = DefinitionCanonicalJson.ToCanonical(DefinitionProjection.ToRequest(existing));

                item.Current = current;

                if (DefinitionCanonicalJson.AreEqual(current, incoming))
                {
                    item.Action = DefinitionImportActions.Unchanged;
                    item.TargetVersion = existing.Version;
                }
                else
                {
                    item.Action = DefinitionImportActions.NewVersion;
                    item.TargetVersion = existing.Version + 1;
                    item.ChangedPaths = DefinitionCanonicalJson.ChangedPaths(current, incoming);
                }
            }

            if (item.HasError)
            {
                item.Action = DefinitionImportActions.Rejected;

                continue;
            }

            if (string.Equals(item.Action, DefinitionImportActions.Unchanged, StringComparison.Ordinal))
            {
                continue;
            }

            if (await WorkflowDefinitionWriteRules
                    .ValidateDefinitionAsync(candidate, _functions, _runTimeoutPolicy, catalog, store, because: null, cancellationToken)
                    .ConfigureAwait(false) is { } finding)
            {
                item.Reject(finding);
            }
        }

        return items;
    }
}
