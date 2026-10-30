using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using Tracon.Google.UnitTests.Infrastructure;

namespace Tracon.Google.UnitTests;

/// <summary>
/// The single-voice path of the Gemini speech synthesizer: the request it puts on the wire, the audio it
/// hands back, and every refusal that happens before the network.
/// </summary>
/// <remarks>
/// The answers are the shapes measured against the live API on 2026-10-25 (WAV with a provenance
/// chunk from the gemini-3.8 models, raw PCM with two different MIME types from the older ones).
/// </remarks>
public sealed class GoogleSpeechSynthesizerTests
{
    private const string Secretive = "do-not-echo-this-text";

    [Fact]
    public async Task A_request_asks_the_model_for_audio_with_the_voice_and_the_language()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.Pcm(), "audio/L16;codec=pcm;rate=24000"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        _ = await synthesizer.SynthesizeAsync(
            GoogleSpeechTestData.Speak("Merhaba.") with { LanguageCode = "tr" },
            TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();

        request.Uri.AbsolutePath.ShouldEndWith($"/models/{GoogleSpeechTestData.Model}:generateContent");
        request.ApiKey.ShouldBe(GoogleSpeechTestData.SetupKey);

        var body = request.Json;
        body.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString().ShouldBe("Merhaba.");

        var generation = body.GetProperty("generationConfig");
        generation.GetProperty("responseModalities").EnumerateArray().Select(static m => m.GetString()).ShouldBe(["AUDIO"]);

        var speech = generation.GetProperty("speechConfig");
        speech.GetProperty("languageCode").GetString().ShouldBe("tr");
        speech.GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString().ShouldBe("Kore");
    }

