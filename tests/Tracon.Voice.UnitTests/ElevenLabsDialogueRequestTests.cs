using System.Text.Json;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The dialogue request the built-in client sends: path, query, header and body.
/// </summary>
/// <remarks>
/// The body shape follows the provider's OpenAPI document and a live two-turn request
/// (2026-10-13): <c>inputs[] {text, voice_id}</c>, <c>model_id</c>,
/// <c>language_code</c>, <c>settings {stability, similarity}</c>, <c>seed</c>,
/// <c>previous_text</c>, <c>future_text</c>, <c>previous_request_ids</c>.
/// </remarks>
public sealed class ElevenLabsDialogueRequestTests
{
    [Fact]
    public async Task A_dialogue_goes_to_the_dialogue_path_with_its_turns_in_order()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldBe("/v1/text-to-dialogue");
        request.Uri.Query.ShouldBe("?output_format=mp3_44100_128");
        request.Headers["xi-api-key"].ShouldBe("FAKE-KEY-DIALOGUE");
        request.Headers.ShouldNotContainKey("Authorization");
        request.ContentType.ShouldBe("application/json");

        using var body = JsonDocument.Parse(request.Body!);
        var inputs = body.RootElement.GetProperty("inputs").EnumerateArray().ToList();

        inputs.Count.ShouldBe(2);
        inputs[0].GetProperty("text").GetString().ShouldBe("Merhaba.");
        inputs[0].GetProperty("voice_id").GetString().ShouldBe("voice-a");
        inputs[1].GetProperty("text").GetString().ShouldBe("Selam.");
        inputs[1].GetProperty("voice_id").GetString().ShouldBe("voice-b");
    }

    [Fact]
    public async Task Timestamps_go_to_the_with_timestamps_path()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.TimestampedResponse(DialogueTestData.TwoSegments));
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns(includeTimestamps: true),
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/v1/text-to-dialogue/with-timestamps");
    }

    [Fact]
    public async Task The_request_names_its_model_language_and_format()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler, static options => options.DialogueModelId = "configured-dialogue");

        _ = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns() with { ModelId = "eleven_v4", LanguageCode = "tr", OutputFormat = "pcm_24000" },
            TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.Query.ShouldBe("?output_format=pcm_24000");

        using var body = JsonDocument.Parse(request.Body!);
        body.RootElement.GetProperty("model_id").GetString().ShouldBe("eleven_v4");
        body.RootElement.GetProperty("language_code").GetString().ShouldBe("tr");
    }

    [Fact]
    public async Task The_configured_dialogue_model_applies_when_the_request_names_none()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler, static options => options.DialogueModelId = "eleven_v4");

        var result = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        body.RootElement.GetProperty("model_id").GetString().ShouldBe("eleven_v4");
        result.Audio.ModelId.ShouldBe("eleven_v4");
    }

    [Fact]
    public async Task The_single_voice_model_is_never_sent_for_a_dialogue()
    {
        // eleven_multilingual_v2 is the single-voice default and not a dialogue model:
        // with no dialogue model set, the provider chooses, and the result names none.
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler, static options => options.SynthesisModelId = "eleven_multilingual_v2");

        var result = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        body.RootElement.TryGetProperty("model_id", out _).ShouldBeFalse();
        body.RootElement.TryGetProperty("language_code", out _).ShouldBeFalse();
        result.Audio.ModelId.ShouldBeNull();
    }

    [Fact]
    public async Task Provider_settings_map_to_the_dialogue_fields()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns() with
            {
                ProviderSettings = DialogueTestData.Settings(
                    (ElevenLabsProviderNames.StabilitySetting, 0.3),
                    (ElevenLabsProviderNames.SimilarityBoostSetting, 0.8),
                    (ElevenLabsProviderNames.SeedSetting, 4294967295L),
                    (ElevenLabsProviderNames.PreviousTextSetting, "before"),
                    (ElevenLabsProviderNames.NextTextSetting, "after"),
                    (ElevenLabsProviderNames.PreviousRequestIdsSetting, new[] { "req-1", "req-2" })),
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        var root = body.RootElement;
        var settings = root.GetProperty("settings");

        settings.GetProperty("stability").GetDouble().ShouldBe(0.3);

        // The dialogue service calls it "similarity"; the single-voice one "similarity_boost".
        settings.GetProperty("similarity").GetDouble().ShouldBe(0.8);
        settings.TryGetProperty("similarity_boost", out _).ShouldBeFalse();
        root.GetProperty("seed").GetInt64().ShouldBe(4294967295L);
        root.GetProperty("previous_text").GetString().ShouldBe("before");

        // The dialogue service's "next" text is future_text, not next_text.
        root.GetProperty("future_text").GetString().ShouldBe("after");
        root.TryGetProperty("next_text", out _).ShouldBeFalse();
        root.GetProperty("previous_request_ids").EnumerateArray().Select(static id => id.GetString()).ShouldBe(["req-1", "req-2"]);
    }

    [Fact]
    public async Task A_dialogue_without_settings_sends_no_settings_object()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        body.RootElement.EnumerateObject().Select(static property => property.Name).ShouldBe(["inputs"]);
    }

    [Fact]
    public async Task Only_stability_is_sent_when_only_stability_is_set()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns() with
            {
                ProviderSettings = DialogueTestData.Settings((ElevenLabsProviderNames.StabilitySetting, 0.3)),
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        body.RootElement.GetProperty("settings").EnumerateObject().Select(static property => property.Name).ShouldBe(["stability"]);
    }

    [Fact]
    public async Task Inline_tags_in_a_turn_pass_through_unchanged()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeDialogueAsync(
            new SpeechDialogueRequest
            {
                Turns = [new SpeechDialogueTurn { Text = "[laughs] Gerçekten mi?", VoiceId = "voice-a" }],
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        body.RootElement.GetProperty("inputs")[0].GetProperty("text").GetString().ShouldBe("[laughs] Gerçekten mi?");
    }
}
