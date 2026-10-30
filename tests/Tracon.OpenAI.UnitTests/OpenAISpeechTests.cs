using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenAI;
using Tracon.OpenAI.UnitTests.Infrastructure;

namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// <c>UseOpenAISpeech</c>: which providers it registers under which names, and the two rules the
/// OpenAI audio client forces on a bridged request (one model, container format names).
/// </summary>
/// <remarks>
/// The wire (model, voice, input, the transcription form) and the tenant checks are asserted
/// through the real host in <c>SpeechClientBridgeTests</c> (functional).
/// </remarks>
#pragma warning disable MEAI001
public sealed class OpenAISpeechTests
{
    [Fact]
    public async Task Both_models_register_a_synthesizer_and_a_transcriber_under_the_provider_name_openai()
    {
        await using var provider = Build(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.TranscriptionModel = "whisper-1";
        });

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();
        var synthesizer = await resolver.GetSynthesizerAsync("openai", TestContext.Current.CancellationToken);
        var transcriber = await resolver.GetTranscriberAsync("openai", TestContext.Current.CancellationToken);

        // One word covers chat, images and speech in a tenant's egress policy.
        synthesizer.ProviderName.ShouldBe(OpenAIProviderNames.ChatCompletions);
        transcriber.ProviderName.ShouldBe(OpenAIProviderNames.ChatCompletions);
        synthesizer.MaxCharactersPerRequest.ShouldBe(4096);
        synthesizer.SupportsTenantCredentials.ShouldBeFalse();
        transcriber.SupportsTenantCredentials.ShouldBeFalse();
    }

    [Fact]
    public async Task A_model_registers_only_its_own_direction()
    {
        await using var synthesisOnly = Build(static options => options.SynthesisModel = "tts-1");
        await using var transcriptionOnly = Build(static options => options.TranscriptionModel = "whisper-1");

        var first = synthesisOnly.GetRequiredService<ISpeechProviderResolver>();
        var second = transcriptionOnly.GetRequiredService<ISpeechProviderResolver>();

        (await first.GetSynthesizerAsync("openai", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        await Should.ThrowAsync<TraconException>(
            async () => await first.GetTranscriberAsync("openai", TestContext.Current.CancellationToken));

        (await second.GetTranscriberAsync("openai", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        await Should.ThrowAsync<TraconException>(
            async () => await second.GetSynthesizerAsync("openai", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "  ")]
    public void A_registration_with_no_model_is_refused_at_setup(string? synthesis, string? transcription)
    {
        var builder = new ServiceCollection().AddTracon().UseOpenAI(TestData.ApiKey);

        var exception = Should.Throw<ArgumentException>(() => builder.UseOpenAISpeech(options =>
        {
            options.SynthesisModel = synthesis;
            options.TranscriptionModel = transcription;
        }));

        exception.ParamName.ShouldBe("configure");
        exception.Message.ShouldContain(nameof(OpenAISpeechOptions.SynthesisModel));
    }

    [Fact]
    public async Task The_names_are_the_ones_the_options_give()
    {
        await using var provider = Build(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.TranscriptionModel = "whisper-1";
            options.SynthesizerName = "voice-out";
            options.TranscriberName = "voice-in";
        });

        var providers = provider.GetRequiredService<ISpeechProviderResolver>().Providers;

        providers.Select(static p => (p.Name, p.Kind)).ShouldBe(
        [
            ("voice-out", SpeechProviderKind.Synthesizer),
            ("voice-in", SpeechProviderKind.Transcriber),
        ]);
    }

    [Fact]
    public void A_name_registered_twice_is_refused_instead_of_the_last_call_winning()
    {
        var builder = new ServiceCollection().AddTracon().UseOpenAI(TestData.ApiKey);

        builder.UseOpenAISpeech(static options => options.SynthesisModel = "tts-1");

        Should.Throw<InvalidOperationException>(
            () => builder.UseOpenAISpeech(static options => options.SynthesisModel = "tts-1-hd"));

        // A second model has its own name.
        builder.UseOpenAISpeech(static options =>
        {
            options.SynthesisModel = "tts-1-hd";
            options.SynthesizerName = "openai-hd";
        });
    }

    [Fact]
    public void An_invalid_name_or_limit_is_refused_at_setup()
    {
        var builder = new ServiceCollection().AddTracon().UseOpenAI(TestData.ApiKey);

        Should.Throw<ArgumentException>(() => builder.UseOpenAISpeech(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.SynthesizerName = "has space";
        }));
        Should.Throw<ArgumentException>(() => builder.UseOpenAISpeech(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.MaxCharactersPerRequest = 0;
        }));
    }

    [Fact]
    public async Task The_declared_voices_are_listed_and_the_limit_is_the_options()
    {
        await using var provider = Build(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.MaxCharactersPerRequest = 1500;
            options.Voices.Add(new VoiceDescriptor { VoiceId = "nova", Name = "Nova" });
        });

        var synthesizer = await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("openai", TestContext.Current.CancellationToken);

        synthesizer.MaxCharactersPerRequest.ShouldBe(1500);
        (await synthesizer.ListVoicesAsync(TestContext.Current.CancellationToken))
            .Select(static voice => voice.VoiceId).ShouldBe(["nova"]);
    }

    [Fact]
    public void Nothing_is_the_unnamed_default_unless_the_options_ask()
    {
        using var provider = Build(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.TranscriptionModel = "whisper-1";
        });

        provider.GetService<ISpeechSynthesizer>().ShouldBeNull();
        provider.GetService<ISpeechTranscriber>().ShouldBeNull();
    }

    [Fact]
    public async Task The_default_is_the_same_instance_as_the_named_provider_when_the_options_ask()
    {
        await using var provider = Build(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.TranscriptionModel = "whisper-1";
            options.UseAsDefault = true;
        });

        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();

        provider.GetRequiredService<ISpeechSynthesizer>()
            .ShouldBeSameAs(await resolver.GetSynthesizerAsync("openai", TestContext.Current.CancellationToken));
        provider.GetRequiredService<ISpeechTranscriber>()
            .ShouldBeSameAs(await resolver.GetTranscriberAsync("openai", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_default_the_consumer_registered_first_stays()
    {
        var own = new OwnSynthesizer();
        var services = new ServiceCollection();
        services.AddSingleton<ISpeechSynthesizer>(own);
        services.AddTracon().UseOpenAI(TestData.ApiKey).UseOpenAISpeech(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.UseAsDefault = true;
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISpeechSynthesizer>().ShouldBeSameAs(own);
    }

    [Fact]
    public async Task A_default_that_stays_is_named_in_one_warning_at_start()
    {
        // TryAdd keeps the first default, and the loser used to say nothing.
        using var logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<ISpeechSynthesizer>(new OwnSynthesizer());
        services.AddTracon().UseOpenAI(TestData.ApiKey).UseOpenAISpeech(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.TranscriptionModel = "whisper-1";
            options.UseAsDefault = true;
        });
        await using var provider = services.BuildServiceProvider();

        await StartWarningAsync(provider);

        var warnings = logs.Entries
            .Where(static line => line.StartsWith("Warning Tracon.OpenAISpeechDefaultWarning ", StringComparison.Ordinal))
            .ToList();

        // The synthesizer default was taken, the transcriber default was free.
        var warning = warnings.ShouldHaveSingleItem();

        warning.ShouldContain("'openai'");
        warning.ShouldContain("ISpeechSynthesizer");
        warning.ShouldContain("UseOpenAISpeech before");
    }

    [Fact]
    public async Task A_default_that_was_not_asked_for_says_nothing()
    {
        using var logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddSingleton<ISpeechSynthesizer>(new OwnSynthesizer());
        services.AddTracon().UseOpenAI(TestData.ApiKey).UseOpenAISpeech(static options =>
        {
            options.SynthesisModel = "tts-1";
        });
        await using var provider = services.BuildServiceProvider();

        await StartWarningAsync(provider);

        logs.Entries.ShouldNotContain(static line => line.Contains("OpenAISpeechDefaultWarning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_default_that_was_free_is_taken_without_a_warning()
    {
        using var logs = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddTracon().UseOpenAI(TestData.ApiKey).UseOpenAISpeech(static options =>
        {
            options.SynthesisModel = "tts-1";
            options.UseAsDefault = true;
        });
        await using var provider = services.BuildServiceProvider();

        await StartWarningAsync(provider);

        logs.Entries.ShouldNotContain(static line => line.Contains("OpenAISpeechDefaultWarning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_host_without_UseOpenAI_fails_the_provider_and_says_which_call_is_missing()
    {
        var services = new ServiceCollection();
        services.AddTracon().UseOpenAISpeech(static options => options.SynthesisModel = "tts-1");
        await using var provider = services.BuildServiceProvider();

        var exception = await Should.ThrowAsync<TraconException>(async () => await provider
            .GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync("openai", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("UseOpenAI(");
    }

    [Theory]
    [InlineData("mp3_44100_128", "mp3")]
    [InlineData("MP3", "mp3")]
    [InlineData("wav", "wav")]
    [InlineData("pcm_16000", "pcm")]
    [InlineData("opus", "opus")]
    [InlineData(" flac ", "flac")]
    [InlineData("aac", "aac")]
    public void A_format_name_is_read_as_its_container(string written, string expected)
        => OpenAIAudioModels.ContainerName(written).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void No_format_leaves_the_choice_to_the_service(string? written)
        => OpenAIAudioModels.ContainerName(written).ShouldBeNull();

    [Theory]
    [InlineData("ulaw_8000")]
    [InlineData("ogg")]
    [InlineData("_mp3")]
    public void A_name_that_is_not_an_openai_container_is_refused_and_named(string written)
    {
        var exception = Should.Throw<TraconException>(() => OpenAIAudioModels.ContainerName(written));

        exception.Message.ShouldContain($"'{written}'");
        exception.Message.ShouldContain("mp3");
    }

    [Fact]
    public async Task A_synthesis_request_for_another_model_is_refused_before_the_service_is_called()
    {
        var wire = new WireStub();
        using var client = TextToSpeech(wire, "tts-1");

        var exception = await Should.ThrowAsync<TraconException>(async () => await client.GetAudioAsync(
            "hello",
            new TextToSpeechOptions { ModelId = "tts-1-hd" },
            TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("'tts-1'");
        exception.Message.ShouldContain("'tts-1-hd'");
        wire.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tts-1")]
    [InlineData("TTS-1")]
    public async Task A_synthesis_request_for_the_bound_model_or_none_runs_and_reports_the_bound_model(string? asked)
    {
        var wire = new WireStub();
        using var client = TextToSpeech(wire, "tts-1");

        var response = await client.GetAudioAsync("hello", new TextToSpeechOptions { ModelId = asked }, TestContext.Current.CancellationToken);

        response.ModelId.ShouldBe("tts-1");

        var request = wire.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/v1/audio/speech");

        var body = JsonDocument.Parse(request.Body).RootElement;
        body.GetProperty("model").GetString().ShouldBe("tts-1");
        body.GetProperty("input").GetString().ShouldBe("hello");
        body.GetProperty("voice").GetString().ShouldBe("alloy");
        body.TryGetProperty("response_format", out _).ShouldBeFalse();
        body.TryGetProperty("speed", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("mp3_44100_128", "mp3", "audio/mpeg")]
    [InlineData("wav", "wav", "audio/wav")]
    [InlineData("OPUS", "opus", "audio/ogg")]
    [InlineData("pcm_16000", "pcm", "audio/pcm")]
    public async Task A_format_name_is_sent_as_its_container_and_the_audio_carries_that_media_type(
        string asked,
        string container,
        string mediaType)
    {
        var wire = new WireStub();
        using var client = TextToSpeech(wire, "tts-1");

        var response = await client.GetAudioAsync("hello", new TextToSpeechOptions { AudioFormat = asked }, TestContext.Current.CancellationToken);

        JsonDocument.Parse(wire.Requests.ShouldHaveSingleItem().Body).RootElement
            .GetProperty("response_format").GetString().ShouldBe(container);
        response.Contents.OfType<DataContent>().ShouldHaveSingleItem().MediaType.ShouldBe(mediaType);
    }

    [Fact]
    public async Task The_voice_and_the_speed_reach_the_service_and_the_audio_is_what_it_answered()
    {
        var wire = new WireStub { Answer = [9, 8, 7] };
        using var client = TextToSpeech(wire, "tts-1");

        var response = await client.GetAudioAsync(
            "hello",
            new TextToSpeechOptions { VoiceId = "nova", Speed = 1.5f },
            TestContext.Current.CancellationToken);

        var body = JsonDocument.Parse(wire.Requests.ShouldHaveSingleItem().Body).RootElement;
        body.GetProperty("voice").GetString().ShouldBe("nova");
        body.GetProperty("speed").GetSingle().ShouldBe(1.5f);
        response.Contents.OfType<DataContent>().ShouldHaveSingleItem().Data.ToArray().ShouldBe([9, 8, 7]);
    }

    [Fact]
    public async Task A_setting_the_service_does_not_read_is_refused_by_key_and_never_by_value()
    {
        var wire = new WireStub();
        using var client = TextToSpeech(wire, "tts-1");

        var setting = await Should.ThrowAsync<TraconException>(async () => await client.GetAudioAsync(
            "hello",
            new TextToSpeechOptions { AdditionalProperties = new AdditionalPropertiesDictionary { ["studio.mood"] = "value-that-must-not-print" } },
            TestContext.Current.CancellationToken));
        var pitch = await Should.ThrowAsync<TraconException>(async () => await client.GetAudioAsync(
            "hello",
            new TextToSpeechOptions { Pitch = 1.2f },
            TestContext.Current.CancellationToken));

        setting.Message.ShouldContain("'studio.mood'");
        setting.Message.ShouldNotContain("value-that-must-not-print");
        pitch.Message.ShouldContain("pitch");
        wire.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_streaming_call_yields_the_audio_in_chunks_that_report_the_bound_model()
    {
        // 40 000 bytes arrive in more than one chunk: the client reads the response as a stream.
        var audio = Enumerable.Range(0, 40_000).Select(static i => (byte)(i % 251)).ToArray();
        var wire = new WireStub { Answer = audio };
        using var client = TextToSpeech(wire, "tts-1");

        var updates = new List<TextToSpeechResponseUpdate>();

        await foreach (var update in client.GetStreamingAudioAsync("hello", null, TestContext.Current.CancellationToken))
        {
            updates.Add(update);
        }

        updates.Count.ShouldBeGreaterThan(1);
        updates.ShouldAllBe(static update => update.ModelId == "tts-1");
        updates.SelectMany(static update => update.Contents.OfType<DataContent>().SelectMany(static c => c.Data.ToArray()))
            .ShouldBe(audio);

        var request = wire.Requests.ShouldHaveSingleItem();
        JsonDocument.Parse(request.Body).RootElement.TryGetProperty("stream_format", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task A_streaming_call_refused_by_the_service_names_the_status_before_any_chunk()
    {
        var wire = new WireStub
        {
            Status = HttpStatusCode.TooManyRequests,
            AnswerType = "application/json",
            Answer = """{"error":{"message":"slow down sk-live-fragment","code":"rate_limit_exceeded"}}"""u8.ToArray(),
        };
        using var client = TextToSpeech(wire, "tts-1");

        var exception = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var update in client.GetStreamingAudioAsync("hello", null, TestContext.Current.CancellationToken))
            {
                update.Contents.ShouldNotBeEmpty();
            }
        });

        exception.Message.ShouldContain("HTTP 429 (rate_limit_exceeded)");
        exception.Message.ShouldNotContain("sk-live-fragment");
    }

    [Fact]
    public async Task A_streaming_call_cancelled_mid_flight_ends_with_the_cancellation()
    {
        var wire = new WireStub { Hang = true };
        using var client = TextToSpeech(wire, "tts-1");
        using var cancellation = new CancellationTokenSource();

        var call = Task.Run(async () =>
        {
            await foreach (var update in client.GetStreamingAudioAsync("hello", null, cancellation.Token))
            {
                update.Contents.ShouldNotBeEmpty();
            }
        });

        await wire.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await call);
    }

    [Theory]
    [InlineData("""{"error":{"message":"Incorrect API key provided: sk-live-fragment","code":"invalid_api_key"}}""", "HTTP 401 (invalid_api_key)")]
    [InlineData("""{"error":{"message":"quota","code":null}}""", "HTTP 401.")]
    [InlineData("""{"error":{"code":"Has Space And Caps"}}""", "HTTP 401.")]
    [InlineData("not json at all, sk-live-fragment", "HTTP 401.")]
    public async Task A_service_error_names_the_status_and_the_code_and_never_the_service_text(string answer, string expected)
    {
        var wire = new WireStub
        {
            Status = HttpStatusCode.Unauthorized,
            AnswerType = "application/json",
            Answer = Encoding.UTF8.GetBytes(answer),
        };
        using var client = TextToSpeech(wire, "tts-1");

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await client.GetAudioAsync("hello", null, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(expected);
        exception.Message.ShouldNotContain("sk-live-fragment");
        exception.InnerException.ShouldBeOfType<ClientResultException>();
        wire.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_synthesis_call()
    {
        var wire = new WireStub();
        using var client = TextToSpeech(wire, "tts-1");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await client.GetAudioAsync("hello", null, cancellation.Token));
    }

    [Fact]
    public async Task A_call_cancelled_while_the_service_is_answering_ends_with_the_cancellation()
    {
        var wire = new WireStub { Hang = true };
        using var client = TextToSpeech(wire, "tts-1");
        using var cancellation = new CancellationTokenSource();

        var call = client.GetAudioAsync("hello", null, cancellation.Token);

        await wire.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await call);
    }

    [Fact]
    public void The_text_to_speech_client_names_the_provider_in_its_metadata()
    {
        using var client = TextToSpeech(new WireStub(), "tts-1");

        var metadata = client.GetService<TextToSpeechClientMetadata>().ShouldNotBeNull();

        metadata.ProviderName.ShouldBe("openai");
        metadata.DefaultModelId.ShouldBe("tts-1");
        client.GetService(typeof(TextToSpeechClientMetadata), "a-key").ShouldBeNull();
    }

    [Fact]
    public async Task A_transcription_request_for_another_model_is_refused_and_the_bound_model_is_reported()
    {
        var wire = new WireStub { AnswerType = "application/json", Answer = """{"text":"hello"}"""u8.ToArray() };
        using var client = SpeechToText(wire, "whisper-1");

        await Should.ThrowAsync<TraconException>(async () => await client.GetTextAsync(
            new MemoryStream([1]),
            new SpeechToTextOptions { ModelId = "whisper-2" },
            TestContext.Current.CancellationToken));
        wire.Requests.ShouldBeEmpty();

        var response = await client.GetTextAsync(
            new MemoryStream([1]),
            new SpeechToTextOptions { ModelId = "whisper-1", SpeechLanguage = "tr" },
            TestContext.Current.CancellationToken);

        response.Text.ShouldBe("hello");
        response.ModelId.ShouldBe("whisper-1");

        var request = wire.Requests.ShouldHaveSingleItem();
        request.Path.ShouldBe("/v1/audio/transcriptions");
        request.ContentType.ShouldStartWith("multipart/form-data");

        var form = Encoding.Latin1.GetString(request.Body);
        form.ShouldContain("name=model");
        form.ShouldContain("whisper-1");
        form.ShouldContain("name=language");
        form.ShouldContain("tr");
    }

    [Theory]
    [InlineData("RIFF\0\0\0\0WAVEfmt ", "audio.wav")]
    [InlineData("OggS\0\0\0\0\0\0\0\0\0\0\0\0", "audio.ogg")]
    [InlineData("fLaC\0\0\0\0\0\0\0\0\0\0\0\0", "audio.flac")]
    [InlineData("\0\0\0\u0018ftypM4A \0\0\0\0", "audio.m4a")]
    [InlineData("ID3\u0004\0\0\0\0\0\0\0\0\0\0\0\0", "audio.mp3")]
    [InlineData("????????????????", "audio.mp3")]
    public async Task The_file_name_follows_the_bytes_because_the_service_reads_the_container_from_it(string head, string fileName)
    {
        var wire = new WireStub { AnswerType = "application/json", Answer = """{"text":"x"}"""u8.ToArray() };
        using var client = SpeechToText(wire, "whisper-1");

        await client.GetTextAsync(new MemoryStream(Encoding.Latin1.GetBytes(head)), null, TestContext.Current.CancellationToken);

        Encoding.Latin1.GetString(wire.Requests.ShouldHaveSingleItem().Body).ShouldContain($"filename={fileName}");
    }

    [Fact]
    public async Task A_transcription_answer_without_text_is_an_empty_transcript()
    {
        var wire = new WireStub { AnswerType = "application/json", Answer = "{}"u8.ToArray() };
        using var client = SpeechToText(wire, "whisper-1");

        var response = await client.GetTextAsync(new MemoryStream([1]), null, TestContext.Current.CancellationToken);

        response.Text.ShouldBeEmpty();
    }

    [Fact]
    public void The_speech_to_text_client_offers_no_streaming_and_names_the_provider_in_its_metadata()
    {
        using var client = SpeechToText(new WireStub(), "whisper-1");

        Should.Throw<NotSupportedException>(() => client.GetStreamingTextAsync(new MemoryStream([1])));
        client.GetService<SpeechToTextClientMetadata>().ShouldNotBeNull().ProviderName.ShouldBe("openai");
    }

    [Theory]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, "audio.wav")]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 1, 2, 3, 4 }, "audio.webm")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46 }, "audio.mp3")]
    [InlineData(new byte[0], "audio.mp3")]
    public void A_short_or_unrecognised_head_is_named_without_reading_past_it(byte[] head, string expected)
        => OpenAIAudioModels.FileNameFor(head).ShouldBe(expected);

    private static OpenAITextToSpeechClient TextToSpeech(WireStub wire, string model)
        => new(Transport(wire), model);

    private static OpenAISpeechToTextClient SpeechToText(WireStub wire, string model)
        => new(Transport(wire), model);

    private static OpenAIAudioTransport Transport(WireStub wire)
    {
        var endpoint = new Uri("http://stub.invalid/v1");
        var client = new OpenAIClient(
            new ApiKeyCredential(TestData.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = endpoint,
                Transport = new HttpClientPipelineTransport(new HttpClient(wire)),
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            });

        return new OpenAIAudioTransport(client, endpoint);
    }

    /// <summary>An HTTP handler that records each request and answers one canned body. No network.</summary>
    private sealed class WireStub : HttpMessageHandler
    {
        public List<(string Path, string ContentType, byte[] Body)> Requests { get; } = [];

        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;

        public string AnswerType { get; init; } = "audio/mpeg";

        public byte[] Answer { get; init; } = [0x49, 0x44, 0x33];

        /// <summary>Gets whether the stub holds the answer until the call is cancelled.</summary>
        public bool Hang { get; init; }

        /// <summary>Gets the signal that a request reached the stub.</summary>
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);

            Requests.Add((request.RequestUri!.AbsolutePath, request.Content?.Headers.ContentType?.ToString() ?? string.Empty, body));
            Arrived.TrySetResult();

            if (Hang)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken); // delay: simulated
            }

            var response = new HttpResponseMessage(Status) { Content = new ByteArrayContent(Answer) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(AnswerType);

            return response;
        }
    }

    // Only the service under test: the other hosted services of a Tracon host are not what these cases probe.
    private static async Task StartWarningAsync(IServiceProvider provider)
    {
        foreach (var hosted in provider.GetServices<IHostedService>().OfType<OpenAISpeechDefaultWarning>())
        {
            await hosted.StartAsync(TestContext.Current.CancellationToken);
        }
    }

    private static ServiceProvider Build(Action<OpenAISpeechOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddTracon().UseOpenAI(TestData.ApiKey).UseOpenAISpeech(configure);

        return services.BuildServiceProvider();
    }

    private sealed class OwnSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "own";

        public int MaxCharactersPerRequest => 10;

        public ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
