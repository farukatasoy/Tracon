using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Validates an <see cref="AgentDefinition"/> without saving it or calling a model.
/// </summary>
/// <remarks>
/// <para>
/// Validation is valuable only when it does what the real compilation path,
/// <see cref="AgentDefinitionCompiler"/>, would do. Because the compiler throws
/// on its first error, this type runs every check <strong>independently and in
/// order</strong>, gathers all findings, then calls
/// <see cref="AgentDefinitionCompiler.Compile(AgentDefinition)"/> last to find
/// remaining structural errors, such as reasoning effort or compaction settings.
/// </para>
/// <para>
/// The compiled <see cref="Microsoft.Agents.AI.AIAgent"/> is discarded. No
/// <c>runs</c> row is opened and no token is spent.
/// </para>
/// </remarks>
public sealed class AgentDefinitionValidator
{
    private readonly IModelProviderRegistry _models;
    private readonly IToolRegistry _tools;
    private readonly AgentSkillCatalog _skills;
    private readonly IAgentCatalog _catalog;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly IOptions<AgentPrismOptions> _options;
    private readonly IMcpToolRefresher? _mcpRefresher;

    /// <summary>Initializes an agent-definition validator.</summary>
    /// <param name="models">The model provider registry.</param>
    /// <param name="tools">The tool registry.</param>
    /// <param name="skills">The skill catalog.</param>
    /// <param name="catalog">The agent catalog used to validate the call graph.</param>
    /// <param name="compiler">The actual compiler run when independent validation passes.</param>
    /// <param name="options">
    /// The options that provide <see cref="AgentPrismValidationOptions.McpTimeout"/>.
    /// </param>
    /// <param name="mcpRefresher">
    /// When registered, refreshes the MCP tool list after a missing tool name is
    /// found. When <see langword="null"/>, <c>AgentPrism.Mcp</c> is not registered
    /// and missing tool names are reported directly as errors.
    /// </param>
    /// <exception cref="ArgumentNullException">A required dependency is <see langword="null"/>.</exception>
    public AgentDefinitionValidator(
        IModelProviderRegistry models,
        IToolRegistry tools,
        AgentSkillCatalog skills,
        IAgentCatalog catalog,
        AgentDefinitionCompiler compiler,
        IOptions<AgentPrismOptions> options,
        IMcpToolRefresher? mcpRefresher = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(options);

        _models = models;
        _tools = tools;
        _skills = skills;
        _catalog = catalog;
        _compiler = compiler;
        _options = options;
        _mcpRefresher = mcpRefresher;
    }

    /// <summary>Validates a definition without saving it or calling a model.</summary>
    /// <param name="definition">The definition to validate.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The report containing all findings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    public async ValueTask<AgentValidationReport> ValidateAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var messages = new List<ValidationMessage>();
        var inconclusive = false;

        await CheckModelAsync(definition, messages, cancellationToken).ConfigureAwait(false);

        await CheckToolsAsync(
            definition,
            messages,
            _options.Value.Validation.McpTimeout,
            () => inconclusive = true,
            cancellationToken).ConfigureAwait(false);

        await CheckSkillsAsync(definition, messages, cancellationToken).ConfigureAwait(false);
        await CheckCallGraphAsync(definition, messages, cancellationToken).ConfigureAwait(false);

        // Compilation is not useful while the outcome is inconclusive. A tool
        // behind an unreachable MCP server cannot resolve in real compilation
        // either, which would replace unknown_tool with a misleading compilation_error.
        if (!HasError(messages) && !inconclusive)
        {
            await CheckStructureAsync(definition, messages, cancellationToken).ConfigureAwait(false);
        }

