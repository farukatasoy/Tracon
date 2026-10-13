using Anthropic.Core;
using SdkEffort = Anthropic.Models.Messages.Effort;
using SdkTtl = Anthropic.Models.Messages.Ttl;

namespace Tracon;

/// <summary>Where the prompt cache breakpoint goes (<see cref="AnthropicProviderNames.PromptCachingPlacementSetting"/>).</summary>
internal enum AnthropicCachePlacement
{
    /// <summary>A top-level <c>cache_control</c>; Anthropic places the breakpoint on the last cacheable block.</summary>
    Request,

    /// <summary>A breakpoint on the last block of the agent's instructions.</summary>
    System,
}

/// <summary>How the model thinks (<see cref="AnthropicProviderNames.ThinkingModeSetting"/>).</summary>
internal enum AnthropicThinkingMode
{
    /// <summary>No <c>thinking</c> field is sent; the model's own default applies.</summary>
    Unset,

    /// <summary><c>{type: "enabled", budget_tokens}</c>.</summary>
    Enabled,

    /// <summary><c>{type: "adaptive"}</c>.</summary>
    Adaptive,
}

/// <summary>
/// The <c>anthropic.*</c> provider settings of one binding, read and checked once
/// when its chat client is built.
/// </summary>
/// <remarks>
/// <para>
/// A wrong value or a combination that cannot work is an error here, in the
/// <see cref="ModelProviderSettings.Validate(ModelBinding, string, IReadOnlyCollection{string})"/> pattern: the registry
/// wraps it into a compilation error that names the agent, and the validation
/// endpoint reports it as <c>invalid_setting</c>. Nothing is ignored silently.
/// </para>
/// <para>
/// The effort and TTL values are read from the Anthropic SDK's own enums
/// (<c>Anthropic.Models.Messages.Effort</c> and <c>Ttl</c>) through the SDK's
/// own wire mapping, so a value the SDK adds is accepted without a Tracon
/// release and Tracon keeps no list of its own.
/// </para>
/// </remarks>
internal sealed record AnthropicRequestSettings
{
    private const string RequestPlacement = "request";
    private const string SystemPlacement = "system";
    private const string EnabledMode = "enabled";
    private const string AdaptiveMode = "adaptive";

    /// <summary>The wire values of <c>output_config.effort</c>, in the SDK's order.</summary>
    internal static IReadOnlyList<string> EffortValues { get; } =
        [.. Enum.GetValues<SdkEffort>().Select(static value => ((ApiEnum<string, SdkEffort>)value).Raw())];

    /// <summary>The wire values of <c>cache_control.ttl</c>, in the SDK's order.</summary>
    internal static IReadOnlyList<string> CacheTtlValues { get; } =
        [.. Enum.GetValues<SdkTtl>().Select(static value => ((ApiEnum<string, SdkTtl>)value).Raw())];

    /// <summary>The effort levels at which thinking tokens are likely to fill a small output limit.</summary>
    private static readonly HashSet<string> HighEffortValues = new(
        [
            ((ApiEnum<string, SdkEffort>)SdkEffort.High).Raw(),
            ((ApiEnum<string, SdkEffort>)SdkEffort.Xhigh).Raw(),
            ((ApiEnum<string, SdkEffort>)SdkEffort.Max).Raw(),
        ],
        StringComparer.Ordinal);

    /// <summary>Gets whether prompt caching is on.</summary>
    public bool PromptCaching { get; init; }

    /// <summary>Gets the cache entry TTL wire value, or <see langword="null"/> for Anthropic's default.</summary>
    public string? CacheTtl { get; init; }

    /// <summary>Gets where the cache breakpoint goes.</summary>
    public AnthropicCachePlacement CachePlacement { get; init; }

    /// <summary>Gets the thinking mode.</summary>
    public AnthropicThinkingMode ThinkingMode { get; init; }

    /// <summary>Gets the thinking budget for <see cref="AnthropicThinkingMode.Enabled"/>.</summary>
    public int? ThinkingBudgetTokens { get; init; }

    /// <summary>Gets the <c>output_config.effort</c> wire value, or <see langword="null"/> for the model's default.</summary>
    public string? Effort { get; init; }

    /// <summary>Gets whether any setting changes the request body.</summary>
    public bool ChangesRequest => PromptCaching || ThinkingMode != AnthropicThinkingMode.Unset || Effort is not null;

