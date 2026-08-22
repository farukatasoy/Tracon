using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Security;

/// <summary>Tests for <see cref="AesGcmContentProtector"/>.</summary>
public sealed class AesGcmContentProtectorTests
{
    private static readonly string ValidKey = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Protect_then_unprotect_round_trips_the_plaintext()
    {
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey));

        var protectedValue = protector.Protect("hello world");

        string.Equals(protectedValue, "hello world", StringComparison.Ordinal).ShouldBeFalse();
        protector.Unprotect(protectedValue).ShouldBe("hello world");
    }

    [Fact]
    public void Protect_produces_a_different_ciphertext_each_time_the_same_plaintext_is_protected()
    {
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey));

        var first = protector.Protect("same input");
        var second = protector.Protect("same input");

        string.Equals(first, second, StringComparison.Ordinal).ShouldBeFalse();
        protector.Unprotect(first).ShouldBe("same input");
        protector.Unprotect(second).ShouldBe("same input");
    }

    [Fact]
    public void Unprotect_returns_a_never_protected_value_unchanged()
    {
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey));

        protector.Unprotect("plain text, never encrypted").ShouldBe("plain text, never encrypted");
    }

    [Fact]
    public void ProtectBytes_then_unprotect_bytes_round_trips_the_plaintext()
    {
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey));
        byte[] plaintext = [1, 2, 3, 4, 5, 250, 251, 252];

        var protectedBytes = protector.ProtectBytes(plaintext);

        protectedBytes.ShouldNotBe(plaintext);
        protector.UnprotectBytes(protectedBytes).ShouldBe(plaintext);
    }

    [Fact]
    public void UnprotectBytes_returns_never_protected_bytes_unchanged()
    {
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey));
        byte[] plaintext = [9, 8, 7];

        protector.UnprotectBytes(plaintext).ShouldBe(plaintext);
    }

    [Fact]
    public void IsEnabled_reflects_the_options_flag()
    {
        CreateProtector(enabled: true, activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey))
            .IsEnabled.ShouldBeTrue();
        CreateProtector(enabled: false, activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey))
            .IsEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Protect_throws_when_no_active_key_id_is_configured()
    {
        var protector = CreateProtector(activeKeyId: null);

        Should.Throw<AgentPrismException>(() => protector.Protect("x"));
    }

    [Fact]
    public void Protect_throws_when_the_active_key_id_has_no_entry_in_keys()
    {
        var protector = CreateProtector(activeKeyId: "missing");

        Should.Throw<AgentPrismException>(() => protector.Protect("x")).Message.ShouldContain("missing");
    }

    [Fact]
    public void Unprotect_throws_naming_the_key_id_when_the_envelopes_key_is_not_configured()
    {
        var writer = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", ValidKey));
        var protectedValue = writer.Protect("secret");

        var reader = CreateProtector(activeKeyId: "k2", keys: ("k2", "Keys:k2"), configuration: ("Keys:k2", ValidKey));

        Should.Throw<AgentPrismException>(() => reader.Unprotect(protectedValue)).Message.ShouldContain("k1");
    }

    [Fact]
    public void Protect_throws_when_the_configured_value_is_not_valid_base64()
    {
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", "not-base64!!!"));

        Should.Throw<AgentPrismException>(() => protector.Protect("x"));
    }

    [Fact]
    public void Protect_throws_when_the_decoded_key_is_not_32_bytes()
    {
        var shortKey = Convert.ToBase64String(new byte[16]);
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"), configuration: ("Keys:k1", shortKey));

        Should.Throw<AgentPrismException>(() => protector.Protect("x"));
    }

    [Fact]
    public void Protect_throws_when_the_configuration_key_has_no_value()
    {
        var protector = CreateProtector(activeKeyId: "k1", keys: ("k1", "Keys:k1"));

        Should.Throw<AgentPrismException>(() => protector.Protect("x"));
    }

    [Fact]
    public void Reading_an_old_key_id_still_works_after_the_active_key_rotates()
    {
        var options = new AgentPrismContentProtectionOptions { Enabled = true, ActiveKeyId = "k1" };
        options.Keys["k1"] = "Keys:k1";
        options.Keys["k2"] = "Keys:k2";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Keys:k1"] = ValidKey, ["Keys:k2"] = Convert.ToBase64String(new byte[32].Select(static (_, i) => (byte)i).ToArray()) })
            .Build();

        var protector = new AesGcmContentProtector(new StaticMonitor(options), configuration);
        var protectedWithK1 = protector.Protect("old data");

        options.ActiveKeyId = "k2";
        var protectedWithK2 = protector.Protect("new data");

        protector.Unprotect(protectedWithK1).ShouldBe("old data");
        protector.Unprotect(protectedWithK2).ShouldBe("new data");
    }

    private static AesGcmContentProtector CreateProtector(
        bool enabled = true,
        string? activeKeyId = null,
        (string KeyId, string ConfigurationKeyName)? keys = null,
        (string ConfigurationKeyName, string Value)? configuration = null)
    {
        var options = new AgentPrismContentProtectionOptions { Enabled = enabled, ActiveKeyId = activeKeyId };

        if (keys is { } k)
        {
            options.Keys[k.KeyId] = k.ConfigurationKeyName;
        }

        var configurationBuilder = new ConfigurationBuilder();

        if (configuration is { } c)
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { [c.ConfigurationKeyName] = c.Value });
        }

        return new AesGcmContentProtector(new StaticMonitor(options), configurationBuilder.Build());
    }

    private sealed class StaticMonitor(AgentPrismContentProtectionOptions value) : IOptionsMonitor<AgentPrismContentProtectionOptions>
    {
        public AgentPrismContentProtectionOptions CurrentValue => value;

        public AgentPrismContentProtectionOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<AgentPrismContentProtectionOptions, string?> listener) => null;
    }
}
