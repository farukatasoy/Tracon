namespace Tracon;

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
    /// </remarks>
    public const string ThinkingBudgetTokensSetting = "anthropic.thinking.budgetTokens";

    /// <summary>
    /// The setting key that selects how the model thinks:
    /// <c>anthropic.thinking.mode</c> (text: <c>enabled</c> or <c>adaptive</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>enabled</c> sends <c>thinking: {type: "enabled", budget_tokens}</c> and
    /// requires <see cref="ThinkingBudgetTokensSetting"/>. It is the mode older
    /// models use; current models reject <c>budget_tokens</c> with <c>400</c>.
    /// When only <see cref="ThinkingBudgetTokensSetting"/> is set, the mode is
    /// <c>enabled</c>, as before this key existed.
    /// </para>
    /// <para>
    /// <c>adaptive</c> sends <c>thinking: {type: "adaptive"}</c>: the model decides
    /// when and how much to think, and <see cref="EffortSetting"/> sets the depth.
    /// It takes no budget; setting <see cref="ThinkingBudgetTokensSetting"/> with it
    /// is an error. Nothing is sent when neither key is set, so the model's own
    /// default applies — no setting changes an existing agent's behavior.
    /// </para>
    /// </remarks>
    public const string ThinkingModeSetting = "anthropic.thinking.mode";

    /// <summary>
    /// The setting key that sets the model's effort: <c>anthropic.effort</c>
    /// (text: <c>low</c>, <c>medium</c>, <c>high</c>, <c>xhigh</c> or <c>max</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sent as <c>output_config.effort</c>. Effort controls how many tokens the
    /// model spends — thinking depth and answer length together — and it is the
    /// main cost lever on current models. The accepted values come from the
    /// Anthropic SDK's own <c>Effort</c> enum, not from a list in Tracon; a model
    /// that does not support a level rejects the request with <c>400</c>.
    /// </para>
    /// <para>
    /// Thinking tokens count against <see cref="ModelBinding.MaxOutputTokens"/>.
    /// With <c>high</c> or above and no explicit limit, the request goes out with
    /// <see cref="AnthropicProviderOptions.DefaultMaxOutputTokens"/> (4096 by
    /// default), which a long answer can exceed. The agent validation report
    /// warns about this when the model catalog states a higher maximum.
    /// </para>
    /// <para>
    /// The value does not reach <see cref="ModelBinding.Fallbacks"/> links: a
    /// fallback link gets no provider settings.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var binding = new ModelBinding
    /// {
    ///     Provider = AnthropicProviderNames.Anthropic,
    ///     Model = "your-current-claude-model",
    ///     MaxOutputTokens = 64000,
    ///     ProviderSettings = new Dictionary&lt;string, JsonElement&gt;
    ///     {
    ///         [AnthropicProviderNames.EffortSetting] = JsonSerializer.SerializeToElement("high"),
    ///         [AnthropicProviderNames.ThinkingModeSetting] = JsonSerializer.SerializeToElement("adaptive"),
    ///         [AnthropicProviderNames.PromptCachingSetting] = JsonSerializer.SerializeToElement(true),
    ///         [AnthropicProviderNames.PromptCachingTtlSetting] = JsonSerializer.SerializeToElement("1h"),
    ///         [AnthropicProviderNames.PromptCachingPlacementSetting] = JsonSerializer.SerializeToElement("system"),
    ///     },
    /// };
    /// </code>
    /// </example>
    public const string EffortSetting = "anthropic.effort";

    /// <summary>
    /// The setting key that sets how long a prompt cache entry lives:
    /// <c>anthropic.promptCaching.ttl</c> (text: <c>5m</c> or <c>1h</c>).
    /// </summary>
    /// <remarks>
    /// Sent as <c>cache_control.ttl</c>. Requires <see cref="PromptCachingSetting"/>
    /// to be <see langword="true"/>; on its own it is an error, not a silent
    /// no-op. When it is not set, Anthropic's default (5 minutes) applies. A
    /// 1-hour entry costs more to write and pays off when the same prefix
    /// returns more than five minutes apart. The accepted values come from the
    /// Anthropic SDK's own <c>Ttl</c> enum.
    /// </remarks>
    public const string PromptCachingTtlSetting = "anthropic.promptCaching.ttl";

    /// <summary>
    /// The setting key that selects where the prompt cache breakpoint goes:
    /// <c>anthropic.promptCaching.placement</c> (text: <c>request</c> or <c>system</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>request</c> (the default) sends a top-level <c>cache_control</c>:
    /// Anthropic places the breakpoint on the last cacheable block, so the whole
    /// request is cached. Requests that share a conversation reuse it.
    /// </para>
    /// <para>
    /// <c>system</c> marks the end of the agent's instructions instead and sends
    /// no top-level <c>cache_control</c>. Requests that carry a different
    /// document or question after the same instructions then share the cached
    /// instruction prefix; with <c>request</c> each of them would write a new
    /// entry that the next one cannot read. When a request carries no
    /// instructions, the top-level breakpoint is used.
    /// </para>
    /// <para>
    /// Requires <see cref="PromptCachingSetting"/> to be <see langword="true"/>.
    /// </para>
    /// </remarks>
    public const string PromptCachingPlacementSetting = "anthropic.promptCaching.placement";

    /// <summary>All setting keys this provider supports.</summary>
    /// <remarks>
    /// A key absent from this list is not silently ignored; it fails compilation
    /// and the error message lists this list.
    /// </remarks>
    public static IReadOnlyList<string> SupportedSettings { get; } =
    [
        PromptCachingSetting,
        PromptCachingTtlSetting,
        PromptCachingPlacementSetting,
        ThinkingBudgetTokensSetting,
        ThinkingModeSetting,
        EffortSetting,
    ];
}
