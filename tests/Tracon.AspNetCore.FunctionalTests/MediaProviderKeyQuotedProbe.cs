using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A provider that refuses a Google image call, a Google speech call or an OpenAI speech call and quotes the key the host
/// was registered with back in its answer leaves that key in no output that carries the text of a provider failure.
/// </summary>
/// <remarks>
/// The OpenAI image call has its own case in <see cref="ImageProviderKeyQuotedProbe"/>. These reach the provider without the
/// chat pipeline that replaces a quoted key: the Google image generator goes through the Google SDK, which puts the
/// provider's body into the exception it throws, and a wrapper replaces the key there (<c>ProviderFailureMasking</c>); the
/// two speech clients throw a failure of their own that keeps the service's text out (the OpenAI transport and the Gemini
/// synthesizer), so their cases are gates, and need no replacement. The key is searched for in the responses the caller reads, the log lines, the log records
/// an exporter would send, the spans, the metric tags and every byte of the database. An empty search proves nothing unless
/// the call reached the provider with the key, and, for the image call that the SDK quotes, unless the replacement is on the
/// page: both are asserted.
/// </remarks>
public sealed class MediaProviderKeyQuotedProbe
{
    [Fact]
    public async Task A_provider_that_quotes_the_setup_key_back_to_a_google_image_call_leaves_it_in_no_output()
    {
        await AssertNoLeakAsync(
            "google-image",
            static (builder, key, endpoint) => builder
                .UseGoogle(key, options => options.Endpoint = endpoint)
                .UseGoogleImages(static options =>
                {
                    options.Enabled = true;
                    options.Model = "canary-image-model";
                }),
            HttpMethod.Post,
            "/tracon/api/images/generate",
            new { prompt = "a cat" },
            expectReplaced: true);
    }

    [Fact]
    public async Task A_provider_that_quotes_the_setup_key_back_to_a_google_speech_call_leaves_it_in_no_output()
    {
        await AssertNoLeakAsync(
            "google-speech",
            static (builder, key, endpoint) =>
            {
                builder.Services.AddSingleton<ISpeechTranscriber>(new StubVoiceProvider());

                builder
                    .UseGoogle(key, options => options.Endpoint = endpoint)
                    .UseGoogleSpeech(static options =>
                    {
                        options.Model = "canary-speech-model";
                        options.UseAsDefault = true;
                    })
                    .UseVoice(static _ => { });
            },
            HttpMethod.Post,
            "/tracon/api/voice/speak",
            new { text = "hello" },
            expectReplaced: false);
    }

    [Fact]
    public async Task A_provider_that_quotes_the_setup_key_back_to_an_openai_speech_call_leaves_it_in_no_output()
    {
        await AssertNoLeakAsync(
            "openai-speech",
            static (builder, key, endpoint) => builder
                .UseOpenAI(key, options => options.Endpoint = endpoint)
                .UseOpenAISpeech(static options =>
                {
                    options.SynthesisModel = "canary-tts-model";
                    options.TranscriptionModel = "canary-stt-model";
                    options.UseAsDefault = true;
                })
                .UseVoice(static _ => { }),
            HttpMethod.Post,
            "/tracon/api/voice/speak",
            new { text = "hello" });
    }

    private static async Task AssertNoLeakAsync(
        string scenario,
        Action<ITraconBuilder, string, Uri> register,
        HttpMethod method,
        string url,
        object body,
        bool expectReplaced = false)
    {
        using var database = new TempSqliteDatabase("media-key-quote");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var wire = await CanaryWireServer.StartAsync();

        wire.Mode = CanaryWireServer.Behavior.RefuseAndQuote;

        var stem = $"canary-{scenario}-setup-key";
        var key = stem + "-3b8e51c07d92a4f6";

        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder =>
            {
                builder.UseSqlite(database.ConnectionString);
                register(builder, key, wire.BaseAddress);
            },
            configureServices: services =>
            {
                services.AddLogging(logging => logging.AddProvider(structured));
                services.Configure<TraconEgressOptions>(static options => options.AllowPrivateNetworkTargets = true);
                services.Configure<TraconOptions>(static options =>
                {
                    options.Observability.RecordSensitiveData = true;
                    options.Observability.SuccessSampleRatio = 1;
                });
            });

        var drive = new CanaryDrive(host);
        var status = await drive.SendAsync(method, url, body);

        status.ShouldBe(HttpStatusCode.BadGateway, drive.Seen);
        wire.Requests.ShouldBeGreaterThanOrEqualTo(1, "the call never reached the provider, so an empty search proves nothing");
        wire.Credentials.ShouldContain(credential => credential.Contains(key, StringComparison.Ordinal), "the provider was never sent the key");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = drive.Seen,
            ["log lines"] = host.Logs.AllText,
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        if (expectReplaced)
        {
            string.Join('\n', outputs.Values).ShouldContain("[redacted]", Case.Sensitive, "the provider's quote is not in any output, so the replacement was never exercised");
        }

        var hits = SecretHits.Find(outputs, [new($"{scenario} setup key", stem)]);

        foreach (var encoding in SecretHits.FindInBytes(database.ReadAllBytes(), stem))
        {
            hits.Add($"the {scenario} setup key is in the database as {encoding}");
        }

        hits.ShouldBeEmpty();
    }
}