    [Fact]
    public async Task A_request_without_a_voice_sends_no_voice_config()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.Pcm(), "audio/L16;codec=pcm;rate=24000"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        _ = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(voice: null), TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Json
            .GetProperty("generationConfig").GetProperty("speechConfig")
            .TryGetProperty("voiceConfig", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_request_that_names_a_model_uses_it_instead_of_the_registered_one()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.Pcm(), "audio/L16;codec=pcm;rate=24000"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        _ = await synthesizer.SynthesizeAsync(
            GoogleSpeechTestData.Speak() with { ModelId = "gemini-other-tts" },
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldEndWith("/models/gemini-other-tts:generateContent");
    }

    [Fact]
    public async Task A_wav_answer_is_returned_as_the_model_sent_it_with_its_provenance_chunk()
    {
        var wav = GoogleSpeechTestData.WavWithProvenance(GoogleSpeechTestData.Pcm());
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(wav, "audio/wav"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var audio = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);

        audio.MediaType.ShouldBe("audio/wav");
        audio.Data.ToArray().ShouldBe(wav);
        audio.Duration.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Pcm_output_of_a_wav_answer_is_the_samples_alone()
    {
        var pcm = GoogleSpeechTestData.Pcm();
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.WavWithProvenance(pcm), "audio/wav"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var audio = await synthesizer.SynthesizeAsync(
            GoogleSpeechTestData.Speak() with { OutputFormat = "PCM" },
            TestContext.Current.CancellationToken);

        audio.Data.ToArray().ShouldBe(pcm);
        audio.MediaType.ShouldBe("audio/L16;rate=24000;channels=1");
    }

    [Theory]
    [InlineData("audio/L16;codec=pcm;rate=24000")]
    [InlineData("audio/l16; rate=24000; channels=1")]
    public async Task A_raw_pcm_answer_becomes_a_wav_file_at_the_rate_the_mime_type_states(string mimeType)
    {
        var pcm = GoogleSpeechTestData.Pcm(48_000);
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(pcm, mimeType));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var audio = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);

        var file = audio.Data.ToArray();

        audio.MediaType.ShouldBe("audio/wav");
        file.Length.ShouldBe(44 + pcm.Length);
        file.AsSpan(0, 4).SequenceEqual("RIFF"u8).ShouldBeTrue();
        file.AsSpan(8, 4).SequenceEqual("WAVE"u8).ShouldBeTrue();
        BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(4, 4)).ShouldBe(file.Length - 8);
        BinaryPrimitives.ReadInt16LittleEndian(file.AsSpan(22, 2)).ShouldBe((short)1);
        BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(24, 4)).ShouldBe(24_000);
        BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(28, 4)).ShouldBe(48_000);
        BinaryPrimitives.ReadInt16LittleEndian(file.AsSpan(34, 2)).ShouldBe((short)16);
        BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(40, 4)).ShouldBe(pcm.Length);
        file.AsSpan(44).ToArray().ShouldBe(pcm);
        audio.Duration.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task The_result_reports_the_model_the_response_id_and_an_estimated_character_count()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(
            GoogleSpeechTestData.Pcm(),
            "audio/L16;codec=pcm;rate=24000",
            modelVersion: "gemini-test-tts-001",
            responseId: "response-42"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var audio = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak("Twelve chars"), TestContext.Current.CancellationToken);

        audio.ModelId.ShouldBe("gemini-test-tts-001");
        audio.ProviderRequestId.ShouldBe("response-42");
        audio.CharactersBilled.ShouldBe(12);
        audio.UsageSource.ShouldBe(SpeechUsageSource.Estimated);
        audio.Alignment.ShouldBeNull();
    }

    [Fact]
    public async Task A_result_without_a_model_version_names_the_requested_model()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(
            GoogleSpeechTestData.Pcm(),
            "audio/L16;codec=pcm;rate=24000",
            modelVersion: null,
            responseId: null));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var audio = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);

        audio.ModelId.ShouldBe(GoogleSpeechTestData.Model);
        audio.ProviderRequestId.ShouldBeNull();
    }

    [Fact]
    public void The_synthesizer_reports_the_chat_provider_name_so_one_egress_rule_covers_speech()
    {
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(new GoogleStubHandler(_ => throw new InvalidOperationException()));

        synthesizer.ProviderName.ShouldBe(GoogleProviderNames.Google);
        synthesizer.SupportsTenantCredentials.ShouldBeTrue();
    }

    [Fact]
    public void The_limits_come_from_the_settings()
    {
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(
            new GoogleStubHandler(_ => throw new InvalidOperationException()),
            options =>
            {
                options.MaxCharactersPerRequest = 123;
                options.MaxDialogueCharactersPerRequest = 456;
            });

        synthesizer.MaxCharactersPerRequest.ShouldBe(123);
        synthesizer.DialogueCapabilities.MaxVoices.ShouldBe(2);
        synthesizer.DialogueCapabilities.MaxCharactersPerRequest.ShouldBe(456);
        synthesizer.DialogueCapabilities.SupportsSegmentTimestamps.ShouldBeFalse();
    }

    [Fact]
    public async Task The_voices_are_the_declared_ones_and_a_tenant_credential_changes_nothing()
    {
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(
            new GoogleStubHandler(_ => throw new InvalidOperationException("no call is made")),
            options => options.Voices.Add(new VoiceDescriptor { VoiceId = "Kore", Name = "Kore" }));

        var own = await synthesizer.ListVoicesAsync(TestContext.Current.CancellationToken);
        var tenant = await synthesizer.ListVoicesWithCredentialAsync(
            new ModelProviderCredential { ApiKey = GoogleSpeechTestData.TenantKey },
            TestContext.Current.CancellationToken);

        own.Select(static voice => voice.VoiceId).ShouldBe(["Kore"]);
        tenant.ShouldBe(own);
    }

    [Theory]
    [InlineData("empty text", "text cannot be empty")]
    [InlineData("timestamps", "IncludeTimestamps is not supported")]
    [InlineData("google setting", "google.speed")]
    [InlineData("foreign setting", "elevenlabs.stability")]
    [InlineData("other format", "'wav' and 'pcm'")]
    public async Task A_request_that_breaks_a_rule_is_refused_before_the_network(string because, string expected)
    {
        var handler = new GoogleStubHandler(_ => throw new InvalidOperationException("the network was reached"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var request = because switch
        {
            "empty text" => GoogleSpeechTestData.Speak("   "),
            "timestamps" => GoogleSpeechTestData.Speak(Secretive) with { IncludeTimestamps = true },
            "google setting" => GoogleSpeechTestData.Speak(Secretive) with
            {
                ProviderSettings = GoogleSpeechTestData.Settings(("google.speed", Secretive)),
            },
            "foreign setting" => GoogleSpeechTestData.Speak(Secretive) with
            {
                ProviderSettings = GoogleSpeechTestData.Settings(("elevenlabs.stability", Secretive)),
            },
            _ => GoogleSpeechTestData.Speak(Secretive) with { OutputFormat = $"mp3_{Secretive}" },
        };

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(request, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(expected, Case.Insensitive, because);

        // The message names the key and the accepted values, never the text or a value.
        exception.Message.ShouldNotContain(Secretive);
        handler.Requests.ShouldBeEmpty(because);
    }

    [Fact]
    public async Task A_null_request_is_an_argument_error()
    {
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(new GoogleStubHandler(_ => throw new InvalidOperationException()));

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await synthesizer.SynthesizeAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_provider_failure_names_the_status_and_leaks_neither_the_key_nor_the_text()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Error(
            400,
            "INVALID_ARGUMENT",
            $"bad request for {Secretive} with key {GoogleSpeechTestData.SetupKey}"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(Secretive), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("HTTP 400 (INVALID_ARGUMENT)");
        exception.Message.ShouldNotContain(Secretive);
        exception.Message.ShouldNotContain(GoogleSpeechTestData.SetupKey);
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_refused_request_without_a_voice_says_that_some_models_need_one()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Error(400, "INVALID_ARGUMENT", "Model tried to generate text"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(voice: null), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("named no voice");
    }

    [Fact]
    public async Task A_server_error_is_wrapped_without_the_voice_hint()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Error(503, "UNAVAILABLE", "try later"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(voice: null), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("HTTP 503 (UNAVAILABLE)");
        exception.Message.ShouldNotContain("named no voice");
    }

    [Fact]
    public async Task An_answer_with_no_audio_reports_the_finish_reason()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Json(
            """{"candidates":[{"content":{"parts":[{"text":"I cannot say that."}]},"finishReason":"SAFETY"}]}"""));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("answered no audio");
        exception.Message.ShouldContain("SAFETY");
        exception.Message.ShouldNotContain("I cannot say that");
    }

    [Fact]
    public async Task An_answer_with_two_audio_parts_is_refused_not_cut()
    {
        var pcm = GoogleSpeechTestData.Pcm(100);
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Json(GoogleSpeechTestData.AnswerJson(
            [("audio/L16;codec=pcm;rate=24000", pcm), ("audio/L16;codec=pcm;rate=24000", pcm)])));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("2 audio parts");
    }

    [Theory]
    [InlineData("audio/mpeg", "not WAV or 16-bit PCM")]
    [InlineData("audio/L16", "states no sample rate")]
    [InlineData("audio/L16;rate=abc", "states no sample rate")]
    public async Task An_answer_in_a_format_Tracon_cannot_read_is_refused(string mimeType, string expected)
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.Pcm(100), mimeType));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(expected);
    }

    [Fact]
    public async Task A_wav_answer_that_is_not_16_bit_pcm_is_refused()
    {
        var wav = GoogleSpeechTestData.WavWithProvenance(GoogleSpeechTestData.Pcm(100));

        // The bits-per-sample field of the format chunk: offset 34.
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34, 2), 8);

        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(wav, "audio/wav"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("not 16-bit integer PCM");
    }

    [Fact]
    public async Task A_cancelled_call_is_a_cancellation_and_not_a_provider_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new GoogleStubHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), cancellation.Token));
    }

    [Fact]
    public async Task A_request_with_a_tenant_credential_goes_out_on_that_key_and_one_without_on_the_setup_key()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.Pcm(), "audio/L16;codec=pcm;rate=24000"));
        var credentials = new List<ModelProviderCredential>();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler, tenantCredentials: credentials);

        _ = await synthesizer.SynthesizeAsync(
            GoogleSpeechTestData.Speak() with { Credential = new ModelProviderCredential { ApiKey = GoogleSpeechTestData.TenantKey } },
            TestContext.Current.CancellationToken);
        _ = await synthesizer.SynthesizeAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken);

        handler.Requests.Select(static request => request.ApiKey)
            .ShouldBe([GoogleSpeechTestData.TenantKey, GoogleSpeechTestData.SetupKey]);
        credentials.ShouldHaveSingleItem().ApiKey.ShouldBe(GoogleSpeechTestData.TenantKey);
    }

    [Fact]
    public async Task A_stream_yields_the_pcm_of_each_part_and_never_a_header()
    {
        var first = GoogleSpeechTestData.Pcm(100);
        var second = GoogleSpeechTestData.Pcm(50);
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Stream("audio/l16; rate=24000; channels=1", first, second));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var chunks = new List<byte[]>();

        await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk.ToArray());
        }

        chunks.ShouldBe([first, second]);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldEndWith($"/models/{GoogleSpeechTestData.Model}:streamGenerateContent");
        request.Uri.Query.ShouldContain("alt=sse");
    }

    [Fact]
    public async Task A_stream_of_a_model_that_answers_one_part_yields_one_chunk()
    {
        var whole = GoogleSpeechTestData.Pcm(1000);
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Stream("audio/L16;codec=pcm;rate=24000", whole));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var chunks = await Collect(synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak()));

        chunks.ShouldHaveSingleItem().ShouldBe(whole);
    }

    [Theory]
    [InlineData("wav", "streams raw PCM")]
    [InlineData("mp3", "'wav' and 'pcm'")]
    public async Task A_stream_refuses_a_format_it_cannot_produce_before_the_network(string format, string expected)
    {
        var handler = new GoogleStubHandler(_ => throw new InvalidOperationException("the network was reached"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Collect(synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak() with { OutputFormat = format })));

        exception.Message.ShouldContain(expected);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stream_refuses_timestamps_before_the_network()
    {
        var handler = new GoogleStubHandler(_ => throw new InvalidOperationException("the network was reached"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Collect(synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak() with { IncludeTimestamps = true })));

        exception.Message.ShouldContain("IncludeTimestamps");
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stream_that_ends_without_audio_is_a_failure_not_an_empty_result()
    {
        var handler = new GoogleStubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """data: {"candidates":[{"content":{"parts":[{"text":"no"}]},"finishReason":"SAFETY"}]}""" + "\r\n\r\n",
                System.Text.Encoding.UTF8,
                "text/event-stream"),
        });
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Collect(synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak())));

        exception.Message.ShouldContain("answered no audio");
    }

    [Fact]
    public async Task A_stream_failure_is_wrapped_without_the_body()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Error(429, "RESOURCE_EXHAUSTED", $"quota for {Secretive}"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Collect(synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak(Secretive))));

        exception.Message.ShouldContain("HTTP 429 (RESOURCE_EXHAUSTED)");
        exception.Message.ShouldNotContain(Secretive);
    }

    [Fact]
    public async Task A_stream_stopped_by_the_caller_in_the_middle_is_a_cancellation_and_not_a_provider_failure()
    {
        var first = GoogleSpeechTestData.Pcm(100);
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.StreamThatStopsAfterOneChunk(first, breakConnection: false));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var received = new List<byte[]>();

        var consuming = async () =>
        {
            await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak(), cancellation.Token))
            {
                received.Add(chunk.ToArray());
                await cancellation.CancelAsync();
            }
        };

        await Should.ThrowAsync<OperationCanceledException>(async () => await consuming().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        received.ShouldBe([first]);
    }

    [Fact]
    public async Task A_connection_that_breaks_in_the_middle_of_a_stream_is_a_provider_failure_after_the_audio_already_sent()
    {
        var first = GoogleSpeechTestData.Pcm(100);
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.StreamThatStopsAfterOneChunk(first, breakConnection: true));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var received = new List<byte[]>();

        var exception = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(GoogleSpeechTestData.Speak(), TestContext.Current.CancellationToken))
            {
                received.Add(chunk.ToArray());
            }
        });

        received.ShouldBe([first], exception.ToString());
        exception.Message.ShouldContain("could not complete the request");
        exception.InnerException.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_stream_with_a_tenant_credential_goes_out_on_that_key()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Stream("audio/L16;codec=pcm;rate=24000", GoogleSpeechTestData.Pcm(10)));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        _ = await Collect(synthesizer.SynthesizeStreamingAsync(
            GoogleSpeechTestData.Speak() with { Credential = new ModelProviderCredential { ApiKey = GoogleSpeechTestData.TenantKey } }));

        handler.Requests.ShouldHaveSingleItem().ApiKey.ShouldBe(GoogleSpeechTestData.TenantKey);
    }

    private static async Task<List<byte[]>> Collect(IAsyncEnumerable<ReadOnlyMemory<byte>> stream)
    {
        var chunks = new List<byte[]>();

        await foreach (var chunk in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            chunks.Add(chunk.ToArray());
        }

        return chunks;
    }
}
