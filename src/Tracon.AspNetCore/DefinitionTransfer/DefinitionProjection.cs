namespace Tracon;

/// <summary>
/// Projects a stored definition into the shape a bundle carries: the shape the save endpoint takes.
/// </summary>
/// <remarks>
/// <para>
/// A save takes a request, an export gives a stored definition back, and the two shapes differ (the stored one
/// carries the tenant, the version and the time). The projection is the way back from the stored shape to the
/// request. 🚨 A field a projection forgets is dropped from every export without an error, so
/// <c>DefinitionBundleFieldCoverageTests</c> makes every member of the four stored types either appear here or
/// be excluded by name.
/// </para>
/// </remarks>
internal static class DefinitionProjection
{
    /// <summary>Gets the members of <see cref="AgentDefinition"/> a bundle does not carry, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> AgentExclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(AgentDefinition.Origin)] = "A bundle holds database agents only; the importing host sets the origin.",
        [nameof(AgentDefinition.Version)] = "The importing host numbers its own versions (the file carries sourceVersion).",
        [nameof(AgentDefinition.TenantId)] = "A bundle carries no tenant: the import writes to the caller's tenant.",
        [nameof(AgentDefinition.UpdatedAt)] = "Set by the store at every write.",
    };

    /// <summary>Gets the members of <see cref="WorkflowDefinition"/> a bundle does not carry, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> WorkflowExclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(WorkflowDefinition.Name)] = "Carried beside the definition, as the path of the save endpoint carries it.",
        [nameof(WorkflowDefinition.TenantId)] = "A bundle carries no tenant: the import writes to the caller's tenant.",
        [nameof(WorkflowDefinition.Version)] = "The importing host numbers its own versions (the file carries sourceVersion).",
        [nameof(WorkflowDefinition.UpdatedAt)] = "Set by the store at every write.",
    };

    /// <summary>Gets the members of <see cref="AgentSkillDefinition"/> a bundle does not carry, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> SkillExclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(AgentSkillDefinition.Id)] = "Assigned by the store.",
        [nameof(AgentSkillDefinition.TenantId)] = "A bundle carries no tenant: the import writes to the caller's tenant.",
        [nameof(AgentSkillDefinition.Version)] = "The importing host numbers its own versions (the file carries sourceVersion).",
        [nameof(AgentSkillDefinition.Origin)] = "A bundle holds stored skills only.",
        [nameof(AgentSkillDefinition.CreatedAt)] = "Set by the store.",
        [nameof(AgentSkillDefinition.UpdatedAt)] = "Set by the store at every write.",
    };

    /// <summary>Gets the members of <see cref="McpServerDefinition"/> a bundle does not carry, with the reason.</summary>
    public static IReadOnlyDictionary<string, string> McpServerExclusions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(McpServerDefinition.Id)] = "Assigned by the store.",
        [nameof(McpServerDefinition.TenantId)] = "A bundle carries no tenant: the import writes to the caller's tenant.",
        [nameof(McpServerDefinition.Name)] = "Carried beside the definition, as the path of the save endpoint carries it.",
        [nameof(McpServerDefinition.Headers)] = "A plain header VALUE is never written to a file; the names travel in headerNames.",
        [nameof(McpServerDefinition.CreatedAt)] = "Set by the store.",
        [nameof(McpServerDefinition.UpdatedAt)] = "Set by the store at every write.",
    };

    /// <summary>Projects an agent definition into its request shape.</summary>
    /// <param name="definition">The stored definition.</param>
    /// <returns>The request that would save the same content.</returns>
    public static AgentDefinitionRequest ToRequest(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new AgentDefinitionRequest
        {
            Name = definition.Name,
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Instructions = definition.Instructions,
            InstructionsByCulture = definition.InstructionsByCulture,
            Model = definition.Model,
            ToolNames = definition.ToolNames,
            SkillNames = definition.SkillNames,
            Skills = definition.Skills,
            Workspace = definition.Workspace,
            ExtensionNames = definition.ExtensionNames,
            CallableAgents = definition.CallableAgents,
            SubAgents = definition.SubAgents,
            McpResourceUris = definition.McpResourceUris,
            Harness = definition.Harness,
            Compaction = definition.Compaction,
            Memory = definition.Memory,
            Metadata = definition.Metadata,
            Parameters = definition.Parameters,
            SharedInstructionsName = definition.SharedInstructionsName,
        };
    }

    /// <summary>Projects a workflow definition into its request shape.</summary>
    /// <param name="definition">The stored definition.</param>
    /// <returns>The request that would save the same content.</returns>
    public static WorkflowSaveRequest ToRequest(WorkflowDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new WorkflowSaveRequest
        {
            DisplayName = definition.DisplayName,
            Description = definition.Description,
            Kind = definition.Kind,
            AgentNames = definition.AgentNames,
            Nodes = definition.Nodes,
            ManagerAgentName = definition.ManagerAgentName,
            MaxIterations = definition.MaxIterations,
            HandoffInstructions = definition.HandoffInstructions,
            RequirePlanApproval = definition.RequirePlanApproval,
            Parameters = definition.Parameters,
            RunTimeout = definition.RunTimeout,
            ResponseMode = definition.ResponseMode,
            RequestTimeout = definition.RequestTimeout,
        };
    }

    /// <summary>Projects a stored skill into its request shape.</summary>
    /// <param name="skill">The stored skill.</param>
    /// <returns>The request that would save the same content.</returns>
    public static AgentSkillRequest ToRequest(AgentSkillDefinition skill)
    {
        ArgumentNullException.ThrowIfNull(skill);

        return new AgentSkillRequest
        {
            Name = skill.Name,
            Description = skill.Description,
            Instructions = skill.Instructions,
            Compatibility = skill.Compatibility,
            License = skill.License,
            AllowedTools = skill.AllowedTools,
            Metadata = skill.Metadata,
            Enabled = skill.Enabled,
            Resources = skill.Resources,
            Scripts = skill.Scripts,
        };
    }

    /// <summary>Projects an MCP server into its request shape, without a plain header value.</summary>
    /// <param name="server">The stored server.</param>
    /// <returns>
    /// The request that would save the same content. <c>headers</c> is <see langword="null"/> (keep what the
    /// target holds); the credential header keys are the stored names.
    /// </returns>
    public static McpServerRequest ToRequest(McpServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return new McpServerRequest
        {
            Description = server.Description,
            Endpoint = server.Endpoint.AbsoluteUri,
            Transport = server.Transport,
            Headers = null,
            HeaderConfigurationKeys = new Dictionary<string, string>(server.HeaderConfigurationKeys, StringComparer.OrdinalIgnoreCase),
            Enabled = server.Enabled,
            RequiresApproval = server.RequiresApproval,
            OAuthEnabled = server.OAuthEnabled,
            OAuthClientId = server.OAuthClientId,
            OAuthClientSecretConfigurationKey = server.OAuthClientSecretConfigurationKey,
            OAuthScopes = server.OAuthScopes,
            OAuthAuthorizationMode = server.OAuthAuthorizationMode,
        };
    }

    /// <summary>Lists the names of an MCP server's plain headers, in name order.</summary>
    /// <param name="server">The stored server.</param>
    /// <returns>The names; their values are not returned.</returns>
    public static IReadOnlyList<string> HeaderNames(McpServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);

        return [.. server.Headers.Keys.Order(StringComparer.Ordinal)];
    }
}
