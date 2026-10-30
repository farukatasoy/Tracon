using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Gemini speech through the whole host: the named synthesizer behind <c>POST /api/voice/speak</c>, the provider list,
/// and the tenant gate, driven against a local server the real <c>Google.GenAI</c> client talks to.
/// </summary>
/// <remarks>
/// <para>
/// A unit test of the synthesizer proves the request it builds; only the host proves the request the tenant's
/// key travels on. Every case reads the <c>x-goog-api-key</c> header the server received, so a call that went out on
/// the wrong key is red even when the answer is <c>200</c>.
/// </para>
/// <para>
/// The egress rule is the chat provider's: <c>google</c>. A policy that names the registration name <c>gemini</c>
/// does not pass it, and a refusal leaves the server's request count at <c>0</c>.
/// </para>
/// </remarks>
public sealed class GeminiSpeechTests
{
    private const string SetupKey = "setup-gemini-key-00000000";
    private const string TenantKeyName = "Tracon:ProviderKeys:Google";
    private const string TenantKey = "default-tenant-gemini-key-1";

    private static readonly Uri SpeakUri = new("/tracon/api/voice/speak", UriKind.Relative);

    [Fact]
    public async Task Speak_with_the_gemini_provider_saves_a_wav_attachment_and_reports_an_estimated_count()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(server);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini", voiceId = "Kore" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var json = await TraconTestHost.ReadJsonAsync(response);

        json.GetProperty("attachment").GetProperty("mediaType").GetString().ShouldBe("audio/wav");
        json.GetProperty("attachment").GetProperty("byteSize").GetInt64().ShouldBeGreaterThan(ScriptedGeminiSpeechServer.Pcm.Length);
        json.GetProperty("characters").GetInt32().ShouldBe("Hello there".Length);
        json.GetProperty("isEstimated").GetBoolean().ShouldBeTrue();

        var call = server.Calls.ShouldHaveSingleItem();

