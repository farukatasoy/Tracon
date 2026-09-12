using System.Text.Json;
using Shouldly;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// Verifies <see cref="VoiceAttributeMapper"/>'s bounds and safe-scalar narrowing.
/// </summary>
/// <remarks>
/// The provider's own schema promises <c>labels</c> holds only string values,
/// but a malformed or future response must not crash the whole voice list's
/// deserialization — every "unsafe value" test here proves that boundary.
/// </remarks>
public sealed class VoiceAttributeMapperTests
{
    [Fact]
    public void Gender_label_is_carried_through()
    {
        var attributes = VoiceAttributeMapper.Map(Labels("""{"gender":"female"}"""), null);

        attributes[VoiceAttributeNames.Gender].ShouldBe("female");
    }

    [Fact]
    public void Null_labels_and_no_languages_yield_an_empty_but_non_null_collection()
    {
        var attributes = VoiceAttributeMapper.Map(null, null);

        attributes.ShouldNotBeNull();
        attributes.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_labels_dictionary_yields_an_empty_collection()
    {
        var attributes = VoiceAttributeMapper.Map(Labels("{}"), []);

        attributes.ShouldBeEmpty();
    }

    [Fact]
    public void Null_valued_label_is_skipped_safely()
    {
        var attributes = VoiceAttributeMapper.Map(Labels("""{"gender":null,"accent":"American"}"""), null);

        attributes.ShouldContainKey(VoiceAttributeNames.Accent);
        attributes.ShouldNotContainKey(VoiceAttributeNames.Gender);
    }

    [Fact]
    public void Object_valued_label_is_skipped_safely()
    {
        var attributes = VoiceAttributeMapper.Map(
            Labels("""{"gender":{"nested":"value"},"accent":"American"}"""),
            null);

        attributes.Count.ShouldBe(1);
        attributes.ShouldContainKey(VoiceAttributeNames.Accent);
        attributes.ShouldNotContainKey(VoiceAttributeNames.Gender);
    }

    [Fact]
    public void Numeric_valued_label_is_skipped_safely()
    {
        var attributes = VoiceAttributeMapper.Map(Labels("""{"age":42,"gender":"female"}"""), null);

        attributes.ShouldNotContainKey(VoiceAttributeNames.Age);
        attributes.ShouldContainKey(VoiceAttributeNames.Gender);
    }

    [Fact]
    public void Array_valued_label_is_skipped_safely()
    {
        var attributes = VoiceAttributeMapper.Map(Labels("""{"tags":["a","b"],"gender":"female"}"""), null);

        attributes.ShouldNotContainKey("tags");
        attributes.ShouldContainKey(VoiceAttributeNames.Gender);
    }

    [Fact]
    public void No_more_than_MaxAttributes_are_carried()
    {
        var json = "{" + string.Join(',', Enumerable.Range(0, 40).Select(i => $"\"key-{i:D2}\":\"v\"")) + "}";

        var attributes = VoiceAttributeMapper.Map(Labels(json), null);

        attributes.Count.ShouldBe(VoiceAttributeMapper.MaxAttributes);
    }

    [Fact]
    public void A_key_longer_than_the_limit_is_skipped()
    {
        var longKey = new string('k', VoiceAttributeMapper.MaxKeyLength + 1);
        var attributes = VoiceAttributeMapper.Map(
            Labels($$"""{"{{longKey}}":"v","gender":"female"}"""),
            null);

        attributes.ShouldNotContainKey(longKey);
        attributes.ShouldContainKey(VoiceAttributeNames.Gender);
    }

    [Fact]
    public void A_value_longer_than_the_limit_is_skipped()
    {
        var longValue = new string('v', VoiceAttributeMapper.MaxValueLength + 1);
        var attributes = VoiceAttributeMapper.Map(
            Labels($$"""{"description":"{{longValue}}","gender":"female"}"""),
            null);

        attributes.ShouldNotContainKey("description");
        attributes.ShouldContainKey(VoiceAttributeNames.Gender);
    }

    [Fact]
    public void Case_different_duplicate_keys_collapse_to_one_canonical_entry()
    {
        var attributes = VoiceAttributeMapper.Map(Labels("""{"Gender":"female","GENDER":"male"}"""), null);

        attributes.Count.ShouldBe(1);
        attributes[VoiceAttributeNames.Gender].ShouldBe("female");
    }

    [Fact]
    public void An_underscore_key_is_canonicalized_to_a_hyphen()
    {
        var attributes = VoiceAttributeMapper.Map(Labels("""{"use_case":"conversational"}"""), null);

        attributes[VoiceAttributeNames.UseCase].ShouldBe("conversational");
    }

    [Fact]
    public void Verified_languages_are_joined_deduplicated_and_sorted_into_one_attribute()
    {
        var languages = new List<ElevenLabsVerifiedLanguage>
        {
            new() { Language = "fr" },
            new() { Language = "en" },
            new() { Language = "EN" },
        };

        var attributes = VoiceAttributeMapper.Map(null, languages);

        attributes[VoiceAttributeNames.Language].ShouldBe("en,fr");
    }

    [Fact]
    public void Null_or_empty_language_entries_are_ignored()
    {
        var languages = new List<ElevenLabsVerifiedLanguage> { new() { Language = null }, new() { Language = "" } };

        var attributes = VoiceAttributeMapper.Map(null, languages);

        attributes.ShouldBeEmpty();
    }

    [Fact]
    public void Empty_verified_languages_list_yields_no_language_attribute()
    {
        VoiceAttributeMapper.Map(null, []).ShouldBeEmpty();
    }

    private static Dictionary<string, JsonElement> Labels(string json)
        => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
