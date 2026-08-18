using Microsoft.Extensions.Options;
using Microsoft.ML.Tokenizers;

namespace AgentPrism;

/// <summary>
/// Estimates a prompt's token count and whether it would be rejected by the
/// pre-flight context-window check, without calling a model provider.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 <strong>The estimate is approximate</strong> (phase 62, F-59, open
/// question 1): counting is done with a single fixed OpenAI encoding
/// (<c>o200k_base</c>, via <see cref="TiktokenTokenizer"/>) regardless of the
/// bound provider — Anthropic and Google publish no equivalent offline
/// tokenizer package. The count is close enough to size a prompt against a
/// context window; it is not the exact count any specific provider would bill.
/// </para>
/// <para>
/// The tokenizer is built ONCE and reused: constructing one loads the vocab
/// data package's contents, and doing that per call would make even a cheap
/// estimate expensive.
/// </para>
/// </remarks>
public sealed class ContextWindowEstimator
{
    // Fixed reference model/encoding for every estimate (see the type's remarks).
    // Requires the Microsoft.ML.Tokenizers.Data.O200kBase package alongside
    // Microsoft.ML.Tokenizers; CreateForModel throws InvalidOperationException at
    // runtime without it — measured, phase 62.
    private static readonly Tokenizer Tokenizer = TiktokenTokenizer.CreateForModel("gpt-4o");

    private readonly IModelProviderRegistry _registry;
    private readonly IOptionsMonitor<AgentPrismOptions> _optionsMonitor;

    /// <summary>Creates a new estimator.</summary>
    /// <param name="registry">The registry used to look up the model's context window.</param>
    /// <param name="optionsMonitor">The runtime settings, for <see cref="AgentPrismPreflightOptions.ReserveRatio"/>.</param>
    /// <exception cref="ArgumentNullException">A dependency is <see langword="null"/>.</exception>
    public ContextWindowEstimator(IModelProviderRegistry registry, IOptionsMonitor<AgentPrismOptions> optionsMonitor)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(optionsMonitor);

        _registry = registry;
        _optionsMonitor = optionsMonitor;
    }

    /// <summary>Estimates <paramref name="prompt"/> against <paramref name="binding"/>'s context window.</summary>
    /// <param name="binding">The model binding whose context window applies.</param>
    /// <param name="prompt">The prompt text to count. Empty when <see langword="null"/>.</param>
    /// <returns>The estimate.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="binding"/> is <see langword="null"/>.</exception>
    public ContextWindowEstimate Estimate(ModelBinding binding, string? prompt)
    {
        ArgumentNullException.ThrowIfNull(binding);

        var promptTokens = Tokenizer.CountTokens(
            prompt ?? string.Empty,
            considerPreTokenization: true,
            considerNormalization: true);

        var contextWindowTokens = ModelCatalogLookup
            .Find(_registry, binding.Provider, binding.Model)?.ContextWindowTokens;

        int? allowedPromptTokens = null;
        var wouldBeRejected = false;

        if (contextWindowTokens is { } window)
        {
            var reserveRatio = Math.Clamp(_optionsMonitor.CurrentValue.Preflight.ReserveRatio, 0, 1);
            allowedPromptTokens = (int)(window * (1 - reserveRatio));
            wouldBeRejected = promptTokens > allowedPromptTokens;
        }

        return new ContextWindowEstimate
        {
            PromptTokens = promptTokens,
            ContextWindowTokens = contextWindowTokens,
            AllowedPromptTokens = allowedPromptTokens,
            WouldBeRejected = wouldBeRejected,
        };
    }
}