        call.ApiKey.ShouldBe(SetupKey);
        call.Path.ShouldEndWith(":generateContent");
        call.Body["generationConfig"]!["speechConfig"]!["voiceConfig"]!["prebuiltVoiceConfig"]!["voiceName"]!.GetValue<string>().ShouldBe("Kore");
    }

    [Fact]
    public async Task A_model_that_answers_raw_pcm_is_still_saved_as_a_wav_attachment()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        server.Format = ScriptedGeminiSpeechServer.Generation.TextLabels;
        await using var host = await StartAsync(server);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini", voiceId = "Kore" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var attachment = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("attachment");

        attachment.GetProperty("mediaType").GetString().ShouldBe("audio/wav");
        attachment.GetProperty("byteSize").GetInt64().ShouldBe(44 + ScriptedGeminiSpeechServer.Pcm.Length);
    }

    [Fact]
    public async Task Voices_are_the_declared_ones_and_no_call_is_made()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(server);

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/voices?provider=gemini", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray()
            .Select(static voice => voice.GetProperty("voiceId").GetString())
            .ShouldBe(["Kore", "Puck"]);
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task The_provider_list_reports_the_dialogue_limits_of_gemini_and_none_for_a_provider_without_dialogue()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        var plain = new CountingSpeechProvider("plain-engine");

        await using var host = await StartAsync(server, builder => builder.AddSpeechSynthesizer("plain", _ => plain));

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/providers", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var providers = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
        var gemini = providers.Single(static entry => string.Equals(entry.GetProperty("name").GetString(), "gemini", StringComparison.Ordinal));
        var other = providers.Single(static entry => string.Equals(entry.GetProperty("name").GetString(), "plain", StringComparison.Ordinal));

        gemini.GetProperty("providerName").GetString().ShouldBe("google");
        gemini.GetProperty("supportsTenantCredentials").GetBoolean().ShouldBeTrue();

        var dialogue = gemini.GetProperty("dialogue");

        dialogue.GetProperty("maxVoices").GetInt32().ShouldBe(2);
        dialogue.GetProperty("maxCharactersPerRequest").GetInt32().ShouldBe(2000);
        dialogue.GetProperty("supportsSegmentTimestamps").GetBoolean().ShouldBeFalse();

        other.GetProperty("dialogue").ValueKind.ShouldBe(System.Text.Json.JsonValueKind.Null);
        server.Requests.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_dialogue_is_voiced_in_the_format_the_model_generation_takes(bool textLabels)
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        server.Format = textLabels ? ScriptedGeminiSpeechServer.Generation.TextLabels : ScriptedGeminiSpeechServer.Generation.PartMetadata;
        await using var host = await StartAsync(
            server,
            configureSpeech: options => options.DialogueSpeakerMode = textLabels
                ? GoogleDialogueSpeakerMode.TextLabels
                : GoogleDialogueSpeakerMode.PartMetadata);

        var dialogue = await Resolver(host).GetDialogueSynthesizerAsync("gemini", TestContext.Current.CancellationToken);
        var result = await dialogue.SynthesizeDialogueAsync(Turns(), TestContext.Current.CancellationToken);

        result.Audio.MediaType.ShouldBe("audio/wav");
        result.Audio.Duration.ShouldBe(TimeSpan.FromSeconds(1));
        result.Segments.ShouldBeNull();

        var call = server.Calls.ShouldHaveSingleItem();

        call.ApiKey.ShouldBe(SetupKey);
        call.Body["generationConfig"]!["speechConfig"]!["multiSpeakerVoiceConfig"]!["speakerVoiceConfigs"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_dialogue_in_the_wrong_format_for_the_model_names_the_other_format_and_leaks_nothing()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        server.Format = ScriptedGeminiSpeechServer.Generation.TextLabels;
        await using var host = await StartAsync(server);

        var dialogue = await Resolver(host).GetDialogueSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await dialogue.SynthesizeDialogueAsync(Turns(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("HTTP 400");
        exception.Message.ShouldContain("to TextLabels");
        exception.Message.ShouldNotContain("Speech metadata");
        exception.Message.ShouldNotContain(SetupKey);
    }

    [Fact]
    public async Task A_dialogue_refused_before_the_network_never_reaches_the_server()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(server);

        var dialogue = await Resolver(host).GetDialogueSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        await Should.ThrowAsync<TraconException>(
            async () => await dialogue.SynthesizeDialogueAsync(
                new SpeechDialogueRequest
                {
                    Turns =
                    [
                        new SpeechDialogueTurn { Text = "a", VoiceId = "Kore" },
                        new SpeechDialogueTurn { Text = "b", VoiceId = "Puck" },
                        new SpeechDialogueTurn { Text = "c", VoiceId = "Charon" },
                    ],
                },
                TestContext.Current.CancellationToken));

        await Should.ThrowAsync<TraconException>(
            async () => await dialogue.SynthesizeDialogueAsync(
                Turns() with { IncludeTimestamps = true },
                TestContext.Current.CancellationToken));

        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task A_bound_tenant_is_served_on_its_own_key_everywhere_and_the_key_appears_in_no_response_and_no_log()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(server, keys: [(TenantKeyName, TenantKey)]);
        await BindAsync(host, "default", TenantKeyName);

        using var spoken = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini", voiceId = "Kore" },
            TestContext.Current.CancellationToken);
        using var providers = await host.Client.GetAsync(new Uri("/tracon/api/voice/providers", UriKind.Relative), TestContext.Current.CancellationToken);

        spoken.StatusCode.ShouldBe(HttpStatusCode.OK, await spoken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var resolver = Resolver(host);
        var synthesizer = await resolver.GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);
        var dialogue = await resolver.GetDialogueSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

        _ = await synthesizer.SynthesizeAsync(new SpeechRequest { Text = "again", VoiceId = "Puck" }, TestContext.Current.CancellationToken);
        _ = await dialogue.SynthesizeDialogueAsync(Turns(), TestContext.Current.CancellationToken);
        await foreach (var chunk in synthesizer.SynthesizeStreamingAsync(
            new SpeechRequest { Text = "streamed", VoiceId = "Puck" },
            TestContext.Current.CancellationToken))
        {
            chunk.IsEmpty.ShouldBeFalse();
        }

        server.Calls.Select(static call => call.ApiKey).ShouldBe([TenantKey, TenantKey, TenantKey, TenantKey]);
        server.Calls.Count(static call => call.Streaming).ShouldBe(1);

        (await spoken.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(TenantKey);
        (await providers.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(TenantKey);
        host.Logs.AllText.ShouldNotContain(TenantKey);
        host.Logs.AllText.ShouldNotContain(SetupKey);
    }

    [Fact]
    public async Task A_tenant_without_a_binding_is_served_on_the_setup_key()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(server, keys: [(TenantKeyName, TenantKey)]);
        await BindAsync(host, "acme", TenantKeyName);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini", voiceId = "Kore" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        server.Calls.ShouldHaveSingleItem().ApiKey.ShouldBe(SetupKey);
    }

    [Fact]
    public async Task Two_tenants_in_parallel_each_send_their_own_key_and_a_third_the_setup_key()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(
            server,
            keys: [("Tracon:ProviderKeys:acme:Google", "acme-gemini-key-000000"), ("Tracon:ProviderKeys:globex:Google", "globex-gemini-key-00000")]);

        await BindAsync(host, "acme", "Tracon:ProviderKeys:acme:Google");
        await BindAsync(host, "globex", "Tracon:ProviderKeys:globex:Google");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acme"] = "acme-gemini-key-000000",
            ["globex"] = "globex-gemini-key-00000",
            ["initech"] = SetupKey,
        };
        var tenants = expected.Keys.ToArray();

        var calls = Enumerable.Range(0, 30).Select(index => Task.Run(async () =>
        {
            var tenant = tenants[index % tenants.Length];

            TraconRunContext.SetCurrent(Scope(tenant));

            try
            {
                var resolver = Resolver(host);

                if (index % 2 == 0)
                {
                    var synthesizer = await resolver.GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

                    _ = await synthesizer.SynthesizeAsync(
                        new SpeechRequest { Text = $"{tenant}:{index}", VoiceId = "Kore" },
                        TestContext.Current.CancellationToken);
                }
                else
                {
                    var dialogue = await resolver.GetDialogueSynthesizerAsync("gemini", TestContext.Current.CancellationToken);

                    _ = await dialogue.SynthesizeDialogueAsync(
                        new SpeechDialogueRequest
                        {
                            Turns =
                            [
                                new SpeechDialogueTurn { Text = $"{tenant}:{index}", VoiceId = "Kore" },
                                new SpeechDialogueTurn { Text = "reply", VoiceId = "Puck" },
                            ],
                        },
                        TestContext.Current.CancellationToken);
                }
            }
            finally
            {
                TraconRunContext.SetCurrent(null);
            }
        }));

        await Task.WhenAll(calls);

        server.Requests.ShouldBe(30);

        foreach (var call in server.Calls)
        {
            var text = call.Body["contents"]![0]!["parts"]![0]!["text"]!.GetValue<string>();
            var tenant = text[..text.IndexOf(':', StringComparison.Ordinal)];

            call.ApiKey.ShouldBe(expected[tenant], $"the call '{text}' carried another tenant's key");
        }
    }

    [Fact]
    public async Task The_policy_judges_the_provider_name_google_and_not_the_registration_name()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(server);

        await SavePolicyAsync(host, "echo", "gemini");

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await Resolver(host).GetSynthesizerAsync("gemini", TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("speech provider 'google'");

        using var refused = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini" },
            TestContext.Current.CancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        server.Requests.ShouldBe(0, "a registration alias passed the tenant's egress policy");

        await SavePolicyAsync(host, "echo", "google");

        using var allowed = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini" },
            TestContext.Current.CancellationToken);

        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        server.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task A_binding_with_an_endpoint_override_is_refused_and_the_server_is_not_reached()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsync(server, keys: [(TenantKeyName, TenantKey)]);
        await BindAsync(host, "default", TenantKeyName, endpoint: "https://proxy.example.com/");

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(TenantKey);
        server.Requests.ShouldBe(0);
    }

    [Fact]
    public async Task A_provider_failure_is_a_bad_gateway_that_carries_neither_the_key_nor_the_provider_text()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        server.FailNext(StatusCodes.Status429TooManyRequests);
        await using var host = await StartAsync(server);

        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini", voiceId = "Kore" },
            TestContext.Current.CancellationToken);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway, body);
        body.ShouldContain("HTTP 429");
        body.ShouldNotContain(SetupKey);
        body.ShouldNotContain(ScriptedGeminiSpeechServer.UpstreamText);
        host.Logs.AllText.ShouldNotContain(SetupKey);
    }

    [Fact]
    public async Task The_speak_tool_serves_the_default_gemini_synthesizer_once_the_format_is_wav()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsDefaultAsync(server, static voice => voice.OutputFormat = "wav");

        var result = await InvokeSpeakAsync(host, "hello there");

        result.ShouldBeOfType<string>().ShouldStartWith("Audio produced.");

        var call = server.Calls.ShouldHaveSingleItem();

        call.ApiKey.ShouldBe(SetupKey);
        call.Path.ShouldEndWith(":generateContent");
    }

    [Fact]
    public async Task The_speak_tool_with_the_default_mp3_format_fails_naming_the_setting_and_calls_nothing()
    {
        // UseVoice's format defaults to an ElevenLabs name (mp3_44100_128). Gemini cannot produce it, and the
        // tool must say which setting to change instead of sending a request Google answers with 400.
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        await using var host = await StartAsDefaultAsync(server);

        var exception = await Should.ThrowAsync<TraconException>(async () => await InvokeSpeakAsync(host, "hello there"));

        exception.Message.ShouldContain("'wav' and 'pcm'");
        exception.Message.ShouldContain("Tracon:Voice:OutputFormat");
        server.Requests.ShouldBe(0);
    }

    private static Task<TraconTestHost> StartAsDefaultAsync(ScriptedGeminiSpeechServer server, Action<VoiceOptions>? voice = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseGoogle(options =>
                {
                    options.ApiKey = SetupKey;
                    options.Endpoint = server.BaseAddress;
                })
                .UseGoogleSpeech(options =>
                {
                    options.Model = "gemini-scripted-tts";
                    options.UseAsDefault = true;
                })
                // The built-in ElevenLabs client serves the transcriber only here: Gemini was registered first and
                // is the default synthesizer. Its key is never used.
                .UseVoice(options =>
                {
                    options.ApiKey = "unused-transcriber-key-0001";
                    voice?.Invoke(options);
                }));

    private static async Task<object?> InvokeSpeakAsync(TraconTestHost host, string text)
    {
        var tool = (AIFunction)host.Services.GetServices<TraconToolRegistration>()
            .Single(registration => string.Equals(registration.Function.Name, "speak", StringComparison.Ordinal))
            .Function;

        TraconRunContext.SetCurrent(Scope("default"));

        try
        {
            // No Services on the arguments: the tool must work from what it took at setup.
            return await tool.InvokeAsync(
                new AIFunctionArguments(new Dictionary<string, object?>(StringComparer.Ordinal) { ["text"] = text }, StringComparer.Ordinal),
                TestContext.Current.CancellationToken);
        }
        finally
        {
            TraconRunContext.SetCurrent(null);
        }
    }

    private static ISpeechProviderResolver Resolver(TraconTestHost host)
        => host.Services.GetRequiredService<ISpeechProviderResolver>();

    private static SpeechDialogueRequest Turns()
        => new()
        {
            Turns =
            [
                new SpeechDialogueTurn { Text = "Welcome back.", VoiceId = "Kore" },
                new SpeechDialogueTurn { Text = "Glad to be here.", VoiceId = "Puck" },
            ],
        };

    private static AgentRunScope Scope(string tenant)
        => new()
        {
            RunId = Guid.NewGuid(),
            RootRunId = Guid.NewGuid(),
            TenantId = tenant,
            SessionId = "session-1",
            AgentName = "podcast",
        };

    private static async Task SavePolicyAsync(TraconTestHost host, params string[] allowed)
        => await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", allowed, TestContext.Current.CancellationToken);

    private static Task BindAsync(TraconTestHost host, string tenantId, string keyName, string? endpoint = null)
        => host.Services.GetRequiredService<ITenantProviderBindingStore>().UpsertAsync(
            new TenantProviderBinding
            {
                TenantId = tenantId,
                ProviderName = GoogleProviderNames.Google,
                ApiKeyConfigurationName = keyName,
                Endpoint = endpoint,
                UpdatedAt = DateTimeOffset.UtcNow,
            },
            TestContext.Current.CancellationToken).AsTask();

    private static Task<TraconTestHost> StartAsync(
        ScriptedGeminiSpeechServer server,
        Action<ITraconBuilder>? configureTracon = null,
        Action<GoogleSpeechOptions>? configureSpeech = null,
        (string Key, string Value)[]? keys = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder
                    .UseGoogle(options =>
                    {
                        options.ApiKey = SetupKey;
                        options.Endpoint = server.BaseAddress;
                    })
                    .UseGoogleSpeech(options =>
                    {
                        options.Model = "gemini-scripted-tts";
                        options.Voices.Add(new VoiceDescriptor { VoiceId = "Kore", Name = "Kore" });
                        options.Voices.Add(new VoiceDescriptor { VoiceId = "Puck", Name = "Puck" });
                        configureSpeech?.Invoke(options);
                    });

                configureTracon?.Invoke(builder);
            },
            configureServices: services => services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection((keys ?? []).Select(static key => new KeyValuePair<string, string?>(key.Key, key.Value)))
                    .Build()));
}
