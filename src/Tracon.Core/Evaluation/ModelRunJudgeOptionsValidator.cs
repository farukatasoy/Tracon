using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Validates <see cref="ModelRunJudgeOptions"/> when the application starts: a judge whose model the catalog says rejects
/// sampling must not set the sampling fields it sends.
/// </summary>
/// <remarks>
/// <para>
/// The judge writes its own request options and never passes through the agent compiler, so the rule the compiler and the
/// validation report apply to an agent (<see cref="SamplingCapabilityCheck"/>) was not applied to it. Without this check a
/// judge on a model that rejects <c>temperature</c> fails on the first sampled run with the provider's <c>400</c>, and the
/// scores simply stop.
/// </para>
/// <para>
/// The provider's own statement of what its API surface sends (<see cref="ISamplingSupport"/>) is still asked when the judge
/// runs; this is the catalog's statement about the model, which is known at start.
/// </para>
/// </remarks>
internal sealed class ModelRunJudgeOptionsValidator(IModelProviderRegistry modelProviders) : IValidateOptions<ModelRunJudgeOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ModelRunJudgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // `Model` is required, so a null here is a configuration that never set it; that is the judge's own error to report.
        if (options.Model is null)
        {
            return ValidateOptionsResult.Success;
        }

        var rejecting = SamplingCapabilityCheck.ModelsRejectingSampling(modelProviders, options.Model);

        return rejecting.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{nameof(ModelRunJudgeOptions)}.{nameof(ModelRunJudgeOptions.Model)} sets {nameof(ModelBinding.Temperature)} " +
                $"or {nameof(ModelBinding.TopP)}, but the model catalog states that these models reject sampling parameters: " +
                $"{string.Join(", ", rejecting)}. Remove them from the judge's model binding.");
    }
}
