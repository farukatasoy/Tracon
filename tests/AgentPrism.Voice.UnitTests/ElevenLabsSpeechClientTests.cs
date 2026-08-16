using System.Net;
using AgentPrism.Voice.UnitTests.Infrastructure;
using Shouldly;

namespace AgentPrism.Voice.UnitTests;

/// <summary>
/// Verifies the ElevenLabs client's HTTP contract with a fake handler.
/// NO test reaches the real provider.
/// </summary>
public sealed class ElevenLabsSpeechClientTests
{
    /// <summary>A valid MP3 header: ID3 tag + at least one byte.</summary>
    private static readonly byte[] Mp3Bytes = [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

    [Fact]
    public async Task Synthesis_goes_to_the_right_path_and_header()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler, options => options.ApiKey = "FAKE-KEY-123");

        _ = await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.AbsolutePath.ShouldBe("/v1/text-to-speech/voice-1");
        request.Uri.Query.ShouldContain("output_format=mp3_44100_128");

        // 🚨 The authentication header is `xi-api-key`; NOT `Authorization: Bearer`.
        request.Headers.ShouldContainKey("xi-api-key");
        request.Headers.ShouldNotContainKey("Authorization");

        request.ContentType.ShouldBe("application/json");
        request.Body.ShouldNotBeNull();
        request.Body.ShouldContain("\"text\":\"hello\"");
    }

    [Fact]
    public async Task Streaming_synthesis_goes_to_the_stream_path_and_returns_chunks()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler);

        var chunks = new List<int>();

        await foreach (var chunk in client.SynthesizeStreamingAsync(
                           new SpeechRequest { Text = "hello" },
                           TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk.Length);
        }

        chunks.Sum().ShouldBe(Mp3Bytes.Length);
        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/v1/text-to-speech/voice-1/stream");
    }

    [Fact]
    public async Task Voice_list_is_read_from_the_v2_endpoint_and_sorted_by_name()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """
            {"voices":[
              {"voice_id":"b","name":"Zoe","category":"premade"},
              {"voice_id":"a","name":"Amy","category":"cloned"}
            ]}
            """));

        using var client = CreateClient(handler);

        var voices = await client.ListVoicesAsync(TestContext.Current.CancellationToken);

        // 🚨 v2: `/v1/voices` is the legacy surface.
        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/v2/voices");

        voices.Count.ShouldBe(2);
        voices[0].Name.ShouldBe("Amy");
        voices[1].Name.ShouldBe("Zoe");
        voices[1].VoiceId.ShouldBe("b");
    }

    [Fact]
    public async Task Transcript_is_sent_with_a_multipart_body()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"text":"hello world","language_code":"en","language_probability":0.98,"audio_duration_secs":3.5}"""));

        using var client = CreateClient(handler);
        using var audio = new MemoryStream(Mp3Bytes);

        var transcript = await client.TranscribeAsync(
            audio,
            "audio/mpeg",
            cancellationToken: TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldBe("/v1/speech-to-text");

        // 🚨 The endpoint does NOT accept a JSON body; multipart is required.
        request.ContentType.ShouldBe("multipart/form-data");
        request.Body.ShouldNotBeNull();
        request.Body.ShouldContain("model_id");

        transcript.Text.ShouldBe("hello world");
        transcript.LanguageCode.ShouldBe("en");
        transcript.AudioDuration.ShouldBe(TimeSpan.FromSeconds(3.5));
    }

    [Fact]
    public async Task Error_is_explicit_when_there_is_no_voice_id()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler, options => options.DefaultVoiceId = null);

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "hello" },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("DefaultVoiceId");
        exception.Message.ShouldContain("list_voices");
    }

    [Fact]
    public async Task Failed_response_body_does_NOT_carry_over_into_the_error()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            // The provider's body echoes the request (and sometimes a piece of the key).
            Content = new StringContent("""{"detail":"invalid api key FAKE-KEY-123 for secret-text"}"""),
        });

        using var client = CreateClient(handler, options => options.ApiKey = "FAKE-KEY-123");

        var exception = await Should.ThrowAsync<AgentPrismException>(
            async () => await client.SynthesizeAsync(
                new SpeechRequest { Text = "secret-text" },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("401");
        exception.Message.ShouldNotContain("FAKE-KEY-123");
        exception.Message.ShouldNotContain("secret-text");
    }

    [Fact]
    public async Task Usage_is_marked_ESTIMATED_when_the_provider_does_not_report_a_character_count()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Binary(Mp3Bytes));
        using var client = CreateClient(handler);

        var audio = await client.SynthesizeAsync(
            new SpeechRequest { Text = "seven chars" },
            TestContext.Current.CancellationToken);

        audio.UsageSource.ShouldBe(SpeechUsageSource.Estimated);
        audio.CharactersBilled.ShouldBe("seven chars".Length);
    }

    [Fact]
    public async Task Usage_is_marked_PROVIDER_when_the_provider_reports_a_character_count()
    {
        var handler = new StubHttpMessageHandler(_ =>
        {
            var response = StubHttpMessageHandler.Binary(Mp3Bytes);
            response.Headers.TryAddWithoutValidation("character-cost", "42");
            return response;
        });

        using var client = CreateClient(handler);

        var audio = await client.SynthesizeAsync(
            new SpeechRequest { Text = "hello" },
            TestContext.Current.CancellationToken);

        audio.UsageSource.ShouldBe(SpeechUsageSource.Provider);
        audio.CharactersBilled.ShouldBe(42);
    }

    [Fact]
    public async Task Health_check_reads_the_voice_list_and_produces_no_charge()
    {
        var handler = new StubHttpMessageHandler(_ => StubHttpMessageHandler.Json(
            """{"voices":[{"voice_id":"a","name":"Amy"}]}"""));

        using var client = CreateClient(handler);

        var health = await client.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.IsHealthy.ShouldBeTrue();
        health.VoiceCount.ShouldBe(1);
        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldBe("/v2/voices");
    }

    [Fact]
    public async Task Health_check_leaks_neither_key_nor_address_when_it_fails()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("nope"),
        });

        using var client = CreateClient(
            handler,
            options =>
            {
                options.ApiKey = "FAKE-KEY-123";
                options.Endpoint = new Uri("https://secret-server.example/");
            });

        var health = await client.CheckHealthAsync(TestContext.Current.CancellationToken);

        health.IsHealthy.ShouldBeFalse();
        health.Detail.ShouldNotBeNull();
        health.Detail.ShouldNotContain("FAKE-KEY-123");
        health.Detail.ShouldNotContain("secret-server");
    }

    [Fact]
    public void Format_is_converted_to_the_correct_MIME_type()
    {
        ElevenLabsSpeechClient.MediaTypeForFormat("mp3_44100_128").ShouldBe("audio/mpeg");
        ElevenLabsSpeechClient.MediaTypeForFormat("opus_48000_128").ShouldBe("audio/ogg");
        ElevenLabsSpeechClient.MediaTypeForFormat("wav_44100").ShouldBe("audio/wav");

        // An unknown format yields a type that cannot hide, NOT a silently wrong one.
        ElevenLabsSpeechClient.MediaTypeForFormat("pcm_16000").ShouldBe("application/octet-stream");
    }

    [Fact]
    public void Trailing_segment_is_kept_even_when_the_base_address_has_no_trailing_slash()
    {
        var combined = ElevenLabsSpeechClient.Combine(new Uri("https://example.test/proxy"), "v2/voices");

        combined.ToString().ShouldBe("https://example.test/proxy/v2/voices");
    }

    private static ElevenLabsSpeechClient CreateClient(
        StubHttpMessageHandler handler,
        Action<VoiceOptions>? configure = null)
    {
        var options = new VoiceOptions
        {
            ApiKey = "FAKE-KEY",
            DefaultVoiceId = "voice-1",
        };

        configure?.Invoke(options);

        return new ElevenLabsSpeechClient(options, new HttpClient(handler));
    }
}
