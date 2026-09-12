using System.Text.Json;

namespace Tracon.Testing.Contracts.Providers;

/// <summary>
/// Behavior tests for a provider that validates
/// <see cref="ModelBinding.ProviderSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// Reading provider settings is <strong>optional</strong>: a provider that
/// offers no tunable of its own reads none, and derives neither this class nor
/// anything else. Deriving it states that the provider does read them and
/// therefore owes the one rule Tracon fixes for all of them — an unknown
/// key is <em>not ignored silently</em>. A setting that quietly does nothing
/// makes the host author miss the behavior they configured without ever seeing
/// why, which is the same reasoning that makes an unparsable
/// <see cref="ModelBinding.ReasoningEffort"/> stop compilation instead of
/// falling back to a default.
/// </para>
/// <para>
/// <c>ModelProviderSettings.Validate</c> in <c>Tracon.Abstractions</c>
/// implements this rule, including the prefix convention (<c>anthropic.*</c>,
/// <c>google.*</c>) that keeps one provider from silently accepting another's
/// keys. A provider is free to hand-roll the check as long as the observable
/// behavior below holds.
/// </para>
/// </remarks>
public abstract class ModelProviderSettingsContract : ModelProviderContract
{
    /// <summary>
    /// A provider setting key this provider supports, with a value it accepts.
    /// Proves the rejection below is about the key being unknown, not about
    /// settings being rejected wholesale.
    /// </summary>
    protected abstract KeyValuePair<string, JsonElement> SupportedSetting { get; }

    /// <summary>
    /// A setting key this provider does not support. Defaults to a key under
    /// this provider's own prefix that no provider could plausibly define.
    /// </summary>
    protected virtual string UnsupportedSettingKey
        => $"{Provider.Name}.contractUnsupportedSetting";

    // JsonDocument.Parse, not JsonSerializer.SerializeToElement: this package
    // stays trim- and AOT-clean, and the serializer's generic overload is
    // annotated RequiresDynamicCode/RequiresUnreferencedCode.
    private static JsonElement True { get; } = JsonDocument.Parse("true").RootElement.Clone();

    private ModelBinding BindingWith(string key, JsonElement value)
        => Binding() with
        {
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [key] = value,
            },
        };

    [Fact]
    public void A_supported_provider_setting_is_accepted()
        => Should.NotThrow(() => Provider.CreateChatClient(BindingWith(SupportedSetting.Key, SupportedSetting.Value)));

    [Fact]
    public void An_unsupported_provider_setting_is_rejected()
    {
        var binding = BindingWith(UnsupportedSettingKey, True);

        var exception = Should.Throw<TraconException>(() => Provider.CreateChatClient(binding));

        exception.Message.ShouldContain(
            UnsupportedSettingKey,
            Case.Insensitive,
            "the message must name the offending key, or the host author cannot find it");
    }

    /// <remarks>
    /// Keys arrive with the default (ordinal) comparer when the binding is
    /// read back from a <c>jsonb</c> column, so a provider must not rely on
    /// the dictionary's own comparer to match its keys case-insensitively.
    /// </remarks>
    [Fact]
    public void A_supported_setting_is_matched_regardless_of_key_casing()
    {
        var shouted = SupportedSetting.Key.ToUpperInvariant();
        var binding = Binding() with
        {
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [shouted] = SupportedSetting.Value,
            },
        };

        Should.NotThrow(() => Provider.CreateChatClient(binding));
    }
}
