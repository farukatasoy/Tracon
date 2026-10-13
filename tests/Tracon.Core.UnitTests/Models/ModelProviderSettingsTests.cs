using System.Text.Json;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Reading and validating the <see cref="ModelBinding.ProviderSettings"/> dictionary.
/// </summary>
/// <remarks>
/// This contract is shared by two provider packages; validating the rules
/// here avoids repeating them in every package.
/// </remarks>
public sealed class ModelProviderSettingsTests
{
    private static readonly string[] Supported = ["demo.enabled", "demo.budget", "demo.threshold"];

    [Fact]
    public void Empty_dictionary_passes_validation()
        => Should.NotThrow(() => ModelProviderSettings.Validate(Binding(), "demo", Supported));

    [Fact]
    public void Supported_keys_pass()
        => Should.NotThrow(() => ModelProviderSettings.Validate(
            Binding(("demo.enabled", true), ("demo.budget", 8)),
            "demo",
            Supported));

    [Fact]
    public void Unrecognized_key_lists_the_valid_keys()
    {
        var exception = Should.Throw<TraconException>(() => ModelProviderSettings.Validate(
            Binding(("demo.noSuchKey", true)),
            "demo",
            Supported));

        exception.Message.ShouldContain("demo.noSuchKey");
        exception.Message.ShouldContain("demo.enabled");
        exception.Message.ShouldContain("demo.budget");
        exception.Message.ShouldContain("demo.threshold");
    }

    [Fact]
    public void Key_with_a_different_prefix_gets_a_separate_error_message()
    {
        var exception = Should.Throw<TraconException>(() => ModelProviderSettings.Validate(
            Binding(("other.setting", true)),
            "demo",
            Supported));

        // A wrong prefix means "you wrote the setting on another provider"; it
        // calls for a different fix than an unrecognized key.
        exception.Message.ShouldContain("other.setting");
        exception.Message.ShouldContain("do not belong");
    }

    [Fact]
    public void Key_comparison_is_case_insensitive()
        => Should.NotThrow(() => ModelProviderSettings.Validate(
            Binding(("DEMO.Enabled", true)),
            "demo",
            Supported));

    [Fact]
    public void Provider_supporting_no_settings_gives_an_understandable_error()
        => Should.Throw<TraconException>(() => ModelProviderSettings.Validate(
                Binding(("demo.enabled", true)),
                "demo",
                []))
            .Message.ShouldContain("supports no extra settings");

    [Fact]
    public void Undefined_setting_returns_null()
    {
        ModelProviderSettings.ReadBoolean(Binding(), "demo.enabled").ShouldBeNull();
        ModelProviderSettings.ReadInt32(Binding(), "demo.budget").ShouldBeNull();
        ModelProviderSettings.ReadString(Binding(), "demo.threshold").ShouldBeNull();
    }

    [Fact]
    public void Boolean_value_is_read()
    {
        ModelProviderSettings.ReadBoolean(Binding(("demo.enabled", true)), "demo.enabled").ShouldBe(true);
        ModelProviderSettings.ReadBoolean(Binding(("demo.enabled", false)), "demo.enabled").ShouldBe(false);
    }

    [Fact]
    public void Boolean_value_given_as_text_is_also_read()
    {
        // Environment variable and command-line providers carry every value as text.
        ModelProviderSettings.ReadBoolean(Binding(("demo.enabled", "true")), "demo.enabled").ShouldBe(true);
    }

    [Fact]
    public void Integer_is_read_and_the_text_form_is_accepted()
    {
        ModelProviderSettings.ReadInt32(Binding(("demo.budget", 2048)), "demo.budget").ShouldBe(2048);
        ModelProviderSettings.ReadInt32(Binding(("demo.budget", "2048")), "demo.budget").ShouldBe(2048);
    }

    [Fact]
    public void Text_is_read_and_blank_text_is_treated_as_null()
    {
        ModelProviderSettings.ReadString(Binding(("demo.threshold", "BLOCK_NONE")), "demo.threshold").ShouldBe("BLOCK_NONE");
        ModelProviderSettings.ReadString(Binding(("demo.threshold", "  ")), "demo.threshold").ShouldBeNull();
    }

    [Fact]
    public void Wrong_type_gives_an_error_naming_the_expected_type()
    {
        Should.Throw<TraconException>(() => ModelProviderSettings.ReadBoolean(Binding(("demo.enabled", 3)), "demo.enabled"))
            .Message.ShouldContain("boolean");

        Should.Throw<TraconException>(() => ModelProviderSettings.ReadInt32(Binding(("demo.budget", true)), "demo.budget"))
            .Message.ShouldContain("integer");

        Should.Throw<TraconException>(() => ModelProviderSettings.ReadString(Binding(("demo.threshold", 3)), "demo.threshold"))
            .Message.ShouldContain("text");
    }

    [Fact]
    public void Unassigned_JsonElement_is_ignored()
    {
        // 🚨 An unassigned JsonElement's ValueKind is Undefined. The read path
        // must treat this as "absent"; throwing would break the entire run
        // because of a single missing field.
        var binding = new ModelBinding
        {
            Provider = "demo",
            Model = "demo-model",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo.enabled"] = default,
            },
        };

