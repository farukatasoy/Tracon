using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The request fields of <see cref="ModelBinding"/> that become <see cref="ChatOptions"/>
/// members: the value checks and the support check.
/// </summary>
public sealed partial class AgentDefinitionCompiler
{
    /// <summary>
    /// Stops compilation when the binding sets a field that the primary provider or a fallback
    /// link would drop.
    /// </summary>
    /// <remarks>
    /// The rule lives in <see cref="SamplingSupportCheck"/>, which the validation report reads too.
    /// </remarks>
    private void CheckSamplingSupport(AgentDefinition definition)
    {
        var findings = SamplingSupportCheck.Unsupported(_models, definition.Model);

        if (findings.Count == 0)
        {
            return;
        }

        throw new TraconCompilationException(
            $"Agent '{definition.Name}' cannot be compiled: {string.Join(" ", findings.Select(static finding => finding.Describe()))}")
        {
            AgentName = definition.Name,
        };
    }

    private static int? ParseTopK(AgentDefinition definition)
    {
        var topK = definition.Model.TopK;

        if (topK is <= 0)
        {
            throw new TraconCompilationException(
                $"Agent '{definition.Name}''s {nameof(ModelBinding.TopK)} value ({topK}) is not valid; it must be greater than zero.")
            {
                AgentName = definition.Name,
            };
        }

        return topK;
    }

    private static List<string>? ParseStopSequences(AgentDefinition definition)
    {
        var sequences = definition.Model.StopSequences;

        if (sequences is not { Count: > 0 })
        {
            return null;
        }

        foreach (var sequence in sequences)
        {
            if (string.IsNullOrEmpty(sequence))
            {
                throw new TraconCompilationException(
                    $"Agent '{definition.Name}''s {nameof(ModelBinding.StopSequences)} has an empty entry; every stop sequence must have text.")
                {
                    AgentName = definition.Name,
                };
            }
        }

        return [.. sequences];
    }

    /// <summary>
    /// Converts <see cref="ModelBinding.ToolMode"/> into a <see cref="ChatToolMode"/>.
    /// </summary>
    /// <remarks>
    /// An invalid value is not silently ignored, for the reason the reasoning effort is not:
    /// a mistyped mode would run with the provider default and nothing would show why. A
    /// <c>required:&lt;tool&gt;</c> that names a tool the definition does not list is rejected
    /// too, because the provider would answer with <c>400</c> at the first run.
    /// </remarks>
    private static ChatToolMode? ParseToolMode(AgentDefinition definition)
    {
        var value = definition.Model.ToolMode;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();

        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return ChatToolMode.Auto;
        }

        if (string.Equals(text, "none", StringComparison.OrdinalIgnoreCase))
        {
            return ChatToolMode.None;
        }

        if (string.Equals(text, "required", StringComparison.OrdinalIgnoreCase))
        {
            return ChatToolMode.RequireAny;
        }

        const string RequiredPrefix = "required:";

        if (text.StartsWith(RequiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var tool = text[RequiredPrefix.Length..].Trim();

            if (tool.Length == 0)
            {
                throw InvalidToolMode(definition, value, "the tool name after 'required:' is empty");
            }

            if (!definition.ToolNames.Contains(tool, StringComparer.Ordinal))
            {
                throw InvalidToolMode(
                    definition,
                    value,
                    $"'{tool}' is not one of the definition's {nameof(AgentDefinition.ToolNames)}" +
                    (definition.ToolNames.Count == 0 ? " (it lists none)" : $" ({string.Join(", ", definition.ToolNames)})"));
            }

            return ChatToolMode.RequireSpecific(tool);
        }

        throw InvalidToolMode(definition, value, "the valid values are 'auto', 'none', 'required' and 'required:<tool>'");
    }

    private static TraconCompilationException InvalidToolMode(AgentDefinition definition, string value, string reason)
        => new($"Agent '{definition.Name}''s {nameof(ModelBinding.ToolMode)} value is not valid: '{value}'; {reason}.")
        {
            AgentName = definition.Name,
        };

    /// <summary>
    /// Converts a <see cref="ModelBinding.ReasoningOutput"/> value into a
    /// <see cref="Microsoft.Extensions.AI.ReasoningOutput"/> value.
    /// </summary>
    /// <remarks>The same rule as <see cref="ParseReasoningEffort"/>: an invalid value is not ignored.</remarks>
    private static ReasoningOutput? ParseReasoningOutput(AgentDefinition definition)
    {
        var value = definition.Model.ReasoningOutput;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<ReasoningOutput>(value, ignoreCase: true, out var output)
            && Enum.IsDefined(output))
        {
            return output;
        }

        throw new TraconCompilationException(
            $"Agent '{definition.Name}''s reasoning output value is not recognized: '{value}'. " +
            $"Valid values: {string.Join(", ", Enum.GetNames<ReasoningOutput>())}.")
        {
            AgentName = definition.Name,
        };
    }
}
