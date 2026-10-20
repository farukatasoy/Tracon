using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The output-limit and sampling checks of <see cref="AgentDefinitionValidator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Kept in their own part with ONE call site in <c>ValidateAsync</c>: the
/// other checks of the validator change for unrelated reasons (loop criteria,
/// tools), and these two only read the model catalog and the chat client the
/// real path builds.
/// </para>
/// <para>
/// <strong>Sampling is an error.</strong> When the catalog states that a model
/// of the binding rejects <see cref="ModelBinding.Temperature"/> and
/// <see cref="ModelBinding.TopP"/>, compilation stops
/// (<see cref="SamplingCapabilityCheck"/>); the report names the field instead
/// of the generic <c>compilation_error</c>, and the compile step that would
/// repeat it is skipped because the report already holds an error.
/// </para>
/// <para>
/// <strong>A field the provider would drop is an error.</strong> The same holds for the
/// other request fields (<see cref="SamplingSupportCheck"/>): a provider whose API surface
/// does not send <see cref="ModelBinding.Seed"/> would run the agent without it.
/// </para>
/// <para>
/// <strong>A low default output limit is a warning.</strong> It does not stop
/// the definition: a short answer fits, and only the model can tell how long its
/// answer will be. It is raised only when all of these hold: the binding sets no
/// <see cref="ModelBinding.MaxOutputTokens"/>; the provider's client states the
/// limit it sends instead (<see cref="ModelOutputLimit"/>); the catalog states a
/// higher maximum for the model; and the agent asks for a structured response
/// (cut off, it fails the run) or for high reasoning effort (thinking tokens
/// count against the same limit).
/// </para>
/// </remarks>
internal sealed partial class AgentDefinitionValidator
{
    /// <summary>The code of a sampling parameter a catalog model rejects.</summary>
    internal const string SamplingUnsupportedCode = "sampling_unsupported";

    /// <summary>The code of a request field the provider's API surface would drop.</summary>
    internal const string SamplingFieldUnsupportedCode = "sampling_field_unsupported";

    /// <summary>The code of the warning about a low default output limit.</summary>
    internal const string OutputTruncationRiskCode = "output_truncation_risk";

    private async ValueTask CheckOutputLimitsAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        // An unknown provider or a binding the registry cannot build is already
        // reported by CheckModelAsync; there is no client or catalog entry to read.
        if (HasError(messages))
        {
            return;
        }

        CheckSampling(definition, messages);
        CheckSamplingSupport(definition, messages);

        await CheckOutputTruncationRiskAsync(definition, messages, cancellationToken).ConfigureAwait(false);
    }

    private void CheckSampling(AgentDefinition definition, List<ValidationMessage> messages)
    {
        var rejecting = SamplingCapabilityCheck.ModelsRejectingSampling(_models, definition.Model);

        if (rejecting.Count == 0)
        {
            return;
        }

        var models = string.Join(", ", rejecting);

        if (definition.Model.Temperature is not null)
        {
            messages.Add(SamplingRejected(nameof(ModelBinding.Temperature), "model.temperature", models));
        }

        if (definition.Model.TopP is not null)
        {
            messages.Add(SamplingRejected(nameof(ModelBinding.TopP), "model.topP", models));
        }
    }

    private void CheckSamplingSupport(AgentDefinition definition, List<ValidationMessage> messages)
    {
        foreach (var finding in SamplingSupportCheck.Unsupported(_models, definition.Model))
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = SamplingFieldUnsupportedCode,
                Message = finding.Describe(),
                Path = finding.Path,
            });
        }
    }

    private static ValidationMessage SamplingRejected(string field, string path, string models) => new()
    {
        Severity = ValidationSeverity.Error,
        Code = SamplingUnsupportedCode,
        Message = $"The model catalog states that these models reject sampling parameters: {models}. " +
                  $"Remove {nameof(ModelBinding)}.{field}; the provider would reject every request with 400.",
        Path = path,
    };

    private async ValueTask CheckOutputTruncationRiskAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        var binding = definition.Model;

        if (binding.MaxOutputTokens is not null)
        {
            return;
        }

        IChatClient client;

        try
        {
            // The same call as CheckModelAsync and the real path: the client is
            // built for THIS binding, so it can state the limit its own settings
            // lead to. No request is sent.
            client = await _models.CreateChatClientAsync(binding, cancellationToken).ConfigureAwait(false);
        }
        catch (TraconException)
        {
            return;
        }

        if (client.GetService<ModelOutputLimit>() is not { } limit)
        {
            return;
        }

        var structured = binding.ResponseFormat?.Kind is AgentResponseFormatKind.Json or AgentResponseFormatKind.JsonSchema;

        if (!structured && !limit.HighReasoningEffort)
        {
            return;
        }

        if (ModelCatalogLookup.Find(_models, binding.Provider, binding.Model)?.MaxOutputTokens is not { } modelMaximum
            || limit.DefaultMaxOutputTokens >= modelMaximum)
        {
            return;
        }

        var reason = structured
            ? "The agent asks for a structured response, and one cut off at the limit fails the run (output_truncated)."
            : "The binding asks for high reasoning effort, and reasoning tokens count against the same limit.";

        messages.Add(new ValidationMessage
        {
            Severity = ValidationSeverity.Warning,
            Code = OutputTruncationRiskCode,
            Message = $"{nameof(ModelBinding.MaxOutputTokens)} is not set, so the '{binding.Provider}' provider sends " +
                      $"its default output limit of {limit.DefaultMaxOutputTokens} tokens; the catalog states that " +
                      $"model '{binding.Model}' can produce up to {modelMaximum}. {reason} " +
                      $"Set {nameof(ModelBinding)}.{nameof(ModelBinding.MaxOutputTokens)}.",
            Path = "model.maxOutputTokens",
        });
    }
}
