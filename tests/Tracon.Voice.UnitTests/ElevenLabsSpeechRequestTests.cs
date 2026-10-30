using System.Text.Json;
using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// The single-voice body carries the provider settings, and a request without settings
/// sends exactly the body it sent before they existed.
/// </summary>
/// <remarks>
/// <c>grep -c voice_settings ElevenLabsSpeechClient.cs</c> was <c>0</c>:
/// no voice setting reached the provider. The field names follow the provider's
/// OpenAPI document (<c>Body_text_to_speech_full</c>, 2026-10-13).
/// </remarks>
public sealed class ElevenLabsSpeechRequestTests
{
    [Fact]
    public async Task A_request_without_settings_sends_the_same_body_as_before()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken);
        _ = await client.SynthesizeAsync(
            new SpeechRequest { Text = "hello", LanguageCode = "tr", IncludeTimestamps = false },
            TestContext.Current.CancellationToken);

        handler.Requests[0].Body.ShouldBe("""{"text":"hello","model_id":"eleven_multilingual_v2"}""");
        handler.Requests[1].Body.ShouldBe("""{"text":"hello","model_id":"eleven_multilingual_v2","language_code":"tr"}""");
    }

    [Fact]
    public async Task Voice_settings_and_continuity_fields_reach_the_body()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeAsync(
            new SpeechRequest
            {
                Text = "hello",
                ProviderSettings = DialogueTestData.Settings(
                    (ElevenLabsProviderNames.StabilitySetting, 0.3),
                    (ElevenLabsProviderNames.SimilarityBoostSetting, 0.75),
                    (ElevenLabsProviderNames.StyleSetting, 0.2),
                    (ElevenLabsProviderNames.UseSpeakerBoostSetting, true),
                    (ElevenLabsProviderNames.SpeedSetting, 1.1),
                    (ElevenLabsProviderNames.SeedSetting, 42),
                    (ElevenLabsProviderNames.PreviousTextSetting, "before"),
                    (ElevenLabsProviderNames.NextTextSetting, "after"),
                    (ElevenLabsProviderNames.PreviousRequestIdsSetting, new[] { "req-1" })),
            },
            TestContext.Current.CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.ShouldHaveSingleItem().Body!);
        var root = body.RootElement;
        var voiceSettings = root.GetProperty("voice_settings");

        voiceSettings.GetProperty("stability").GetDouble().ShouldBe(0.3);
        voiceSettings.GetProperty("similarity_boost").GetDouble().ShouldBe(0.75);
        voiceSettings.GetProperty("style").GetDouble().ShouldBe(0.2);
        voiceSettings.GetProperty("use_speaker_boost").GetBoolean().ShouldBeTrue();
        voiceSettings.GetProperty("speed").GetDouble().ShouldBe(1.1);
        root.GetProperty("seed").GetInt64().ShouldBe(42);
        root.GetProperty("previous_text").GetString().ShouldBe("before");
        root.GetProperty("next_text").GetString().ShouldBe("after");
        root.GetProperty("previous_request_ids").EnumerateArray().Select(static id => id.GetString()).ShouldBe(["req-1"]);
    }

    [Fact]
    public async Task A_voice_setting_left_out_is_not_sent_so_the_voices_own_value_applies()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        _ = await client.SynthesizeAsync(
            new SpeechRequest
            {
                Text = "hello",
                ProviderSettings = DialogueTestData.Settings((ElevenLabsProviderNames.SpeedSetting, 0.9)),
            },
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Body
            .ShouldBe("""{"text":"hello","model_id":"eleven_multilingual_v2","voice_settings":{"speed":0.9}}""");
    }

    [Fact]
    public async Task The_timestamped_and_streaming_paths_send_the_settings_too()
    {
        var handler = new StubHttpMessageHandler(request => request.RequestUri!.AbsolutePath.EndsWith("/with-timestamps", StringComparison.Ordinal)
            ? StubHttpMessageHandler.Json($$"""{"audio_base64":"{{Convert.ToBase64String(DialogueTestData.Mp3Bytes)}}"}""")
            : DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);
        var settings = DialogueTestData.Settings((ElevenLabsProviderNames.SeedSetting, 7));

        _ = await client.SynthesizeAsync(
            new SpeechRequest { Text = "hello", IncludeTimestamps = true, ProviderSettings = settings },
            TestContext.Current.CancellationToken);

        await foreach (var chunk in client.SynthesizeStreamingAsync(
                           new SpeechRequest { Text = "hello", ProviderSettings = settings },
                           TestContext.Current.CancellationToken))
        {
            chunk.Length.ShouldBeGreaterThan(0);
        }

        handler.Requests.Count.ShouldBe(2);
        handler.Requests.ShouldAllBe(request => request.Body!.Contains("\"seed\":7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_tenant_credential_is_refused_on_every_single_voice_path()
    {
        // The built-in client cannot use a tenant's key (4-A). Before that change, it
        // ignored one and called on the host's key: a direct caller billed the tenant's
        // speech to the host without a word.
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);
        var credential = new ModelProviderCredential { ApiKey = "tenant-key-value" };

        await Should.ThrowAsync<NotSupportedException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "hello", Credential = credential },
                TestContext.Current.CancellationToken));

        await Should.ThrowAsync<NotSupportedException>(async () =>
        {
            await foreach (var chunk in client.SynthesizeStreamingAsync(
                               new SpeechRequest { Text = "hello", Credential = credential },
                               TestContext.Current.CancellationToken))
            {
                chunk.Length.ShouldBeGreaterThan(0);
            }
        });

        using var audio = new MemoryStream([1, 2, 3]);

        await Should.ThrowAsync<NotSupportedException>(
            async () => await client.TranscribeAsync(
                audio,
                "audio/wav",
                new SpeechTranscriptionOptions { Credential = credential },
                TestContext.Current.CancellationToken));

        handler.Requests.ShouldBeEmpty();
    }
}
