using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A Microsoft.Extensions.AI speech client served as a Tracon speech provider passes the tenant's
/// checks before the service is reached, and sends the request the service documents.
/// </summary>
/// <remarks>
/// <para>
/// F-579. The bridge is a control-plane seam, not a media helper (the plan's counter-argument to
/// "write your own adapter"): the tenant's egress policy (K-930), the refusal of a tenant's own
/// key, the character limit and the host's startup are applied by Tracon's resolver and
/// registration, so an adapter written by hand could skip all of them. Every refusal case here
/// asserts that the scripted service saw <b>no request</b>: a <c>403</c> after the text left the
/// process proves nothing.
/// </para>
/// <para>
/// The OpenAI cases drive the real <c>OpenAIClient</c> against <see cref="ScriptedOpenAIAudioServer"/>
/// and assert the wire. The generic cases use a client that carries no provider name.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class SpeechClientBridgeTests
{
    private const string KeyName = "Tracon:ProviderKeys:Speech";
    private static readonly Uri SpeakUri = new("/tracon/api/voice/speak", UriKind.Relative);

    [Fact]
    public async Task Speak_sends_the_model_voice_and_text_the_service_expects_and_saves_the_audio()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello there", provider = "openai", voiceId = "nova" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var body = server.SpeechBodies.ShouldHaveSingleItem();
        body["model"]!.GetValue<string>().ShouldBe("tts-test");
        body["input"]!.GetValue<string>().ShouldBe("hello there");
        body["voice"]!.GetValue<string>().ShouldBe("nova");
        body.ContainsKey("response_format").ShouldBeFalse();

        var json = await TraconTestHost.ReadJsonAsync(response);
        json.GetProperty("characters").GetInt32().ShouldBe(11);

        // The service reports no billed characters: the quantity is the text length, said so.
        json.GetProperty("isEstimated").GetBoolean().ShouldBeTrue();
        json.GetProperty("cost").ValueKind.ShouldBe(JsonValueKind.Null);
        json.GetProperty("attachment").GetProperty("mediaType").GetString().ShouldBe("audio/mpeg");
    }

    [Fact]
    public async Task A_call_that_names_no_voice_is_left_to_the_client_and_the_service_still_gets_one()
    {
        // Measured with Microsoft.Extensions.AI.OpenAI 10.11.0: the client sends "alloy" when
        // no voice is named, and the service refuses a body without one.
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "openai" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        server.SpeechBodies.ShouldHaveSingleItem()["voice"]!.GetValue<string>().ShouldBe("alloy");
    }

    [Fact]
    public async Task A_tenant_whose_egress_denies_the_provider_cannot_speak_through_the_bridge()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        await SavePolicyAsync(host, "anthropic");

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "openai" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldContain("not allowed to call speech provider 'openai'");
        server.Requests.ShouldBe(0, "the tenant's text must not reach a provider its policy forbids");
    }

    [Fact]
    public async Task A_policy_that_names_the_provider_lets_the_call_through()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        await SavePolicyAsync(host, "anthropic", "openai");

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "openai" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        server.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task A_tenant_with_its_own_key_is_refused_and_the_setup_key_is_not_used()
    {
        // The client was built with the setup key. Speaking anyway bills the host for the
        // tenant's speech (the K-878 rule, as for images).
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(
            server,
            configureServices: static services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection([new(KeyName, "tenant-speech-key")]).Build()));

        await host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = "default",
                ProviderName = "openai",
                ApiKeyConfigurationName = KeyName,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "openai" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The reason, not just the status: a binding whose key has no value is refused with other words.
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("own credential");
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task Speak_rejects_text_above_the_declared_limit_before_the_service_is_called()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server, speech: static options => options.MaxCharactersPerRequest = 10);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = new string('a', 11), provider = "openai" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("the limit is 10");
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task A_service_failure_is_a_502_that_does_not_echo_the_service_body()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);
        server.FailNext(500, times: 10);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "openai" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldContain("Speech could not be generated");

        // The status and the machine code are enough to act on; the service's text is not printed.
        body.ShouldContain("HTTP 500 (model_not_found)");
        body.ShouldNotContain("upstream-body-text-that-must-not-leak");
    }

    [Fact]
    public async Task A_request_for_character_timing_is_refused_with_a_502_and_the_service_is_not_called()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "openai", includeTimestamps = true },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("IncludeTimestamps");
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task The_provider_list_shows_the_bridged_names_and_that_they_take_no_tenant_key()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/providers", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listed = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Select(static entry => (
                Name: entry.GetProperty("name").GetString(),
                Kind: entry.GetProperty("kind").GetString(),
                Provider: entry.GetProperty("providerName").GetString(),
                Credentials: entry.GetProperty("supportsTenantCredentials").GetBoolean()))
            .ToList();

        listed.ShouldBe(
        [
            ("openai", "Synthesizer", "openai", false),
            ("openai", "Transcriber", "openai", false),
        ]);
        server.Requests.ShouldBe(0, "listing the providers must not call the service");
    }

    [Fact]
    public async Task The_declared_voices_are_listed_without_calling_the_service()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(
            server,
            speech: static options => options.Voices.Add(new VoiceDescriptor { VoiceId = "nova", Name = "Nova" }));

        using var response = await host.Client.GetAsync(
            new Uri("/tracon/api/voice/voices?provider=openai", UriKind.Relative),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response))[0].GetProperty("voiceId").GetString().ShouldBe("nova");
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task A_format_name_of_the_voice_options_is_sent_as_the_container_the_service_knows()
    {
        // `speak` sends VoiceOptions.OutputFormat ("mp3_44100_128" by default) to the default
        // synthesizer. The service refuses that name; the client sends it as it is.
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);
        var synthesizer = await Resolver(host).GetSynthesizerAsync("openai", TestContext.Current.CancellationToken);

        var audio = await synthesizer.SynthesizeAsync(
            new SpeechRequest { Text = "hello", OutputFormat = "mp3_44100_128" },
            TestContext.Current.CancellationToken);

        server.SpeechBodies.ShouldHaveSingleItem()["response_format"]!.GetValue<string>().ShouldBe("mp3");
        audio.Data.ToArray().ShouldBe(ScriptedOpenAIAudioServer.Mp3);
        audio.ModelId.ShouldBe("tts-test");
    }

    [Fact]
    public async Task A_request_for_another_model_is_refused_because_the_client_would_ignore_it()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);
        var synthesizer = await Resolver(host).GetSynthesizerAsync("openai", TestContext.Current.CancellationToken);

        var exception = await Should.ThrowAsync<TraconException>(async () => await synthesizer.SynthesizeAsync(
            new SpeechRequest { Text = "hello", ModelId = "tts-other" },
            TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("'tts-other'");
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task Streaming_yields_the_first_chunk_before_the_service_has_finished_the_audio()
    {
        // The raw chunked answer, not server-sent events: every speech model serves it, and the client
        // that reads server-sent events cannot be published with Native AOT. The first half of the audio
        // arrives while the service still holds the second.
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        server.StreamGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = await StartAsync(server);
        var synthesizer = await Resolver(host).GetSynthesizerAsync("openai", TestContext.Current.CancellationToken);

        await using var chunks = synthesizer
            .SynthesizeStreamingAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        var streamed = new List<byte>();

        (await chunks.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken)).ShouldBeTrue();
        streamed.AddRange(chunks.Current.ToArray());

        streamed.ShouldBe(ScriptedOpenAIAudioServer.Mp3[..ScriptedOpenAIAudioServer.FirstChunkLength]);

        server.StreamGate.SetResult();

        while (await chunks.MoveNextAsync())
        {
            streamed.AddRange(chunks.Current.ToArray());
        }

        streamed.ShouldBe(ScriptedOpenAIAudioServer.Mp3);
        server.Requests.ShouldBe(1);
        server.SpeechBodies.ShouldHaveSingleItem().ContainsKey("stream_format").ShouldBeFalse();
    }

    [Fact]
    public async Task A_streaming_call_the_service_refuses_names_the_status_and_not_the_service_text()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);
        var synthesizer = await Resolver(host).GetSynthesizerAsync("openai", TestContext.Current.CancellationToken);
        server.FailNext(500, times: 10);

        var exception = await Should.ThrowAsync<TraconException>(async () =>
        {
            await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(
                               new SpeechRequest { Text = "hello" },
                               TestContext.Current.CancellationToken))
            {
                chunk.Length.ShouldBeGreaterThan(0);
            }
        });

        exception.Message.ShouldContain("HTTP 500 (model_not_found)");
        exception.Message.ShouldNotContain("upstream-body-text-that-must-not-leak");
    }

    [Fact]
    public async Task A_transcription_sends_the_audio_the_model_and_the_language_and_reports_the_model_used()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);
        var transcriber = await Resolver(host).GetTranscriberAsync("openai", TestContext.Current.CancellationToken);

        var transcript = await transcriber.TranscribeAsync(
            new MemoryStream(ScriptedOpenAIAudioServer.Mp3),
            "audio/mpeg",
            new SpeechTranscriptionOptions { LanguageCode = "tr" },
            TestContext.Current.CancellationToken);

        transcript.Text.ShouldBe("where is my order");
        transcript.ModelId.ShouldBe("stt-test");

        // The client reads no duration from the answer, so there is none to price.
        transcript.AudioDuration.ShouldBeNull();

        var form = server.Transcriptions.ShouldHaveSingleItem();
        form.Model.ShouldBe("stt-test");
        form.Language.ShouldBe("tr");
        form.FileName.ShouldBe("audio.mp3");
        form.FileLength.ShouldBe(ScriptedOpenAIAudioServer.Mp3.Length);
    }

    [Fact]
    public async Task A_tenant_whose_egress_denies_the_provider_cannot_transcribe_through_the_bridge()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);

        await SavePolicyAsync(host, "anthropic");

        await Should.ThrowAsync<TraconTenantProviderRefusedException>(async () => await Resolver(host)
            .GetTranscriberAsync("openai", TestContext.Current.CancellationToken));
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task A_transcription_failure_is_a_tracon_exception_that_does_not_echo_the_service_body()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server);
        var transcriber = await Resolver(host).GetTranscriberAsync("openai", TestContext.Current.CancellationToken);
        server.FailNext(500, times: 10);

        var exception = await Should.ThrowAsync<TraconException>(async () => await transcriber.TranscribeAsync(
            new MemoryStream(ScriptedOpenAIAudioServer.Mp3),
            "audio/mpeg",
            null,
            TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("HTTP 500 (model_not_found)");
        exception.Message.ShouldNotContain("upstream-body-text-that-must-not-leak");
    }

    [Fact]
    public async Task The_unnamed_default_is_the_openai_synthesizer_when_the_options_ask_for_it()
    {
        await using var server = await ScriptedOpenAIAudioServer.StartAsync();
        await using var host = await StartAsync(server, speech: static options => options.UseAsDefault = true);

        // No "provider": the default synthesizer answers, as it does for the speak tool.
        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        server.SpeechBodies.ShouldHaveSingleItem()["model"]!.GetValue<string>().ShouldBe("tts-test");
    }

    [Fact]
    public async Task The_egress_policy_names_the_provider_name_and_not_the_registration_name()
    {
        // K-930: two names for one service are one provider to the policy. The registration is
        // "studio"; the service is "vendor-x". A policy that lists only the registration name
        // must not let the text through, and one that lists the provider name must.
        var client = new CountingTextToSpeechClient();
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddSpeechSynthesizer(
                "studio",
                new TextToSpeechClientBinding { ProviderName = "vendor-x", MaxCharactersPerRequest = 100 }),
            configureServices: services => services.AddSingleton<ITextToSpeechClient>(client));

        await SavePolicyAsync(host, "studio");

        using var denied = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "studio" },
            TestContext.Current.CancellationToken);

        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("'vendor-x'");
        client.Calls.ShouldBe(0);

        await SavePolicyAsync(host, "vendor-x");

        using var allowed = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "hello", provider = "studio" },
            TestContext.Current.CancellationToken);

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        client.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task A_client_without_a_provider_name_stops_the_host()
    {
        // K-930: the egress policy is applied to the provider name. A provider that has none
        // would pass every policy, so the host must not start.
        var exception = await Should.ThrowAsync<OptionsValidationException>(() => TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddSpeechSynthesizer(
                "nameless",
                new TextToSpeechClientBinding { MaxCharactersPerRequest = 100 }),
            configureServices: static services => services.AddSingleton<ITextToSpeechClient>(new NamelessClient())));

        exception.Message.ShouldContain("'nameless'");
        exception.Message.ShouldContain("no provider name");
    }

    [Fact]
    public async Task A_missing_keyed_client_stops_the_host()
    {
        // Without the startup check the first tenant request would fail instead, on a path
        // that has nothing to do with the cause.
        var exception = await Should.ThrowAsync<OptionsValidationException>(() => TraconTestHost.StartAsync(
            configureTracon: static builder => builder.AddSpeechSynthesizer(
                "keyed",
                new TextToSpeechClientBinding { ServiceKey = "gpu-box", ProviderName = "gpu", MaxCharactersPerRequest = 100 })));

        exception.Message.ShouldContain("'keyed'");
        exception.Message.ShouldContain("'gpu-box'");
    }

    [Fact]
    public async Task A_host_that_names_a_speech_model_without_the_openai_provider_stops()
    {
        var exception = await Should.ThrowAsync<OptionsValidationException>(() => TraconTestHost.StartAsync(
            configureTracon: static builder => builder.UseOpenAISpeech(static options => options.SynthesisModel = "tts-test")));

        exception.Message.ShouldContain("UseOpenAI(");
    }

    private static ISpeechProviderResolver Resolver(TraconTestHost host)
        => host.Services.GetRequiredService<ISpeechProviderResolver>();

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task<TraconTestHost> StartAsync(
        ScriptedOpenAIAudioServer server,
        Action<OpenAISpeechOptions>? speech = null,
        Action<IServiceCollection>? configureServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseOpenAI(options =>
                {
                    options.ApiKey = "scripted-server-key";
                    options.Endpoint = server.BaseAddress;
                })
                .UseOpenAISpeech(options =>
                {
                    options.SynthesisModel = "tts-test";
                    options.TranscriptionModel = "stt-test";
                    speech?.Invoke(options);
                }),
            configureServices: configureServices);

    /// <summary>A text-to-speech client that counts its calls and answers with a valid MP3 frame.</summary>
    private sealed class CountingTextToSpeechClient : ITextToSpeechClient
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            return Task.FromResult(new TextToSpeechResponse([new DataContent(ScriptedOpenAIAudioServer.Mp3, "audio/mpeg")]));
        }

        public IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>A text-to-speech client that reports no metadata, so it has no provider name to offer.</summary>
    private sealed class NamelessClient : ITextToSpeechClient
    {
        public Task<TextToSpeechResponse> GetAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
            string text,
            TextToSpeechOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
