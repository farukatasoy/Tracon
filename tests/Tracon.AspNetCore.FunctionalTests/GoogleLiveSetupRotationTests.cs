using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The Google provider set up with the configuration key of its key sends the value that is current when a call starts, for
/// a compiled agent, for speech and for images, and the text of a failure that quotes the new key is masked.
/// </summary>
/// <remarks>
/// <para>
/// The same boundaries as the OpenAI case (<see cref="LiveSetupKeyRotationTests"/>): the agent is compiled once and cached, so
/// the client it holds was built before the rotation, and the real Google client talks to a local server, so the key that
/// counts is the one on the wire.
/// </para>
/// <para>
/// The image case adds the mask: the image resolver builds the mask of a call's failure once, long before the key
/// rotates, and the provider that quotes the key it was sent quotes the new one.
/// </para>
/// </remarks>
public sealed class GoogleLiveSetupRotationTests
{
    private const string AgentName = "google-live-setup-agent";
    private const string KeyName = "Platform:Google:Key";
    private const string StartupKey = "startup-gemini-key-00000000";
    private static readonly Uri SpeakUri = new("/tracon/api/voice/speak", UriKind.Relative);

    [Fact]
    public async Task A_rotated_setup_key_is_the_key_of_the_next_run_of_the_same_compiled_agent()
    {
        await using var wire = await CanaryWireServer.StartAsync();
        var configuration = Configuration((KeyName, "gemini-key-one-000000"));

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseGoogle(options =>
                {
                    options.ApiKey = StartupKey;
                    options.Endpoint = wire.BaseAddress;
                    options.ApiKeyConfigurationName = KeyName;
                })
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Answer briefly.",
                    Model = new ModelBinding { Provider = GoogleProviderNames.Google, Model = "canary-model" },
                    Origin = AgentDefinitionOrigin.Code,
                }),
            configureServices: services => services.AddSingleton(configuration));

        await RunAsync(host);

        configuration[KeyName] = "gemini-key-two-000000";

        await RunAsync(host);

        // The name has no value for a moment (a source that reloads reads an empty file): the last valid key is used.
        configuration[KeyName] = "  ";

        await RunAsync(host);

        wire.Credentials.ShouldBe(["gemini-key-one-000000", "gemini-key-two-000000", "gemini-key-two-000000"]);
    }

    [Fact]
    public async Task A_rotated_setup_key_is_the_key_of_the_next_speech_call_through_the_host()
    {
        await using var server = await ScriptedGeminiSpeechServer.StartAsync();
        var configuration = Configuration((KeyName, "gemini-key-one-000000"));

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseGoogle(options =>
                {
                    options.ApiKey = StartupKey;
                    options.Endpoint = server.BaseAddress;
                    options.ApiKeyConfigurationName = KeyName;
                })
                .UseGoogleSpeech(options =>
                {
                    options.Model = "gemini-scripted-tts";
                    options.Voices.Add(new VoiceDescriptor { VoiceId = "Kore", Name = "Kore" });
                }),
            configureServices: services => services.AddSingleton(configuration));

        await SpeakAsync(host);

        configuration[KeyName] = "gemini-key-two-000000";

        await SpeakAsync(host);

        server.Calls.Select(static call => call.ApiKey).ShouldBe(["gemini-key-one-000000", "gemini-key-two-000000"]);
    }

    [Fact]
    public async Task A_provider_that_quotes_the_rotated_key_back_to_an_image_call_leaves_neither_key_in_any_output()
    {
        const string Stem = "canary-google-live-setup-key";
        const string KeyOne = Stem + "-one-3b8e51c07d92a4f6";
        const string KeyTwo = Stem + "-two-8d20a6f1c47b95e3";

        using var database = new TempSqliteDatabase("google-live-setup-quote");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();
        var configuration = Configuration((KeyName, KeyOne));

        await using var wire = await CanaryWireServer.StartAsync();

        wire.Mode = CanaryWireServer.Behavior.RefuseAndQuote;

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                builder
                    .UseGoogle(options =>
                    {
                        options.ApiKey = StartupKey;
                        options.Endpoint = wire.BaseAddress;
                        options.ApiKeyConfigurationName = KeyName;
                    })
                    .UseGoogleImages(static provider => provider.Model = "canary-image-model")
                    .ConfigureImages(static images => images.Enabled = true);
            },
            configureServices: services =>
            {
                services.AddSingleton(configuration);
                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(static options =>
                {
                    options.Observability.RecordSensitiveData = true;
                    options.Observability.SuccessSampleRatio = 1;
                });
            });

        var drive = new CanaryDrive(host);

        // The first call builds the mask of the image failure, with the first key.
        (await drive.SendAsync(HttpMethod.Post, "/tracon/api/images/generate", new { prompt = "a cat" })).ShouldBe(HttpStatusCode.BadGateway, drive.Seen);

        configuration[KeyName] = KeyTwo;

        // The second call is sent with the new key, and the provider quotes it back.
        (await drive.SendAsync(HttpMethod.Post, "/tracon/api/images/generate", new { prompt = "a cat" })).ShouldBe(HttpStatusCode.BadGateway, drive.Seen);

        wire.Credentials.ShouldContain(credential => credential.Contains(KeyTwo, StringComparison.Ordinal), "the provider was never sent the new key");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = drive.Seen,
            ["log lines"] = host.Logs.AllText,
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        string.Join('\n', outputs.Values).ShouldContain("[redacted]", Case.Sensitive, "the provider's quote is not in any output, so the replacement was never exercised");

        var hits = SecretHits.Find(outputs, [new("google setup key (rotated)", Stem)]);

        foreach (var encoding in SecretHits.FindInBytes(database.ReadAllBytes(), Stem))
        {
            hits.Add($"a google setup key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }

    private static IConfiguration Configuration(params (string Name, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(static pair => new KeyValuePair<string, string?>(pair.Name, pair.Value)))
            .Build();

    private static async Task RunAsync(TraconTestHost host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"/tracon/api/agents/{AgentName}/run", UriKind.Relative))
        {
            Content = JsonContent.Create(new AgentRunRequest { Message = "hello", SessionId = Guid.NewGuid().ToString("N") }),
        };

        // The Idempotency-Key header selects the single JSON response.
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        using var response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    private static async Task SpeakAsync(TraconTestHost host)
    {
        using var response = await host.Client.PostAsJsonAsync(
            SpeakUri,
            new { text = "Hello there", provider = "gemini", voiceId = "Kore" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }
}
