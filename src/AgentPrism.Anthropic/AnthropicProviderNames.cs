namespace AgentPrism;

/// <summary>
/// The provider name registered by <c>UseAnthropic()</c> and the
/// <see cref="ModelBinding.ProviderSettings"/> keys.
/// </summary>
/// <remarks>
/// These names are <strong>stable</strong>. Agent definitions are stored in the
/// database under these names; changing them breaks stored definitions.
/// </remarks>
public static class AnthropicProviderNames
{
    /// <summary>The provider that uses the Anthropic Messages API: <c>anthropic</c>.</summary>
    public const string Anthropic = "anthropic";

    /// <summary>
    /// The prefix for provider-specific settings: <c>anthropic</c>.
    /// </summary>
    public const string SettingsPrefix = "anthropic";

    /// <summary>
    /// The setting key that turns on prompt caching: <c>anthropic.promptCaching</c> (boolean).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <strong>off</strong>. Turning it on lowers cost but changes
    /// behavior: <c>cache_control</c> is added to the request body, the fee for
    /// creating a cache entry is higher than the normal input fee for short
    /// prompts, and the model only caches prompts above a certain token
    /// threshold. It is opted in explicitly so there is no silent default.
    /// </para>
    /// <para>
    /// Measured against <c>claude-haiku-4-5-20251001</c>: with it on, the
    /// response reported <c>cache_creation_input_tokens=4209</c>; with it off, this
    /// counter never appeared.
    /// </para>
    /// </remarks>
    public const string PromptCachingSetting = "anthropic.promptCaching";

    /// <summary>
    /// The setting key that provides the extended-thinking budget:
    /// <c>anthropic.thinking.budgetTokens</c> (integer).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The value must be <strong>smaller</strong> than
    /// <see cref="ModelBinding.MaxOutputTokens"/>; otherwise Anthropic rejects the
    /// request.
    /// </para>
    /// <para>
    /// While thinking is on, Anthropic allows <see cref="ModelBinding.Temperature"/>
    /// to be only 1. Any other temperature gets the request rejected with
    /// <c>invalid_request_error</c> — measured against the live API.
    /// </para>
    /// </remarks>
    public const string ThinkingBudgetTokensSetting = "anthropic.thinking.budgetTokens";

    /// <summary>All setting keys this provider supports.</summary>
    /// <remarks>
    /// A key absent from this list is not silently ignored; it fails compilation
    /// and the error message lists this list.
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } =
    [
        PromptCachingSetting,
        ThinkingBudgetTokensSetting,
    ];
}