        ModelProviderSettings.ReadBoolean(binding, "demo.enabled").ShouldBeNull();
    }

    [Fact]
    public void Null_value_is_ignored()
    {
        var binding = new ModelBinding
        {
            Provider = "demo",
            Model = "demo-model",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo.budget"] = JsonSerializer.SerializeToElement<int?>(null),
            },
        };

        ModelProviderSettings.ReadInt32(binding, "demo.budget").ShouldBeNull();
    }

    [Fact]
    public void Reads_case_insensitively_even_from_an_ordinally_compared_dictionary()
    {
        // A dictionary deserialized from jsonb arrives with the default
        // (ordinal) comparer; the lookup cannot be left to the dictionary's own comparer.
        var binding = new ModelBinding
        {
            Provider = "demo",
            Model = "demo-model",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["Demo.Budget"] = JsonSerializer.SerializeToElement(64),
            },
        };

        ModelProviderSettings.ReadInt32(binding, "demo.budget").ShouldBe(64);
    }

    [Fact]
    public void Default_binding_has_an_empty_assigned_dictionary()
    {
        var binding = new ModelBinding { Provider = "demo", Model = "demo-model" };

        binding.ProviderSettings.ShouldNotBeNull();
        binding.ProviderSettings.ShouldBeEmpty();
    }

    [Fact]
    public void The_dictionary_overload_applies_the_same_rule_and_names_the_request_dictionary()
    {
        // Phase 203: a speech request's ProviderSettings is checked by the same code as a
        // model binding's (K-483); only what the message calls the dictionary differs.
        var settings = Binding(("other.setting", true), ("demo.noSuchKey", true)).ProviderSettings;

        var exception = Should.Throw<TraconException>(() => ModelProviderSettings.Validate(settings, "demo", Supported));

        exception.Message.ShouldStartWith("ProviderSettings is invalid:");
        exception.Message.ShouldNotContain(nameof(ModelBinding));
        exception.Message.ShouldContain("do not belong to the 'demo' provider: other.setting");
        exception.Message.ShouldContain("not recognized: demo.noSuchKey");
        exception.Message.ShouldContain("Supported keys: demo.budget, demo.enabled, demo.threshold");

        Should.NotThrow(() => ModelProviderSettings.Validate(
            Binding(("DEMO.Enabled", true)).ProviderSettings,
            "demo",
            Supported));
        Should.NotThrow(() => ModelProviderSettings.Validate(new Dictionary<string, JsonElement>(StringComparer.Ordinal), "demo", []));
    }

    [Fact]
    public void The_binding_overload_keeps_its_message()
    {
        var exception = Should.Throw<TraconException>(() => ModelProviderSettings.Validate(
            Binding(("other.setting", true)),
            "demo",
            Supported));

        exception.Message.ShouldStartWith("ModelBinding.ProviderSettings is invalid:");
        exception.Message.ShouldContain("provider named in ModelBinding.Provider");
    }

    [Fact]
    public void The_dictionary_overload_refuses_null_and_an_empty_prefix()
    {
        Should.Throw<ArgumentNullException>(() => ModelProviderSettings.Validate((IReadOnlyDictionary<string, JsonElement>)null!, "demo", Supported));
        Should.Throw<ArgumentException>(() => ModelProviderSettings.Validate(new Dictionary<string, JsonElement>(StringComparer.Ordinal), " ", Supported));
        Should.Throw<ArgumentNullException>(() => ModelProviderSettings.Validate(new Dictionary<string, JsonElement>(StringComparer.Ordinal), "demo", null!));
    }

    [Fact]
    public void A_wrong_type_is_named_by_its_JSON_kind_never_by_its_value()
    {
        const string marker = "pasted-credential-value";

        var exception = Should.Throw<TraconException>(
            () => ModelProviderSettings.ReadInt32(Binding(("demo.budget", marker)), "demo.budget"));

        exception.Message.ShouldContain("demo.budget");
        exception.Message.ShouldContain("String");
        exception.Message.ShouldNotContain(marker);
    }

    [Fact]
    public void The_dictionary_readers_read_numbers_lists_and_large_integers()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["demo.ratio"] = JsonSerializer.SerializeToElement(0.25),
            ["demo.ratioText"] = JsonSerializer.SerializeToElement("0.5"),
            ["demo.seed"] = JsonSerializer.SerializeToElement(4294967295L),
            ["demo.ids"] = JsonSerializer.SerializeToElement(new[] { "a", "b" }),
            ["demo.blankIds"] = JsonSerializer.SerializeToElement(new[] { "a", " " }),
            ["demo.mixedIds"] = JsonSerializer.SerializeToElement(new object[] { "a", 3 }),
        };

        ModelProviderSettings.ReadDouble(settings, "demo.ratio").ShouldBe(0.25);
        ModelProviderSettings.ReadDouble(settings, "demo.ratioText").ShouldBe(0.5);
        ModelProviderSettings.ReadDouble(settings, "demo.missing").ShouldBeNull();
        ModelProviderSettings.ReadInt64(settings, "demo.seed").ShouldBe(4294967295L);
        Should.Throw<TraconException>(() => ModelProviderSettings.ReadInt32(settings, "demo.seed"));
        ModelProviderSettings.ReadStringList(settings, "demo.ids").ShouldBe(["a", "b"]);
        Should.Throw<TraconException>(() => ModelProviderSettings.ReadStringList(settings, "demo.blankIds"))
            .Message.ShouldContain("a list of non-blank text");
        Should.Throw<TraconException>(() => ModelProviderSettings.ReadStringList(settings, "demo.mixedIds"));
        Should.Throw<TraconException>(() => ModelProviderSettings.ReadDouble(settings, "demo.ids"))
            .Message.ShouldContain("a number");
    }

    private static ModelBinding Binding(params (string Key, object Value)[] entries)
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in entries)
        {
            settings[key] = value switch
            {
                bool boolean => JsonSerializer.SerializeToElement(boolean),
                int number => JsonSerializer.SerializeToElement(number),
                _ => JsonSerializer.SerializeToElement(value.ToString()),
            };
        }

        return new ModelBinding { Provider = "demo", Model = "demo-model", ProviderSettings = settings };
    }
}
