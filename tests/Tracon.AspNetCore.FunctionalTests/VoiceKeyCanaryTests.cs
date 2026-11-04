using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The API key of the voice provider (ElevenLabs) is sent to that provider and appears nowhere else: not in a response,
/// an attachment record, a log record in any of its parts, a span, a metric tag or a byte of the database.
/// </summary>
/// <remarks>
/// <para>
/// The provider is a real local HTTP server that speaks the ElevenLabs wire and keeps the <c>xi-api-key</c> header it is
/// sent, so the key is known to have travelled the whole way. The scenarios are the ones the key meets: the health
/// probe and the voice list (which send it), a synthesis that succeeds and is stored as an attachment, and a synthesis
/// the provider refuses, once without naming the key and once quoting it back in the body. Unlike the model providers'
/// setup keys, the voice key is not found in any output in either case: a failed synthesis answers with Tracon's own
/// sentence, not the provider's.
/// </para>
/// </remarks>
public sealed class VoiceKeyCanaryTests
{
    private const string Stem = "canary-voice-elevenlabs-key";
    private const string Key = Stem + "-0f6b3a91d84c27e5";

    [Fact]
    public async Task The_voice_key_reaches_the_provider_and_no_other_output()
        => (await DriveAsync(providerQuotes: false)).ShouldBeEmpty();

    [Fact]
    public async Task The_voice_key_is_in_no_output_when_the_provider_quotes_it_back_in_its_refusal()
        => (await DriveAsync(providerQuotes: true)).ShouldBeEmpty();

    private static async Task<List<string>> DriveAsync(bool providerQuotes)
    {
        using var database = new TempSqliteDatabase("voice-key-canary");
        using var probe = new LeakProbe();
        var structured = new StructuredLogProbe();

        await using var provider = await FakeElevenLabs.StartAsync();
        provider.Quotes = providerQuotes;
        await using var host = await TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseSqlite(database.ConnectionString)
                .UseVoice(options =>
                {
                    options.ApiKey = Key;
                    options.Endpoint = provider.Address;
                    options.DefaultVoiceId = "v1";
                }),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
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

        foreach (var refuses in (bool[])[false, true])
        {
            provider.Refuses = refuses;

            foreach (var path in (string[])["/tracon/api/voice/health", "/tracon/api/voice/voices", "/tracon/api/voice/providers", "/tracon/api/voice/sessions"])
            {
                await drive.SendAsync(HttpMethod.Get, path);
            }

            var spoken = await drive.SendAsync(HttpMethod.Post, "/tracon/api/voice/speak", new { text = "hello from the canary" });

            if (!refuses)
            {
                spoken.ShouldBe(HttpStatusCode.OK, "the synthesis did not succeed, so the stored attachment is not there to search");
            }
        }

        (await drive.ReadEverythingAsync()).ShouldBeGreaterThan(40);

        provider.Keys.ShouldContain(static key => string.Equals(key, Key, StringComparison.Ordinal), "the provider was never sent the key");
        provider.SynthesisRequests.ShouldBeGreaterThanOrEqualTo(2, "the synthesis has to have reached the provider, or an empty search proves nothing");

        var bytes = database.ReadAllBytes();

        SecretHits.FindInBytes(bytes, "audio/mpeg").ShouldNotBeEmpty("the stored synthesis is not in the database, so the scan reads the wrong place");

        var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["responses and records"] = drive.Seen,
            ["log lines"] = host.Logs.AllText,
            ["log records (state, scopes, exception parts)"] = structured.Text,
            ["span names, tags, baggage and events"] = probe.Spans,
            ["metric tags"] = probe.Measurements,
        };

        var hits = SecretHits.Find(outputs, [new("voice key", Stem)]);

        foreach (var encoding in SecretHits.FindInBytes(bytes, Stem))
        {
            hits.Add($"the voice key is in the database as {encoding}");
        }

        return hits;
    }

    /// <summary>A server that speaks enough of the ElevenLabs wire for health, voices and text to speech, and keeps the key it is sent.</summary>
    private sealed class FakeElevenLabs : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly List<string> _keys = [];
        private int _synthesis;

        private FakeElevenLabs(WebApplication app, Uri address)
        {
            _app = app;
            Address = address;
        }

        public Uri Address { get; }

        public bool Refuses { get; set; }

        public bool Quotes { get; set; }

        public int SynthesisRequests => Volatile.Read(ref _synthesis);

        public IReadOnlyList<string> Keys
        {
            get
            {
                lock (_keys)
                {
                    return [.. _keys];
                }
            }
        }

        public static async Task<FakeElevenLabs> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            var app = builder.Build();
            FakeElevenLabs? server = null;

            app.Map("/{**path}", async (HttpContext context) =>
            {
                lock (server!._keys)
                {
                    server._keys.Add(context.Request.Headers["xi-api-key"].ToString());
                }

                var path = context.Request.Path.Value ?? string.Empty;

                if (path.Contains("text-to-speech", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref server._synthesis);

                    if (server.Refuses)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key" + (server.Quotes ? ": " + context.Request.Headers["xi-api-key"] : string.Empty) + "\"}}", context.RequestAborted);

                        return;
                    }

                    context.Response.ContentType = "audio/mpeg";
                    // An ID3 header, so the attachment guard recognizes the bytes as audio.
                    await context.Response.Body.WriteAsync("ID3\u0003\0\0\0\0\0\0"u8.ToArray().Concat(new byte[256]).ToArray(), context.RequestAborted);

                    return;
                }

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(path.Contains("voices", StringComparison.Ordinal) ? "{\"voices\":[{\"voice_id\":\"v1\",\"name\":\"Canary\"}]}" : "{}", context.RequestAborted);
            });

            await app.StartAsync();

            server = new FakeElevenLabs(app, new Uri($"{app.Urls.First()}/"));

            return server;
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
