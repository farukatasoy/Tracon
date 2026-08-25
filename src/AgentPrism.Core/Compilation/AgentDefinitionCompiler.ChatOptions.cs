using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Chat client creation, tool resolution, <see cref="ChatOptions"/>
/// production, response format, and model capability checks.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    private IChatClient CreateChatClient(AgentDefinition definition) => CreateChatClient(definition, definition.Model);

    private IChatClient CreateChatClient(AgentDefinition definition, ModelBinding binding)
    {
        try
        {
            return _models.CreateChatClient(binding);
        }
        catch (AgentPrismException ex)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' could not be compiled: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
        }
    }

    private ValueTask<IChatClient> CreateChatClientAsync(AgentDefinition definition, CancellationToken cancellationToken)
        => CreateChatClientAsync(definition, definition.Model, cancellationToken);

    private async ValueTask<IChatClient> CreateChatClientAsync(AgentDefinition definition, ModelBinding binding, CancellationToken cancellationToken)
    {
        try
        {
            return await _models.CreateChatClientAsync(binding, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentPrismException ex)
        {
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' could not be compiled: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
        }
    }

    private List<AITool> ResolveTools(AgentDefinition definition)
    {
        var tools = new List<AITool>(definition.ToolNames.Count);
        List<string>? missing = null;

        foreach (var toolName in definition.ToolNames)
        {
            if (_tools.TryGet(toolName, out var tool))
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
            var registered = _tools.List();
            var available = registered.Count == 0
                ? "no tools are registered"
                : string.Join(", ", registered.Select(static descriptor => descriptor.Name));

            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}' references the following tools, but they are not registered in code: " +
                $"{string.Join(", ", missing)}. Registered tools: {available}. " +
                "Tools are defined only in code; register them with `builder.AddAgentPrism().AddTool(...)`.")
            {
                AgentName = definition.Name,
            };
        }

        return tools;
    }

    private ChatOptions BuildChatOptions(AgentDefinition definition, List<AITool> tools, string? culture, string? sharedInstructions)
    {
        var options = new ChatOptions
        {
            Instructions = CombineInstructions(sharedInstructions, InstructionCultureResolver.Resolve(definition, culture)),
            ModelId = definition.Model.Model,
            Temperature = definition.Model.Temperature,
            TopP = definition.Model.TopP,
            MaxOutputTokens = definition.Model.MaxOutputTokens,
        };

        if (tools.Count > 0)
        {
            options.Tools = tools;
        }

        if (ParseReasoningEffort(definition) is { } effort)
        {
            options.Reasoning = new ReasoningOptions { Effort = effort };
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
                throw new AgentPrismCompilationException(
                    $"Agent '{definition.Name}' selected the JsonSchema output mode but did not " +
                    $"supply {nameof(AgentResponseFormat.Schema)}.")
                {
                    AgentName = definition.Name,
                };
            }

            if (schema.ValueKind != JsonValueKind.Object)
            {
                throw new AgentPrismCompilationException(
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
            throw new AgentPrismCompilationException(
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
            throw new AgentPrismCompilationException(
                $"Agent '{definition.Name}''s model ('{definition.Model.Provider}/{definition.Model.Model}') " +
                "does not support structured output.")
            {
                AgentName = definition.Name,
            };
        }
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

        throw new AgentPrismCompilationException(
            $"Agent '{definition.Name}''s reasoning effort value is not recognized: '{value}'. " +
            $"Valid values: {string.Join(", ", Enum.GetNames<ReasoningEffort>())}.")
        {
            AgentName = definition.Name,
        };
    }
}
