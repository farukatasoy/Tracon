using System.Text.Json;

namespace Tracon.Testing.Contracts.Providers;

/// <summary>
/// Behavior tests for how a provider handles
/// <see cref="ModelBinding.ProviderSettings"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every provider owes the one rule Tracon fixes for all of them: an unknown
/// key is <em>not ignored silently</em>. This holds for a provider that
/// offers no tunable of its own as well — it rejects every key. A setting
/// that quietly does nothing makes the host author miss the behavior they
/// configured without ever seeing why, which is the same reasoning that makes
/// an unparsable <see cref="ModelBinding.ReasoningEffort"/> stop compilation
/// instead of falling back to a default.
/// </para>
/// <para>
/// Derive this class for every provider. A provider that supports at least
/// one key overrides <see cref="SupportedSetting"/> with it; a provider that
/// supports none leaves it <see langword="null"/>, and the scenarios that need
/// a supported key are reported as skipped.
/// </para>
/// <para>
/// <c>ModelProviderSettings.Validate</c> in <c>Tracon.Abstractions</c>
/// implements this rule, including the prefix convention (<c>anthropic.*</c>,
/// <c>google.*</c>) that keeps one provider from silently accepting another's
/// keys. Pass an empty list of supported keys when the provider supports none.
/// Use it rather than a hand-rolled check: the model provider registry passes
/// its exception through with the offending key named, but masks the message
/// of any other exception a provider throws while it builds a client. These
/// scenarios call the provider directly, below the registry, so they cannot
/// see that difference.
/// </para>
/// </remarks>
public abstract class ModelProviderSettingsContract : ModelProviderContract
{
    private const string NoSupportedSetting =
        "This provider supports no provider setting key, so there is no accepted key to test.";

    private const string NoInvalidSettingValue =
        "This provider declares no setting with a closed set of values, so there is no wrong value to test.";

    /// <summary>
    /// A provider setting key this provider supports, with a value it accepts,
    /// or <see langword="null"/> when the provider supports no key.
    /// </summary>
    /// <remarks>
    /// When given, it proves the rejection below is about the key being
    /// unknown, not about settings being rejected wholesale. The default is
    /// <see langword="null"/>.
    /// </remarks>
    protected virtual KeyValuePair<string, JsonElement>? SupportedSetting => null;

    /// <summary>
    /// A setting key this provider supports, with a value it must reject, or
    /// <see langword="null"/> when no supported key has a closed set of values.
    /// </summary>
    /// <remarks>
    /// A known key with a wrong value is the same mistake as an unknown key — a
    /// typo the host author cannot see — so it fails the same way, naming the
    /// key. Choose a value of the right JSON type that is outside the accepted
    /// set (for example an effort level the provider does not define), so the
    /// case proves value validation and not only type checking. The default is
    /// <see langword="null"/>, which reports the scenario as skipped.
    /// </remarks>
    protected virtual KeyValuePair<string, JsonElement>? InvalidSettingValue => null;

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
    {
        var supported = SupportedSetting;
        Assert.SkipWhen(supported is null, NoSupportedSetting);
        var (key, value) = supported.Value;

        Should.NotThrow(() => Provider.CreateChatClient(BindingWith(key, value)));
    }

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

    [Fact]
    public void A_supported_setting_with_an_invalid_value_is_rejected()
    {
        var invalid = InvalidSettingValue;
        Assert.SkipWhen(invalid is null, NoInvalidSettingValue);
        var (key, value) = invalid.Value;

        var exception = Should.Throw<TraconException>(() => Provider.CreateChatClient(BindingWith(key, value)));

        exception.Message.ShouldContain(
            key,
            Case.Insensitive,
            "the message must name the key whose value is wrong, or the host author cannot find it");
    }

    /// <remarks>
    /// Keys arrive with the default (ordinal) comparer when the binding is
    /// read back from a <c>jsonb</c> column, so a provider must not rely on
    /// the dictionary's own comparer to match its keys case-insensitively.
    /// </remarks>
    [Fact]
    public void A_supported_setting_is_matched_regardless_of_key_casing()
    {
        var supported = SupportedSetting;
        Assert.SkipWhen(supported is null, NoSupportedSetting);
        var (key, value) = supported.Value;

        var shouted = key.ToUpperInvariant();
        var binding = Binding() with
        {
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [shouted] = value,
            },
        };

        Should.NotThrow(() => Provider.CreateChatClient(binding));
    }
}
