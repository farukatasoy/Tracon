using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Chat client creation, tool resolution, <see cref="ChatOptions"/>
/// production, response format, and model capability checks.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    private IChatClient CreateChatClient(AgentDefinition definition) => CreateChatClient(definition, AgentLoopBinding(definition));

    private IChatClient CreateChatClient(AgentDefinition definition, ModelBinding binding)
    {
        try
        {
            return _models.CreateChatClient(binding);
        }
        catch (TraconException ex)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' could not be compiled: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
        }
    }

    private ValueTask<IChatClient> CreateChatClientAsync(AgentDefinition definition, CancellationToken cancellationToken)
        => CreateChatClientAsync(definition, AgentLoopBinding(definition), cancellationToken);

    // F-315 (K-892): a harness agent's MaximumIterationsPerRequest goes to MAF's
    // own loop, which sits ABOVE the model pipeline and never sees a tool call
    // - the pipeline's loop runs them all. Measured: a limit of 2 still made 7
    // model calls. The limit therefore travels on the binding to the loop that
    // runs the tools, and on to every fallback link.
    private static ModelBinding AgentLoopBinding(AgentDefinition definition)
    {
        if (definition.Harness?.MaximumIterationsPerRequest is not { } limit)
        {
            return definition.Model;
        }

        if (limit < 1)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' has an invalid {nameof(HarnessSettings)}." +
                $"{nameof(HarnessSettings.MaximumIterationsPerRequest)} ({limit}); it must be at least 1.")
            {
                AgentName = definition.Name,
            };
        }

        return definition.Model with { ToolLoop = definition.Model.ToolLoop with { IterationLimit = limit } };
    }

    private async ValueTask<IChatClient> CreateChatClientAsync(AgentDefinition definition, ModelBinding binding, CancellationToken cancellationToken)
    {
        try
        {
            return await _models.CreateChatClientAsync(binding, cancellationToken).ConfigureAwait(false);
        }

        // F-442: a tenant's refusal keeps its type, so every path that refuses a
        // provider records the same class; it gains the agent's name, which the
        // compilation failure it used to become carried.
        catch (TraconTenantProviderRefusedException ex)
        {
            throw new TraconTenantProviderRefusedException($"Agent '{definition.Name}' could not be compiled: {ex.Message}", ex);
        }
        catch (TraconException ex)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' could not be compiled: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
        }
    }

    // 🚨 Every model client the agent calls is built here, BEFORE the agent,
    // so the async path can put each one through the registry's tenant-aware
    // members. The summarizer and the aiJudge loop judge were once built
    // inside BuildAgent with the synchronous CreateChatClient: the tenant's
    // egress policy and its own key never reached them, and a definition
    // summarized on a provider the tenant had forbidden (2026-10-11). The
    // synchronous path stays tenant-blind by design (K-466).
    private SecondaryChatClients CreateSecondaryChatClients(AgentDefinition definition)
        => new(
            ResolveSummarizationBinding(definition) is { } binding ? CreateChatClient(definition, binding) : null,
            _loopEvaluators.CreateJudgeChatClient(definition));

    private async ValueTask<SecondaryChatClients> CreateSecondaryChatClientsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        // The summarizer reads the tenant's conversation on the tenant's
        // account: egress policy AND the tenant's own key. The judge is the
        // host's one judge model (K-613, K-706): egress policy only.
        var summarization = ResolveSummarizationBinding(definition) is { } binding
            ? await CreateChatClientAsync(definition, binding, cancellationToken).ConfigureAwait(false)
            : null;

        var loopJudge = await _loopEvaluators.CreateJudgeChatClientAsync(definition, cancellationToken).ConfigureAwait(false);

        return new SecondaryChatClients(summarization, loopJudge);
    }

    private List<AITool> ResolveTools(AgentDefinition definition)
    {
        var tools = new List<AITool>(definition.ToolNames.Count);
        List<string>? missing = null;
        HostedToolContext? hostedContext = null;

        CheckHostedTools(definition);

        foreach (var toolName in definition.ToolNames)
        {
            // A hosted tool first: a name a host registered in code must not turn into a remote tool
            // of the same name the day a server publishes one.
            if (_hostedTools.TryGet(toolName, out var hosted))
            {
                tools.Add(HostedToolRegistry.Create(hosted, hostedContext ??= CreateHostedToolContext(definition), definition.Name));
            }
            else if (_tools.TryGet(toolName, out var tool))
            {
                tools.Add(tool);
            }
            else
            {
                (missing ??= []).Add(toolName);
            }
        }

        if (missing is not null)
        {
            var registered = _tools.List().Concat(_hostedTools.Descriptors).ToList();
            var available = registered.Count == 0
                ? "no tools are registered"
                : string.Join(", ", registered.Select(static descriptor => descriptor.Name));

            throw new TraconCompilationException(
                $"Agent '{definition.Name}' references the following tools, but they are not registered in code: " +
                $"{string.Join(", ", missing)}. Registered tools: {available}. " +
                "Tools are defined only in code; register them with `builder.AddTracon().AddTool(...)`.")
            {
                AgentName = definition.Name,
            };
        }

        return tools;
    }

    // What a hosted tool's factory may read: the tenant the agent is compiled for (the same one the
    // agent extensions get), the provider of the primary binding, and the host's services.
    private HostedToolContext CreateHostedToolContext(AgentDefinition definition)
        => new()
        {
            TenantId = ResolveTenantId(definition),
            Provider = definition.Model.Provider,
            Services = _services ?? NoServices.Instance,
        };

    /// <summary>
    /// Stops the compilation when the hosted tools a definition selects are not acceptable: two of a
    /// kind, a second web search, a tool mode that requires one, or a provider that would drop one.
    /// </summary>
    /// <remarks>
    /// The rules live in <see cref="HostedToolRegistry.FindProblems"/> and
    /// <see cref="HostedToolSupportCheck"/>, which the validation report reads too. A definition saved over
    /// HTTP passed them there, and a definition written in code never met the validator.
    /// </remarks>
    private void CheckHostedTools(AgentDefinition definition)
    {
        var selected = _hostedTools.Selected(definition);

        if (selected.Count == 0)
        {
            return;
        }

        var messages = _hostedTools.FindProblems(definition).Select(static problem => problem.Message)
            .Concat(HostedToolSupportCheck.Unsupported(_models, definition.Model, selected).Select(static finding => finding.Describe()))
            .ToList();

        if (messages.Count > 0)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' selects hosted tools that cannot run: {string.Join(" ", messages)}")
            {
                AgentName = definition.Name,
            };
        }
    }

    private ChatOptions BuildChatOptions(AgentDefinition definition, List<AITool> tools, string? culture, string? sharedInstructions)
    {
        CheckSamplingCapability(definition);
        CheckSamplingSupport(definition);

        var model = definition.Model;
        var options = new ChatOptions
        {
            Instructions = CombineInstructions(sharedInstructions, InstructionCultureResolver.Resolve(definition, culture)),
            ModelId = model.Model,
            Temperature = model.Temperature,
            TopP = model.TopP,
            MaxOutputTokens = model.MaxOutputTokens,
            Seed = model.Seed,
            TopK = ParseTopK(definition),
            FrequencyPenalty = model.FrequencyPenalty,
            PresencePenalty = model.PresencePenalty,
            AllowMultipleToolCalls = model.AllowMultipleToolCalls,
            ToolMode = ParseToolMode(definition),
        };

        if (ParseStopSequences(definition) is { } stopSequences)
        {
            options.StopSequences = stopSequences;
        }

        if (tools.Count > 0)
        {
            options.Tools = tools;
        }

        var effort = ParseReasoningEffort(definition);
        var output = ParseReasoningOutput(definition);

        if (effort is not null || output is not null)
        {
            options.Reasoning = new ReasoningOptions { Effort = effort, Output = output };
        }

        options.ResponseFormat = BuildResponseFormat(definition);

        return options;
    }

    /// <summary>Prepends a shared instructions block's text to a definition's own resolved instructions.</summary>
    private static string? CombineInstructions(string? sharedInstructions, string? ownInstructions)
    {
        if (string.IsNullOrEmpty(sharedInstructions))
        {
            return ownInstructions;
        }

        return string.IsNullOrEmpty(ownInstructions)
            ? sharedInstructions
            : string.Concat(sharedInstructions, "\n\n", ownInstructions);
    }

    /// <summary>
    /// Converts <see cref="ModelBinding.ResponseFormat"/> into a <see cref="ChatResponseFormat"/>.
    /// </summary>
    /// <remarks>
    /// An invalid combination is not silently ignored:
    /// compilation stops when the mode is <see cref="AgentResponseFormatKind.JsonSchema"/>
    /// and the schema is missing, when the schema is set for other modes, or
    /// when the schema is not a JSON object. Only the
    /// <c>ForJsonSchema(JsonElement, ...)</c> overload is used - the overloads
    /// taking <c>Type</c> or <c>JsonSerializerOptions</c> rely on reflection
    /// and break the AOT stance.
    /// </remarks>
    private ChatResponseFormat? BuildResponseFormat(AgentDefinition definition)
    {
        var format = definition.Model.ResponseFormat;

        if (format is null)
        {
            return null;
        }

        if (format.Kind == AgentResponseFormatKind.JsonSchema)
        {
            if (format.Schema is not { } schema)
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}' selected the JsonSchema output mode but did not " +
                    $"supply {nameof(AgentResponseFormat.Schema)}.")
                {
                    AgentName = definition.Name,
                };
            }

            if (schema.ValueKind != JsonValueKind.Object)
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}''s {nameof(AgentResponseFormat.Schema)} field must be a JSON " +
                    "object.")
                {
                    AgentName = definition.Name,
                };
            }

            CheckStructuredOutputCapability(definition);

            return ChatResponseFormat.ForJsonSchema(schema, format.SchemaName, format.SchemaDescription);
        }

        if (format.Schema is not null)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}' selected the '{format.Kind}' output mode but also supplied " +
                $"{nameof(AgentResponseFormat.Schema)}. The schema is only used in JsonSchema mode.")
            {
                AgentName = definition.Name,
            };
        }

        if (format.Kind == AgentResponseFormatKind.Json)
        {
            CheckStructuredOutputCapability(definition);

            return ChatResponseFormat.Json;
        }

        return ChatResponseFormat.Text;
    }

    /// <summary>
    /// Checks whether the selected model supports structured output.
    /// </summary>
    /// <remarks>
    /// The check is SKIPPED for a model not found in the model catalog:
    /// model names may come from configuration and the catalog is
    /// not a validation list. Compilation stops only for a model that IS
    /// FOUND in the catalog and whose <see cref="ModelDescriptor.SupportsStructuredOutput"/>
    /// value is explicitly <see langword="false"/>.
    /// </remarks>
    private void CheckStructuredOutputCapability(AgentDefinition definition)
    {
        var descriptor = FindModelDescriptor(definition.Model.Provider, definition.Model.Model);

        if (descriptor is { SupportsStructuredOutput: false })
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}''s model ('{definition.Model.Provider}/{definition.Model.Model}') " +
                "does not support structured output.")
            {
                AgentName = definition.Name,
            };
        }
    }

    /// <summary>
    /// Stops compilation when the definition sets <see cref="ModelBinding.Temperature"/>
    /// or <see cref="ModelBinding.TopP"/> and the catalog states that a model of the
    /// binding rejects them.
    /// </summary>
    /// <remarks>
    /// Without this, the first run of such an agent fails with the provider's
    /// <c>400</c>. The rule lives in <see cref="SamplingCapabilityCheck"/>, which
    /// the validation report reads too.
    /// </remarks>
    private void CheckSamplingCapability(AgentDefinition definition)
    {
        var rejecting = SamplingCapabilityCheck.ModelsRejectingSampling(_models, definition.Model);

        if (rejecting.Count == 0)
        {
            return;
        }

        throw new TraconCompilationException(
            $"Agent '{definition.Name}' sets {nameof(ModelBinding.Temperature)} or {nameof(ModelBinding.TopP)}, " +
            $"but the model catalog states that these models reject sampling parameters: " +
            $"{string.Join(", ", rejecting)}. Remove them from the definition.")
        {
            AgentName = definition.Name,
        };
    }

    private ModelDescriptor? FindModelDescriptor(string provider, string model)
        => ModelCatalogLookup.Find(_models, provider, model);

    /// <summary>
    /// Converts a <see cref="ModelBinding.ReasoningEffort"/> value into a
    /// <see cref="Microsoft.Extensions.AI.ReasoningEffort"/> value.
    /// </summary>
    /// <remarks>
    /// An invalid value is not silently ignored. Reasoning effort changes both
    /// cost and latency; if a mistyped value ran unnoticed, the user would not
    /// get the behavior they expect and would not see why. The provider
    /// decides whether the model supports this setting.
    /// </remarks>
    private static ReasoningEffort? ParseReasoningEffort(AgentDefinition definition)
    {
        var value = definition.Model.ReasoningEffort;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<ReasoningEffort>(value, ignoreCase: true, out var effort)
            && Enum.IsDefined(effort))
        {
            return effort;
        }

        throw new TraconCompilationException(
            $"Agent '{definition.Name}''s reasoning effort value is not recognized: '{value}'. " +
            $"Valid values: {string.Join(", ", Enum.GetNames<ReasoningEffort>())}.")
        {
            AgentName = definition.Name,
        };
    }
}
