using System.Text.Json;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// A provider setting the built-in client cannot honour is refused before the network,
/// never dropped, and the refusal names the key but never the value.
/// </summary>
/// <remarks>
/// <para>
/// That decision and an earlier fix fix the rule for model bindings; this is the same rule for a speech
/// request, through the same code (<see cref="ModelProviderSettings"/>). The dialogue
/// service accepts an unknown <c>settings</c> field without an error (measured
/// 2026-10-13), so a dropped setting would never show.
/// </para>
/// <para>
/// Every value below is a marker that must not appear in the message: a value can be
/// the user's text or a credential pasted into the wrong field.
/// </para>
/// </remarks>
public sealed class SpeechProviderSettingsTests
{
    private const string Marker = "do-not-echo-me";

    [Theory]
    [InlineData("elevenlabs.bogus", "not recognized")]
    [InlineData("openai.temperature", "do not belong to the 'elevenlabs' provider")]
    [InlineData("stability", "do not belong to the 'elevenlabs' provider")]
    public async Task An_unknown_or_foreign_key_is_refused_for_a_single_voice(string key, string expected)
    {
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "hello", ProviderSettings = DialogueTestData.Settings((key, Marker)) },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(key);
        exception.Message.ShouldContain(expected);
        exception.Message.ShouldContain(ElevenLabsProviderNames.StabilitySetting, customMessage: "the message lists the supported keys");
        exception.Message.ShouldNotContain(Marker);
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("elevenlabs.bogus", "not recognized")]
    [InlineData("google.voice", "do not belong to the 'elevenlabs' provider")]
    public async Task An_unknown_or_foreign_key_is_refused_for_a_dialogue(string key, string expected)
    {
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(
                DialogueTestData.TwoTurns() with { ProviderSettings = DialogueTestData.Settings((key, Marker)) },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(key);
        exception.Message.ShouldContain(expected);
        exception.Message.ShouldNotContain(Marker);
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ElevenLabsProviderNames.StyleSetting)]
    [InlineData(ElevenLabsProviderNames.UseSpeakerBoostSetting)]
    [InlineData(ElevenLabsProviderNames.SpeedSetting)]
    public async Task A_single_voice_setting_is_refused_on_a_dialogue_with_its_own_reason(string key)
    {
        // Not "not recognized": the key is real, the dialogue service just has no such
        // field and would silently ignore it.
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(
                DialogueTestData.TwoTurns() with { ProviderSettings = DialogueTestData.Settings((key, 0.5)) },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(key);
        exception.Message.ShouldContain("no effect on a dialogue");
        exception.Message.ShouldContain(ElevenLabsProviderNames.SimilarityBoostSetting);
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ElevenLabsProviderNames.StabilitySetting, "a number")]
    [InlineData(ElevenLabsProviderNames.SpeedSetting, "a number")]
    [InlineData(ElevenLabsProviderNames.SeedSetting, "an integer")]
    [InlineData(ElevenLabsProviderNames.UseSpeakerBoostSetting, "a boolean")]
    [InlineData(ElevenLabsProviderNames.PreviousRequestIdsSetting, "a list of text")]
    public async Task A_value_of_the_wrong_type_is_refused_by_its_kind_not_its_value(string key, string expected)
    {
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "hello", ProviderSettings = DialogueTestData.Settings((key, Marker)) },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(key);
        exception.Message.ShouldContain(expected);
        exception.Message.ShouldContain("String", customMessage: "the JSON kind is named instead of the value");
        exception.Message.ShouldNotContain(Marker);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Text_where_a_setting_expects_text_is_not_echoed_when_it_fails_a_limit()
    {
        // The continuity text is the user's own words; a dialogue takes at most 100.
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);
        var longText = Marker + new string('x', 100);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(
                DialogueTestData.TwoTurns() with
                {
                    ProviderSettings = DialogueTestData.Settings((ElevenLabsProviderNames.PreviousTextSetting, longText)),
                },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(ElevenLabsProviderNames.PreviousTextSetting);
        exception.Message.ShouldContain("at most 100 characters");
        exception.Message.ShouldContain($"it has {longText.Length}");
        exception.Message.ShouldNotContain(Marker);
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ElevenLabsProviderNames.StabilitySetting, 1.5, "from 0 to 1")]
    [InlineData(ElevenLabsProviderNames.StabilitySetting, -0.1, "from 0 to 1")]
    [InlineData(ElevenLabsProviderNames.SimilarityBoostSetting, 2.0, "from 0 to 1")]
    [InlineData(ElevenLabsProviderNames.SeedSetting, -1.0, "from 0 to 4294967295")]
    [InlineData(ElevenLabsProviderNames.SeedSetting, 4294967296.0, "from 0 to 4294967295")]
    public async Task A_value_out_of_the_providers_range_is_refused_before_the_network(string key, double value, string expected)
    {
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);
        var settings = string.Equals(key, ElevenLabsProviderNames.SeedSetting, StringComparison.Ordinal)
            ? DialogueTestData.Settings((key, (long)value))
            : DialogueTestData.Settings((key, value));

        var single = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "hello", ProviderSettings = settings },
                TestContext.Current.CancellationToken));

        var dialogue = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeDialogueAsync(
                DialogueTestData.TwoTurns() with { ProviderSettings = settings },
                TestContext.Current.CancellationToken));

        single.Message.ShouldContain(key);
        single.Message.ShouldContain(expected);
        dialogue.Message.ShouldBe(single.Message);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task More_than_three_request_ids_are_refused()
    {
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest
                {
                    Text = "hello",
                    ProviderSettings = DialogueTestData.Settings(
                        (ElevenLabsProviderNames.PreviousRequestIdsSetting, new[] { "r1", "r2", "r3", Marker })),
                },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("at most 3 request ids; it has 4");
        exception.Message.ShouldNotContain(Marker);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_streaming_path_refuses_an_unknown_key_before_the_network_too()
    {
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);

        await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var chunk in client.SynthesizeStreamingAsync(
                               new SpeechRequest { Text = "hello", ProviderSettings = DialogueTestData.Settings(("elevenlabs.bogus", 1)) },
                               TestContext.Current.CancellationToken))
            {
                _ = chunk;
            }
        });

        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Keys_match_regardless_of_casing_and_text_values_are_read_as_their_type()
    {
        // A dictionary read back from storage or bound from configuration may carry an
        // ordinal comparer and text values.
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["ELEVENLABS.STABILITY"] = JsonSerializer.SerializeToElement("0.25"),
            ["ElevenLabs.UseSpeakerBoost"] = JsonSerializer.SerializeToElement("false"),
        };

        _ = await client.SynthesizeAsync(
            new SpeechRequest { Text = "hello", ProviderSettings = settings },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        var voiceSettings = body.RootElement.GetProperty("voice_settings");
        voiceSettings.GetProperty("stability").GetDouble().ShouldBe(0.25);
        voiceSettings.GetProperty("use_speaker_boost").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_null_or_unassigned_value_counts_as_absent()
    {
        var handler = NewHandler();
        using var client = DialogueTestData.CreateClient(handler);
        var settings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            [ElevenLabsProviderNames.StabilitySetting] = JsonSerializer.SerializeToElement<double?>(null),
            [ElevenLabsProviderNames.SeedSetting] = default,
        };

        _ = await client.SynthesizeAsync(
            new SpeechRequest { Text = "hello", ProviderSettings = settings },
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Body.ShouldBe("""{"text":"hello","model_id":"eleven_multilingual_v2"}""");
    }

    private static StubHttpMessageHandler NewHandler() => new(_ => DialogueTestData.RawResponse());
}