    /// <summary>Gets whether the effort is high enough that thinking may fill a small output limit.</summary>
    public bool HighEffort => Effort is { } effort && HighEffortValues.Contains(effort);

    /// <summary>Reads and checks the settings of a binding.</summary>
    /// <param name="binding">The binding.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="TraconException">A key is unknown, a value is wrong, or two settings contradict each other.</exception>
    public static AnthropicRequestSettings Read(ModelBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        ModelProviderSettings.Validate(
            binding,
            AnthropicProviderNames.SettingsPrefix,
            AnthropicProviderNames.SupportedSettings);

        var promptCaching = ModelProviderSettings.ReadBoolean(binding, AnthropicProviderNames.PromptCachingSetting) ?? false;
        var ttl = ReadChoice(binding, AnthropicProviderNames.PromptCachingTtlSetting, CacheTtlValues);
        var placement = ReadChoice(binding, AnthropicProviderNames.PromptCachingPlacementSetting, [RequestPlacement, SystemPlacement]);

        if (!promptCaching && (ttl is not null || placement is not null))
        {
            var key = ttl is not null
                ? AnthropicProviderNames.PromptCachingTtlSetting
                : AnthropicProviderNames.PromptCachingPlacementSetting;

            throw Invalid(
                $"'{key}' has no effect unless '{AnthropicProviderNames.PromptCachingSetting}' is true. " +
                $"Set '{AnthropicProviderNames.PromptCachingSetting}' to true, or remove '{key}'.");
        }

        var budget = ModelProviderSettings.ReadInt32(binding, AnthropicProviderNames.ThinkingBudgetTokensSetting);

        if (budget is { } invalidBudget && invalidBudget <= 0)
        {
            throw Invalid($"'{AnthropicProviderNames.ThinkingBudgetTokensSetting}' must be greater than zero.");
        }

        var mode = ReadChoice(binding, AnthropicProviderNames.ThinkingModeSetting, [EnabledMode, AdaptiveMode]) switch
        {
            EnabledMode => AnthropicThinkingMode.Enabled,
            AdaptiveMode => AnthropicThinkingMode.Adaptive,

            // Today's behavior: a budget on its own means enabled thinking.
            _ => budget is null ? AnthropicThinkingMode.Unset : AnthropicThinkingMode.Enabled,
        };

        if (mode == AnthropicThinkingMode.Enabled && budget is null)
        {
            throw Invalid(
                $"'{AnthropicProviderNames.ThinkingModeSetting}' is '{EnabledMode}', which requires " +
                $"'{AnthropicProviderNames.ThinkingBudgetTokensSetting}'. Current models reject a budget; use " +
                $"'{AdaptiveMode}' for them.");
        }

        if (mode == AnthropicThinkingMode.Adaptive && budget is not null)
        {
            throw Invalid(
                $"'{AnthropicProviderNames.ThinkingModeSetting}' is '{AdaptiveMode}', which takes no budget. " +
                $"Remove '{AnthropicProviderNames.ThinkingBudgetTokensSetting}', or set the mode to '{EnabledMode}'.");
        }

        return new AnthropicRequestSettings
        {
            PromptCaching = promptCaching,
            CacheTtl = ttl,
            CachePlacement = string.Equals(placement, SystemPlacement, StringComparison.Ordinal)
                ? AnthropicCachePlacement.System
                : AnthropicCachePlacement.Request,
            ThinkingMode = mode,
            ThinkingBudgetTokens = mode == AnthropicThinkingMode.Enabled ? budget : null,
            Effort = ReadChoice(binding, AnthropicProviderNames.EffortSetting, EffortValues),
        };
    }

    /// <summary>Reads a text setting that must be one of <paramref name="allowed"/>.</summary>
    /// <returns>The allowed value as it is written on the wire, or <see langword="null"/> when not set.</returns>
    private static string? ReadChoice(ModelBinding binding, string key, IReadOnlyList<string> allowed)
    {
        if (ModelProviderSettings.ReadString(binding, key) is not { } value)
        {
            return null;
        }

        foreach (var candidate in allowed)
        {
            if (string.Equals(candidate, value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        // The value is not repeated: it can be a credential pasted into the wrong
        // field, and the message reaches logs and run records (Faz 203).
        throw Invalid($"'{key}' has a value outside its set. Valid values: {string.Join(", ", allowed)}.");
    }

    private static ProviderSettingsValidationException Invalid(string message) => new(message);
}
