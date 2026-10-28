namespace Tracon;

/// <summary>
/// Decides which members of a <see cref="ModelBinding"/> a factory agent can declare.
/// </summary>
/// <remarks>
/// <para>
/// A definition agent gets its <see cref="Microsoft.Extensions.AI.ChatOptions"/> from the compiler:
/// the compiler copies the request fields of the binding into the options of every call. A factory
/// agent is built by the caller, so Tracon never touches its options. A request field on a binding
/// that declares the model of a factory agent would be read by nothing, and Tracon refuses a value
/// that is ignored (the same rule as <see cref="ModelBinding.ProviderSettings"/>).
/// </para>
/// <para>
/// The two lists below are the whole classification. Every member of <see cref="ModelBinding"/>
/// is in exactly one of them; a unit test walks the type by reflection and fails when a new member
/// is in neither. The production code reads the lists, not the type, so it stays free of reflection
/// (AOT).
/// </para>
/// </remarks>
internal static class FactoryModelFields
{
    /// <summary>
    /// The request fields: the compiler turns each into a <see cref="Microsoft.Extensions.AI.ChatOptions"/>
    /// member. The second part names that member, for the message.
    /// </summary>
    private static readonly (string Name, string ChatOptionsMember, Func<ModelBinding, bool> IsSet)[] RequestFields =
    [
        (nameof(ModelBinding.Temperature), "Temperature", static model => model.Temperature is not null),
        (nameof(ModelBinding.TopP), "TopP", static model => model.TopP is not null),
        (nameof(ModelBinding.TopK), "TopK", static model => model.TopK is not null),
        (nameof(ModelBinding.MaxOutputTokens), "MaxOutputTokens", static model => model.MaxOutputTokens is not null),
        (nameof(ModelBinding.Seed), "Seed", static model => model.Seed is not null),
        (nameof(ModelBinding.FrequencyPenalty), "FrequencyPenalty", static model => model.FrequencyPenalty is not null),
        (nameof(ModelBinding.PresencePenalty), "PresencePenalty", static model => model.PresencePenalty is not null),
        (nameof(ModelBinding.StopSequences), "StopSequences", static model => model.StopSequences is { Count: > 0 }),
        (nameof(ModelBinding.ToolMode), "ToolMode", static model => model.ToolMode is not null),
        (nameof(ModelBinding.AllowMultipleToolCalls), "AllowMultipleToolCalls", static model => model.AllowMultipleToolCalls is not null),
        (nameof(ModelBinding.ReasoningEffort), "Reasoning.Effort", static model => model.ReasoningEffort is not null),
        (nameof(ModelBinding.ReasoningOutput), "Reasoning.Output", static model => model.ReasoningOutput is not null),
        (nameof(ModelBinding.ResponseFormat), "ResponseFormat", static model => model.ResponseFormat is not null),
    ];

    /// <summary>
    /// The members the model registry or the provider adapter reads when it builds the client.
    /// These are the ones a factory agent can declare.
    /// </summary>
    private static readonly string[] AcceptedFields =
    [
        nameof(ModelBinding.Provider),
        nameof(ModelBinding.Model),
        nameof(ModelBinding.ProviderSettings),
        nameof(ModelBinding.Fallbacks),
        nameof(ModelBinding.ResponseCache),
        nameof(ModelBinding.ToolLoop),
    ];

    /// <summary>Gets the names of the request fields a factory agent cannot declare.</summary>
    public static IReadOnlyList<string> RequestFieldNames { get; } = [.. RequestFields.Select(static field => field.Name)];

    /// <summary>Gets the names of the members a factory agent can declare.</summary>
    public static IReadOnlyList<string> AcceptedFieldNames => AcceptedFields;

    /// <summary>Checks the model a factory agent declares.</summary>
    /// <param name="agentName">The agent name, for the message.</param>
    /// <param name="model">The declared model.</param>
    /// <param name="parameterName">The name of the parameter the model came in, for <see cref="ArgumentException.ParamName"/>.</param>
    /// <exception cref="ArgumentException">
    /// The provider or the model name is empty, or a request field is set.
    /// </exception>
    public static void Validate(string agentName, ModelBinding model, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(model.Provider) || string.IsNullOrWhiteSpace(model.Model))
        {
            throw new ArgumentException(
                $"The model of factory agent '{agentName}' needs a provider and a model name. " +
                "Cost and model attribution read the name, so a binding without one declares nothing.",
                parameterName);
        }

        foreach (var (name, chatOptionsMember, isSet) in RequestFields)
        {
            if (isSet(model))
            {
                throw new ArgumentException(
                    $"The model of factory agent '{agentName}' sets {nameof(ModelBinding)}.{name}. " +
                    "A factory agent builds its own ChatOptions, so Tracon would ignore the value; a value that " +
                    $"is ignored is refused. Remove it from the binding and set ChatOptions.{chatOptionsMember} in the factory.",
                    parameterName);
            }
        }
    }
}
