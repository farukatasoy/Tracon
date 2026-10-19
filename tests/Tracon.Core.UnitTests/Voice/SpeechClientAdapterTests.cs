using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// A Microsoft.Extensions.AI speech client served as a Tracon synthesizer or transcriber:
/// what reaches the client, what comes back, and what the adapter refuses.
/// </summary>
/// <remarks>
/// The cases that cross the tenant, HTTP and host-startup boundaries live in
/// <c>SpeechClientBridgeTests</c> (functional); these cases are the pure mapping.
/// </remarks>
#pragma warning disable MEAI001
public sealed class SpeechClientAdapterTests
{
    private const string Studio = "studio";
    private static readonly byte[] FirstChunk = [0x49, 0x44, 0x33, 0x04];
    private static readonly byte[] SecondChunk = [0x01, 0x02, 0x03];

    [Fact]
    public async Task A_request_reaches_the_client_as_the_options_it_names()
    {
        var client = new FakeTextToSpeechClient();
        await using var provider = Build(client);

        var synthesizer = await Synthesizer(provider);

        await synthesizer.SynthesizeAsync(
            new SpeechRequest
            {
                Text = "hello",
                VoiceId = "nova",
                ModelId = "tts-hd",
                OutputFormat = "wav",
                LanguageCode = "tr",
                ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                {
                    ["studio.mood"] = JsonSerializer.SerializeToElement("calm"),
                },
            },
            TestContext.Current.CancellationToken);

        var (text, options) = client.Calls.ShouldHaveSingleItem();
        text.ShouldBe("hello");
        options.ShouldNotBeNull();
        options.VoiceId.ShouldBe("nova");
        options.ModelId.ShouldBe("tts-hd");
        options.AudioFormat.ShouldBe("wav");
        options.Language.ShouldBe("tr");
        ((JsonElement)options.AdditionalProperties!["studio.mood"]!).GetString().ShouldBe("calm");
    }

    [Fact]
    public async Task A_request_that_names_nothing_leaves_every_choice_to_the_client()
    {
        var client = new FakeTextToSpeechClient();
        await using var provider = Build(client);

        await (await Synthesizer(provider)).SynthesizeAsync(
            new SpeechRequest { Text = "hello", VoiceId = " ", OutputFormat = string.Empty },
            TestContext.Current.CancellationToken);

        var options = client.Calls.ShouldHaveSingleItem().Options.ShouldNotBeNull();
        options.VoiceId.ShouldBeNull();
        options.ModelId.ShouldBeNull();
        options.AudioFormat.ShouldBeNull();
        options.Language.ShouldBeNull();
        options.AdditionalProperties.ShouldBeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_tenant_credential_never_reaches_the_client_options(bool withProviderSettings)
    {
        // LoggingTextToSpeechClient writes the options to the log at Trace level: a key
        // placed in any of them leaves the process (K-059).
        const string Key = "tenant-voice-value";

        var client = new FakeTextToSpeechClient();
        await using var provider = Build(client);

        await (await Synthesizer(provider)).SynthesizeAsync(
            new SpeechRequest
            {
                Text = "hello",
                Credential = new ModelProviderCredential { ApiKey = Key },
                ProviderSettings = withProviderSettings
                    ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["studio.mood"] = JsonSerializer.SerializeToElement("calm"),
                    }
                    : new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            },
            TestContext.Current.CancellationToken);

        var options = client.Calls.ShouldHaveSingleItem().Options.ShouldNotBeNull();
        options.RawRepresentationFactory.ShouldBeNull();
        options.VoiceId.ShouldBeNull();
        options.ModelId.ShouldBeNull();
        options.AudioFormat.ShouldBeNull();
        options.Language.ShouldBeNull();

        if (withProviderSettings)
        {
            options.AdditionalProperties!.Keys.ShouldBe(["studio.mood"]);
        }
        else
        {
            options.AdditionalProperties.ShouldBeNull();
        }

        JsonSerializer.Serialize(options.AdditionalProperties).ShouldNotContain(Key);
    }