        return new AgentValidationReport
        {
            Valid = !HasError(messages),
            Inconclusive = inconclusive,
            Messages = messages,
        };
    }

    private static bool HasError(List<ValidationMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Severity == ValidationSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private async ValueTask CheckModelAsync(AgentDefinition definition, List<ValidationMessage> messages, CancellationToken cancellationToken)
    {
        var providers = _models.List();
        var registered = false;

        foreach (var provider in providers)
        {
            if (string.Equals(provider.Name, definition.Model.Provider, StringComparison.Ordinal))
            {
                registered = true;
                break;
            }
        }

        if (!registered)
        {
            var known = providers.Count == 0
                ? "no provider is registered"
                : string.Join(", ", providers.Select(static provider => provider.Name));

            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "unknown_model",
                Message = $"No model provider named '{definition.Model.Provider}' is registered. " +
                          $"Registered providers: {known}.",
                Path = "model.provider",
            });

            return;
        }

        try
        {
            // This is the same call as the real path (phase 65: including the
            // requesting tenant's own provider credential and egress policy).
            // It creates the chat client but sends no request.
            _ = await _models.CreateChatClientAsync(definition.Model, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentPrismException ex)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "invalid_setting",
                Message = ex.Message,
                Path = "model.providerSettings",
            });
        }
    }

    private async ValueTask CheckToolsAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        TimeSpan mcpTimeout,
        Action markInconclusive,
        CancellationToken cancellationToken)
    {
        var missing = FindMissingTools(definition);

        if (missing.Count == 0)
        {
            return;
        }

        if (_mcpRefresher is not null)
        {
            if (await TryRefreshMcpAsync(mcpTimeout, cancellationToken).ConfigureAwait(false))
            {
                missing = FindMissingTools(definition);
            }
            else
            {
                // An unreachable MCP server and a wrong tool name are not the
                // same condition. It is unknown whether remaining names are truly
                // unknown or behind the unreachable server, so unknown_tool is not
                // emitted and the result is marked Inconclusive instead.
                markInconclusive();
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = "mcp_unreachable",
                    Message = "A fresh list could not be fetched from MCP servers for missing tool names " +
                              "(timeout or connection failure). The result is inconclusive.",
                });

                return;
            }
        }

        foreach (var (index, name) in missing)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "unknown_tool",
                Message = $"Agent '{definition.Name}' refers to tool '{name}', but it is not registered " +
                          "in this code.",
                Path = $"toolNames[{index}]",
            });
        }
    }

    private List<(int Index, string Name)> FindMissingTools(AgentDefinition definition)
    {
        List<(int, string)>? missing = null;

        for (var index = 0; index < definition.ToolNames.Count; index++)
        {
            var name = definition.ToolNames[index];

            if (!_tools.TryGet(name, out _))
            {
                (missing ??= []).Add((index, name));
            }
        }

        return missing ?? [];
    }

    private async ValueTask<bool> TryRefreshMcpAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var outcome = await _mcpRefresher!.RefreshAsync(cts.Token).ConfigureAwait(false);

            // 🚨 HATA-006 / MT-CORE-006: when an MCP server actively refuses a
            // connection, McpToolCatalog does not throw. Its tools disappear from
            // the list and refresh reports success. Only a timeout throws below.
            // Both cases must behave the same; HadUnreachableServers closes the gap.
            return !outcome.HadUnreachableServers;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The MCP transport can throw several exception types, including HTTP
            // and JSON-RPC. The required guarantee is that an unreachable server
            // never breaks validation; it only makes the result Inconclusive.
            return false;
        }
    }

    private async ValueTask CheckSkillsAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < definition.SkillNames.Count; index++)
        {
            var name = definition.SkillNames[index];

            if (!await _skills.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Error,
                    Code = "unknown_skill",
                    Message = $"Agent '{definition.Name}' refers to skill '{name}', but the skill was not found.",
                    Path = $"skillNames[{index}]",
                });
            }
        }
    }

    private async ValueTask CheckCallGraphAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        if (definition.CallableAgentNames.Count == 0)
        {
            return;
        }

        var descriptors = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        if (AgentCallGraph.ValidateDetailed(definition.Name, definition.CallableAgentNames, descriptors) is { } problem)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = problem.Code,
                Message = problem.Message,
                Path = "callableAgentNames",
            });
        }
    }

    private async ValueTask CheckStructureAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            var callable = await _compiler.ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);

            _ = await _compiler.CompileAsync(definition, callable, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentPrismCompilationException ex)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "compilation_error",
                Message = ex.Message,
            });
        }
    }
}