    [Fact]
    public async Task A_transcription_never_hands_the_tenant_credential_to_the_client()
    {
        const string Key = "tenant-voice-value";

        var client = new FakeSpeechToTextClient();
        await using var provider = Build(speechToText: client);

        await (await Transcriber(provider)).TranscribeAsync(
            new MemoryStream([1, 2]),
            "audio/wav",
            new SpeechTranscriptionOptions { Credential = new ModelProviderCredential { ApiKey = Key } },
            TestContext.Current.CancellationToken);

        var options = client.Calls.ShouldHaveSingleItem().Options.ShouldNotBeNull();
        options.RawRepresentationFactory.ShouldBeNull();
        options.AdditionalProperties.ShouldBeNull();
    }

    [Fact]
    public async Task The_audio_is_the_client_bytes_with_an_estimated_character_count()
    {
        var client = new FakeTextToSpeechClient
        {
            Respond = static _ => new TextToSpeechResponse([new DataContent(FirstChunk, "audio/mpeg")])
            {
                ModelId = "tts-hd",
                ResponseId = "resp-9",
            },
        };
        await using var provider = Build(client);

        var audio = await (await Synthesizer(provider)).SynthesizeAsync(
            new SpeechRequest { Text = "hello" },
            TestContext.Current.CancellationToken);

        audio.Data.ToArray().ShouldBe(FirstChunk);
        audio.MediaType.ShouldBe("audio/mpeg");
        audio.ModelId.ShouldBe("tts-hd");
        audio.ProviderRequestId.ShouldBe("resp-9");
        audio.CharactersBilled.ShouldBe(5);
        audio.UsageSource.ShouldBe(SpeechUsageSource.Estimated);
        audio.Duration.ShouldBeNull();
        audio.Alignment.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "tts-asked", "tts-asked")]
    [InlineData(null, null, "tts-default")]
    [InlineData("tts-reported", "tts-asked", "tts-reported")]
    public async Task The_model_is_the_one_the_client_reports_then_the_one_asked_then_the_client_default(
        string? reported,
        string? asked,
        string expected)
    {
        var client = new FakeTextToSpeechClient
        {
            Metadata = new TextToSpeechClientMetadata(Studio, null, "tts-default"),
            Respond = _ => new TextToSpeechResponse([new DataContent(FirstChunk, "audio/mpeg")]) { ModelId = reported },
        };
        await using var provider = Build(client);

        var audio = await (await Synthesizer(provider)).SynthesizeAsync(
            new SpeechRequest { Text = "hello", ModelId = asked },
            TestContext.Current.CancellationToken);

        audio.ModelId.ShouldBe(expected);
    }

    [Fact]
    public async Task Several_audio_contents_are_one_audio_in_order()
    {
        var client = new FakeTextToSpeechClient
        {
            Respond = static _ => new TextToSpeechResponse(
            [
                new TextContent("not audio"),
                new DataContent(FirstChunk, "audio/mpeg"),
                new DataContent(SecondChunk, "audio/ogg"),
            ]),
        };
        await using var provider = Build(client);

        var audio = await (await Synthesizer(provider)).SynthesizeAsync(
            new SpeechRequest { Text = "hello" },
            TestContext.Current.CancellationToken);

        audio.Data.ToArray().ShouldBe([.. FirstChunk, .. SecondChunk]);
        audio.MediaType.ShouldBe("audio/mpeg");
    }

    [Fact]
    public async Task A_response_with_no_audio_fails_the_call()
    {
        var client = new FakeTextToSpeechClient { Respond = static _ => new TextToSpeechResponse([new TextContent("sorry")]) };
        await using var provider = Build(client);

        var exception = await Should.ThrowAsync<TraconException>(async () => await (await Synthesizer(provider))
            .SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("returned no audio");
    }

    [Fact]
    public async Task The_streamed_bytes_are_the_bytes_of_the_single_response()
    {
        var client = new FakeTextToSpeechClient();
        await using var provider = Build(client);
        var synthesizer = await Synthesizer(provider);

        var request = new SpeechRequest { Text = "hello", VoiceId = "nova" };
        var whole = await synthesizer.SynthesizeAsync(request, TestContext.Current.CancellationToken);
        var streamed = new List<byte>();

        await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(request, TestContext.Current.CancellationToken))
        {
            streamed.AddRange(chunk.ToArray());
        }

        streamed.ShouldBe(whole.Data.ToArray());
        client.Calls.Count.ShouldBe(2);
        client.Calls[1].Options!.VoiceId.ShouldBe("nova");
    }

    [Fact]
    public async Task A_request_for_character_timing_is_refused_on_both_paths_and_the_client_is_not_called()
    {
        var client = new FakeTextToSpeechClient();
        await using var provider = Build(client);
        var synthesizer = await Synthesizer(provider);
        var request = new SpeechRequest { Text = "hello", IncludeTimestamps = true };

        var whole = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(request, TestContext.Current.CancellationToken));
        var streamed = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(request, TestContext.Current.CancellationToken))
            {
                chunk.Length.ShouldBeGreaterThan(0);
            }
        });

        whole.Message.ShouldContain(nameof(SpeechRequest.IncludeTimestamps));
        streamed.Message.ShouldContain(nameof(SpeechRequest.IncludeTimestamps));
        client.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_client_failure_becomes_a_tracon_exception_without_the_client_message()
    {
        var failure = new HttpRequestException("body fragment sk-leaked", null, HttpStatusCode.InternalServerError);
        var client = new FakeTextToSpeechClient { Failure = failure };
        await using var provider = Build(client);
        var synthesizer = await Synthesizer(provider);

        var whole = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken));
        var streamed = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(
                               new SpeechRequest { Text = "hello" },
                               TestContext.Current.CancellationToken))
            {
                chunk.Length.ShouldBeGreaterThan(0);
            }
        });

        foreach (var exception in new[] { whole, streamed })
        {
            exception.Message.ShouldContain($"'{Studio}'");
            exception.Message.ShouldContain("HTTP 500");
            exception.Message.ShouldNotContain("sk-leaked");
            exception.InnerException.ShouldBeSameAs(failure);
        }
    }

    [Fact]
    public async Task A_client_that_throws_before_the_first_step_of_a_stream_is_wrapped_too()
    {
        var failure = new InvalidOperationException("synchronous failure sk-leaked");
        var client = new FakeTextToSpeechClient { ThrowOnStreamStart = failure };
        await using var provider = Build(client);

        var exception = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var chunk in (await Synthesizer(provider)).SynthesizeStreamingAsync(
                               new SpeechRequest { Text = "hello" },
                               TestContext.Current.CancellationToken))
            {
                chunk.Length.ShouldBeGreaterThan(0);
            }
        });

        exception.Message.ShouldNotContain("sk-leaked");
        exception.InnerException.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task A_timeout_while_the_callers_token_is_alive_is_a_provider_failure_and_not_a_cancellation()
    {
        var client = new FakeTextToSpeechClient { Failure = new TaskCanceledException("the client's own timeout") };
        await using var provider = Build(client);

        var exception = await Should.ThrowAsync<TraconException>(async () => await (await Synthesizer(provider))
            .SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken));

        exception.InnerException.ShouldBeOfType<TaskCanceledException>();
    }

    [Fact]
    public async Task A_tracon_exception_from_the_client_is_not_wrapped()
    {
        var refusal = new TraconException("the client's own refusal");
        var client = new FakeTextToSpeechClient { Failure = refusal };
        await using var provider = Build(client);

        var thrown = await Should.ThrowAsync<TraconException>(async () => await (await Synthesizer(provider))
            .SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(refusal);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_call_with_the_cancellation_and_not_a_provider_failure()
    {
        var client = new FakeTextToSpeechClient();
        await using var provider = Build(client);
        var synthesizer = await Synthesizer(provider);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await synthesizer.SynthesizeAsync(new SpeechRequest { Text = "hello" }, cancellation.Token));
        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(new SpeechRequest { Text = "hello" }, cancellation.Token))
            {
                chunk.Length.ShouldBeGreaterThan(0);
            }
        });
    }

    [Fact]
    public async Task The_voices_are_the_ones_the_binding_declares_and_none_by_default()
    {
        var voices = new[] { new VoiceDescriptor { VoiceId = "nova", Name = "Nova" } };

        await using var declared = Build(new FakeTextToSpeechClient(), voices: voices);
        await using var undeclared = Build(new FakeTextToSpeechClient());

        (await (await Synthesizer(declared)).ListVoicesAsync(TestContext.Current.CancellationToken))
            .Select(static voice => voice.VoiceId).ShouldBe(["nova"]);
        (await (await Synthesizer(undeclared)).ListVoicesAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_limit_and_the_credential_support_are_the_bindings()
    {
        await using var provider = Build(new FakeTextToSpeechClient(), maxCharacters: 123);

        var synthesizer = await Synthesizer(provider);

        synthesizer.MaxCharactersPerRequest.ShouldBe(123);
        synthesizer.SupportsTenantCredentials.ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_limit_that_is_not_positive_is_refused_at_setup(int maxCharacters)
    {
        var builder = new ServiceCollection().AddTracon();

        var exception = Should.Throw<ArgumentException>(() => builder.AddSpeechSynthesizer(
            Studio,
            new TextToSpeechClientBinding { ProviderName = Studio, MaxCharactersPerRequest = maxCharacters }));

        exception.Message.ShouldContain(nameof(TextToSpeechClientBinding.MaxCharactersPerRequest));
    }

    [Fact]
    public void A_null_binding_is_refused_and_a_repeated_name_still_is()
    {
        var builder = new ServiceCollection().AddTracon();

        Should.Throw<ArgumentNullException>(() => builder.AddSpeechSynthesizer(Studio, (TextToSpeechClientBinding)null!));
        Should.Throw<ArgumentNullException>(() => builder.AddSpeechTranscriber(Studio, (SpeechToTextClientBinding)null!));

        builder.AddSpeechSynthesizer(Studio, new TextToSpeechClientBinding { ProviderName = Studio, MaxCharactersPerRequest = 10 });

        Should.Throw<InvalidOperationException>(() => builder.AddSpeechSynthesizer(
            "STUDIO",
            new TextToSpeechClientBinding { ProviderName = Studio, MaxCharactersPerRequest = 10 }));
    }

    [Fact]
    public async Task The_provider_name_on_the_binding_wins_over_the_client_metadata()
    {
        var client = new FakeTextToSpeechClient { Metadata = new TextToSpeechClientMetadata("vendor-name", null, null) };
        await using var provider = Build(client, providerName: "our-name");

        (await Synthesizer(provider)).ProviderName.ShouldBe("our-name");
    }

    [Fact]
    public async Task The_provider_name_falls_back_to_the_client_metadata()
    {
        var client = new FakeTextToSpeechClient { Metadata = new TextToSpeechClientMetadata("vendor-name", null, null) };
        await using var provider = Build(client, providerName: null);

        (await Synthesizer(provider)).ProviderName.ShouldBe("vendor-name");
    }

    [Fact]
    public async Task A_client_with_no_provider_name_fails_the_build_of_the_adapter_and_names_the_registration()
    {
        // K-930: the tenant's egress policy is applied to this name. No name, no policy.
        await using var provider = Build(new FakeTextToSpeechClient(), providerName: null);

        var exception = await Should.ThrowAsync<TraconException>(async () => await Synthesizer(provider));

        exception.Message.ShouldContain($"'{Studio}'");
        exception.Message.ShouldContain("no provider name");
    }

    [Fact]
    public async Task A_missing_client_fails_the_build_of_the_adapter_and_names_the_registration_and_the_key()
    {
        var services = new ServiceCollection();
        services.AddTracon().AddSpeechSynthesizer(
            Studio,
            new TextToSpeechClientBinding { ServiceKey = "gpu-box", ProviderName = Studio, MaxCharactersPerRequest = 100 });
        await using var provider = services.BuildServiceProvider();

        var exception = await Should.ThrowAsync<TraconException>(async () => await Synthesizer(provider));

        exception.Message.ShouldContain($"'{Studio}'");
        exception.Message.ShouldContain("'gpu-box'");
    }

    [Fact]
    public async Task A_keyed_client_is_taken_by_its_key_and_the_unkeyed_one_is_left_alone()
    {
        var keyed = new FakeTextToSpeechClient();
        var unkeyed = new FakeTextToSpeechClient();
        var services = new ServiceCollection();
        services.AddSingleton<ITextToSpeechClient>(unkeyed);
        services.AddKeyedSingleton<ITextToSpeechClient>("gpu-box", keyed);
        services.AddTracon().AddSpeechSynthesizer(
            Studio,
            new TextToSpeechClientBinding { ServiceKey = "gpu-box", ProviderName = Studio, MaxCharactersPerRequest = 100 });
        await using var provider = services.BuildServiceProvider();

        await (await Synthesizer(provider)).SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken);

        keyed.Calls.Count.ShouldBe(1);
        unkeyed.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dialogue_for_a_bridged_name_is_reported_and_does_not_fall_back_to_another_engine()
    {
        await using var provider = Build(new FakeTextToSpeechClient());
        var resolver = provider.GetRequiredService<ISpeechProviderResolver>();

        var exception = await Should.ThrowAsync<SpeechCapabilityNotSupportedException>(
            async () => await resolver.GetDialogueSynthesizerAsync(Studio, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"'{Studio}'");
    }

    [Fact]
    public async Task A_transcription_reaches_the_client_as_the_options_it_names()
    {
        var client = new FakeSpeechToTextClient();
        await using var provider = Build(speechToText: client);

        var audio = new MemoryStream([1, 2, 3]);
        await (await Transcriber(provider)).TranscribeAsync(
            audio,
            "audio/wav",
            new SpeechTranscriptionOptions { ModelId = "stt-hd", LanguageCode = "tr" },
            TestContext.Current.CancellationToken);

        var (stream, options) = client.Calls.ShouldHaveSingleItem();
        stream.ShouldBeSameAs(audio);
        options.ShouldNotBeNull();
        options.ModelId.ShouldBe("stt-hd");
        options.SpeechLanguage.ShouldBe("tr");
    }

    [Fact]
    public async Task A_transcription_with_no_options_leaves_every_choice_to_the_client()
    {
        var client = new FakeSpeechToTextClient();
        await using var provider = Build(speechToText: client);

        await (await Transcriber(provider)).TranscribeAsync(new MemoryStream([1]), "audio/wav", null, TestContext.Current.CancellationToken);

        var options = client.Calls.ShouldHaveSingleItem().Options.ShouldNotBeNull();
        options.ModelId.ShouldBeNull();
        options.SpeechLanguage.ShouldBeNull();
    }

    [Fact]
    public async Task The_transcript_carries_the_text_and_the_span_between_the_start_and_the_end()
    {
        var client = new FakeSpeechToTextClient
        {
            Respond = static () => new SpeechToTextResponse("where is my order")
            {
                StartTime = TimeSpan.FromSeconds(1),
                EndTime = TimeSpan.FromSeconds(4.5),
                ModelId = "stt-reported",
            },
        };
        await using var provider = Build(speechToText: client);

        var transcript = await (await Transcriber(provider)).TranscribeAsync(
            new MemoryStream([1]),
            "audio/wav",
            null,
            TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("where is my order");
        transcript.AudioDuration.ShouldBe(TimeSpan.FromSeconds(3.5));
        transcript.ModelId.ShouldBe("stt-reported");
    }

    [Theory]
    [InlineData(true, false, 1, 2)]
    [InlineData(false, true, 1, 2)]
    [InlineData(false, false, 1, 2)]
    [InlineData(true, true, 5, 2)]
    [InlineData(true, true, 3, 3)]
    public async Task A_transcript_with_one_end_of_the_span_or_none_or_a_backwards_span_has_no_duration(
        bool hasStart,
        bool hasEnd,
        int startSeconds,
        int endSeconds)
    {
        var client = new FakeSpeechToTextClient
        {
            Respond = () => new SpeechToTextResponse("text")
            {
                StartTime = hasStart ? TimeSpan.FromSeconds(startSeconds) : null,
                EndTime = hasEnd ? TimeSpan.FromSeconds(endSeconds) : null,
            },
        };
        await using var provider = Build(speechToText: client);

        var transcript = await (await Transcriber(provider)).TranscribeAsync(
            new MemoryStream([1]),
            "audio/wav",
            null,
            TestContext.Current.CancellationToken);

        transcript.AudioDuration.ShouldBeNull();
    }

    [Fact]
    public async Task The_transcript_model_falls_back_to_the_one_asked_then_the_client_default()
    {
        var client = new FakeSpeechToTextClient { Metadata = new SpeechToTextClientMetadata(Studio, null, "stt-default") };
        await using var provider = Build(speechToText: client);
        var transcriber = await Transcriber(provider);

        var asked = await transcriber.TranscribeAsync(
            new MemoryStream([1]),
            "audio/wav",
            new SpeechTranscriptionOptions { ModelId = "stt-asked" },
            TestContext.Current.CancellationToken);
        var unasked = await transcriber.TranscribeAsync(new MemoryStream([1]), "audio/wav", null, TestContext.Current.CancellationToken);

        asked.ModelId.ShouldBe("stt-asked");
        unasked.ModelId.ShouldBe("stt-default");
    }

    [Fact]
    public async Task A_transcription_failure_becomes_a_tracon_exception_and_a_cancelled_token_does_not()
    {
        var failure = new InvalidOperationException("body fragment sk-leaked");
        var client = new FakeSpeechToTextClient { Failure = failure };
        await using var provider = Build(speechToText: client);
        var transcriber = await Transcriber(provider);

        var exception = await Should.ThrowAsync<TraconException>(async () => await transcriber.TranscribeAsync(
            new MemoryStream([1]),
            "audio/wav",
            null,
            TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"'{Studio}'");
        exception.Message.ShouldNotContain("sk-leaked");
        exception.InnerException.ShouldBeSameAs(failure);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        client.Failure = null;

        await Should.ThrowAsync<OperationCanceledException>(async () => await transcriber.TranscribeAsync(
            new MemoryStream([1]),
            "audio/wav",
            null,
            cancellation.Token));
    }

    [Fact]
    public async Task The_transcriber_name_is_checked_like_the_synthesizer_name()
    {
        await using var provider = Build(speechToText: new FakeSpeechToTextClient(), providerName: null);

        var exception = await Should.ThrowAsync<TraconException>(async () => await Transcriber(provider));

        exception.Message.ShouldContain($"'{Studio}'");
        exception.Message.ShouldContain("no provider name");
    }

    [Fact]
    public void The_startup_check_names_every_bridged_provider_that_cannot_be_built()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddSpeechSynthesizer("no-client", new TextToSpeechClientBinding { ProviderName = "p", MaxCharactersPerRequest = 10 })
            .AddSpeechTranscriber("no-name", new SpeechToTextClientBinding());
        services.AddSingleton<ISpeechToTextClient>(new FakeSpeechToTextClient());
        using var provider = services.BuildServiceProvider();

        var exception = Should.Throw<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<SpeechClientBridgeOptions>>().Value);

        exception.Failures.Count().ShouldBe(2);
        exception.Message.ShouldContain("'no-client'");
        exception.Message.ShouldContain("'no-name'");
    }

    [Fact]
    public void The_startup_check_passes_a_complete_registration_and_does_not_build_a_provider_it_did_not_bridge()
    {
        var ownFactoryRuns = 0;
        var services = new ServiceCollection();
        services.AddSingleton<ITextToSpeechClient>(new FakeTextToSpeechClient());
        services.AddTracon()
            .AddSpeechSynthesizer(Studio, new TextToSpeechClientBinding { ProviderName = Studio, MaxCharactersPerRequest = 10 })
            .AddSpeechSynthesizer("own", _ =>
            {
                ownFactoryRuns++;

                throw new InvalidOperationException("a consumer factory keeps its lazy creation");
            });
        using var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IOptions<SpeechClientBridgeOptions>>().Value;

        ownFactoryRuns.ShouldBe(0);
    }

    private static async ValueTask<ISpeechSynthesizer> Synthesizer(ServiceProvider provider)
        => await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetSynthesizerAsync(Studio, TestContext.Current.CancellationToken);

    private static async ValueTask<ISpeechTranscriber> Transcriber(ServiceProvider provider)
        => await provider.GetRequiredService<ISpeechProviderResolver>()
            .GetTranscriberAsync(Studio, TestContext.Current.CancellationToken);

    private static ServiceProvider Build(
        FakeTextToSpeechClient? textToSpeech = null,
        FakeSpeechToTextClient? speechToText = null,
        string? providerName = Studio,
        int maxCharacters = 100,
        IReadOnlyList<VoiceDescriptor>? voices = null)
    {
        var services = new ServiceCollection();
        var builder = services.AddTracon();

        if (textToSpeech is not null)
        {
            services.AddSingleton<ITextToSpeechClient>(textToSpeech);
            builder.AddSpeechSynthesizer(
                Studio,
                new TextToSpeechClientBinding
                {
                    ProviderName = providerName,
                    MaxCharactersPerRequest = maxCharacters,
                    Voices = voices ?? [],
                });
        }

        if (speechToText is not null)
        {
            services.AddSingleton<ISpeechToTextClient>(speechToText);
            builder.AddSpeechTranscriber(Studio, new SpeechToTextClientBinding { ProviderName = providerName });
        }

        return services.BuildServiceProvider();
    }

    private sealed class FakeTextToSpeechClient : ITextToSpeechClient
    {
        public List<(string Text, TextToSpeechOptions? Options)> Calls { get; } = [];

        public TextToSpeechClientMetadata? Metadata { get; init; }

        public Exception? Failure { get; init; }

        /// <summary>Gets an exception the client throws when the stream is created, before the first step.</summary>
        public Exception? ThrowOnStreamStart { get; init; }

        public Func<string, TextToSpeechResponse> Respond { get; init; } = static _ => new TextToSpeechResponse(
        [
            new DataContent(FirstChunk, "audio/mpeg"),
            new DataContent(SecondChunk, "audio/mpeg"),
        ]);

        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((text, options));

            return Failure is null ? Task.FromResult(Respond(text)) : Task.FromException<TextToSpeechResponse>(Failure);
        }

        public IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowOnStreamStart is not null)
            {
                throw ThrowOnStreamStart;
            }

            return Stream(text, options, cancellationToken);
        }

        private async IAsyncEnumerable<TextToSpeechResponseUpdate> Stream(
            string text,
            TextToSpeechOptions? options,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((text, options));

            await Task.Yield();

            if (Failure is not null)
            {
                throw Failure;
            }

            foreach (var content in Respond(text).Contents)
            {
                yield return new TextToSpeechResponseUpdate([content]);
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(TextToSpeechClientMetadata) ? Metadata : null;

        public void Dispose()
        {
        }
    }

    private sealed class FakeSpeechToTextClient : ISpeechToTextClient
    {
        public List<(Stream Audio, SpeechToTextOptions? Options)> Calls { get; } = [];

        public SpeechToTextClientMetadata? Metadata { get; init; }

        public Exception? Failure { get; set; }

        public Func<SpeechToTextResponse> Respond { get; init; } = static () => new SpeechToTextResponse("text");

        public Task<SpeechToTextResponse> GetTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add((audioSpeechStream, options));

            return Failure is null ? Task.FromResult(Respond()) : Task.FromException<SpeechToTextResponse>(Failure);
        }

        public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
            Stream audioSpeechStream,
            SpeechToTextOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType == typeof(SpeechToTextClientMetadata) ? Metadata : null;

        public void Dispose()
        {
        }
    }
}
