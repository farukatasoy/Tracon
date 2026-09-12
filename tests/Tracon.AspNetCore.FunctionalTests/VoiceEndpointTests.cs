using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Verifies the <c>/api/voice/*</c> endpoints.
/// </summary>
/// <remarks>
/// The test project <strong>does NOT</strong> reference <c>Tracon.Voice</c>:
/// the endpoints know only the <see cref="ISpeechSynthesizer"/> abstraction.
/// This is proof that the abstraction lives in the right package
/// (Abstractions) — the K-174 pattern.
/// </remarks>
public sealed class VoiceEndpointTests
{
    [Fact]
    public async Task Endpoints_return_501_when_no_provider()
    {
        // NOT 404: a wrong address and missing configuration must not be confused with each other.
        await using var host = await TraconTestHost.StartAsync();

        using var voices = await host.Client.GetAsync(new Uri("/tracon/api/voice/voices", UriKind.Relative));
        voices.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);

        using var health = await host.Client.GetAsync(new Uri("/tracon/api/voice/health", UriKind.Relative));
        health.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Voice_list_is_read_from_the_abstraction()
    {
        await using var host = await StartWithVoiceAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/voice/voices", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var body = await TraconTestHost.ReadJsonAsync(response);
        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("voiceId").GetString().ShouldBe("voice-1");
    }

    [Fact]
    public async Task Speak_writes_an_attachment_and_returns_the_measurement_in_the_response()
    {
        await using var host = await StartWithVoiceAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "hello world", sessionId = "session-1" });

        response.EnsureSuccessStatusCode();

        var body = await TraconTestHost.ReadJsonAsync(response);

        // 🚨 The session id MUST be written to the attachment: if left empty,
        // the retention policy treats the attachment as orphaned and deletes it.
        body.GetProperty("attachment").GetProperty("sessionId").GetString().ShouldBe("session-1");
        body.GetProperty("attachment").GetProperty("mediaType").GetString().ShouldBe("audio/mpeg");

        // The measurement is NOT written to tool_invocations (there is no run), but it is not invisible either.
        body.GetProperty("characters").GetInt32().ShouldBe("hello world".Length);
        body.GetProperty("isEstimated").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Written_attachment_is_downloadable_and_comes_back_as_audio()
    {
        await using var host = await StartWithVoiceAsync();

        using var speak = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "hello", sessionId = (string?)null });

        speak.EnsureSuccessStatusCode();

        var id = (await TraconTestHost.ReadJsonAsync(speak))
            .GetProperty("attachment").GetProperty("id").GetString();

        using var download = await host.Client.GetAsync(
            new Uri($"/tracon/api/attachments/{id}", UriKind.Relative));

        download.EnsureSuccessStatusCode();
        download.Content.Headers.ContentType?.MediaType.ShouldBe("audio/mpeg");
    }

    [Fact]
    public async Task IncludeTimestamps_false_returns_no_alignment()
    {
        await using var host = await StartWithVoiceAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "hi" });

        response.EnsureSuccessStatusCode();

        var body = await TraconTestHost.ReadJsonAsync(response);

        (!body.TryGetProperty("alignment", out var alignment) || alignment.ValueKind == JsonValueKind.Null)
            .ShouldBeTrue();
    }

    [Fact]
    public async Task IncludeTimestamps_true_returns_the_alignment_and_the_same_cost_accounting()
    {
        await using var host = await StartWithVoiceAsync();

        using var plain = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "hi" });
        plain.EnsureSuccessStatusCode();
        var plainBody = await TraconTestHost.ReadJsonAsync(plain);

        using var timestamped = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "hi", includeTimestamps = true });
        timestamped.EnsureSuccessStatusCode();
        var timestampedBody = await TraconTestHost.ReadJsonAsync(timestamped);

        var alignment = timestampedBody.GetProperty("alignment");
        alignment.GetArrayLength().ShouldBe(2);
        alignment[0].GetProperty("character").GetString().ShouldBe("h");
        alignment[1].GetProperty("character").GetString().ShouldBe("i");

        // Alignment is not a separate billing unit (docs/72, section 72.2, rule 3).
        timestampedBody.GetProperty("characters").GetInt32().ShouldBe(plainBody.GetProperty("characters").GetInt32());
        timestampedBody.GetProperty("isEstimated").GetBoolean().ShouldBe(plainBody.GetProperty("isEstimated").GetBoolean());
    }

    [Fact]
    public async Task Empty_text_returns_400()
    {
        await using var host = await StartWithVoiceAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "   " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Text_over_the_limit_returns_400_and_does_NOT_reach_the_provider()
    {
        // The HTTP operator endpoint had to enforce the same
        // MaxCharactersPerRequest limit that SpeakTool (the agent call)
        // already enforces — it previously did not (see
        // ISpeechSynthesizer.MaxCharactersPerRequest).
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new LimitedSynthesizer()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "this-text-is-longer-than-ten-characters" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Contains("MaxCharactersPerRequest", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task When_the_provider_rejects_its_body_is_NOT_copied_to_the_response()
    {
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new FailingSynthesizer()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "secret-text" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);

        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        text.Contains("FAKE-KEY", StringComparison.Ordinal).ShouldBeFalse();
    }

    [Fact]
    public async Task Headerless_audio_is_NOT_written_as_an_attachment()
    {
        // When the provider returns an unexpected format, the error surfaces at WRITE time.
        await using var host = await TraconTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new HeaderlessSynthesizer()));

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/voice/speak", UriKind.Relative),
            new { text = "hello" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
    }

    private static Task<TraconTestHost> StartWithVoiceAsync()
        => TraconTestHost.StartAsync(
            configureServices: static services =>
                services.AddSingleton<ISpeechSynthesizer>(new StubSynthesizer()));

    /// <summary>Returns a valid MP3 (ID3 tag + frame).</summary>
    private sealed class StubSynthesizer : ISpeechSynthesizer
    {
        private static readonly byte[] Mp3 =
            [0x49, 0x44, 0x33, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFB];

        public string ProviderName => "test-voice";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SpeechAudio
            {
                Data = Mp3,
                MediaType = "audio/mpeg",
                CharactersBilled = request.Text.Length,
                UsageSource = SpeechUsageSource.Provider,
                Alignment = request.IncludeTimestamps ? BuildAlignment(request.Text) : null,
            });

        private static List<SpeechAlignment> BuildAlignment(string text)
        {
            var result = new List<SpeechAlignment>(text.Length);

            for (var index = 0; index < text.Length; index++)
            {
                result.Add(new SpeechAlignment
                {
                    Character = text[index].ToString(),
                    Start = TimeSpan.FromSeconds(index * 0.1),
                    End = TimeSpan.FromSeconds((index + 1) * 0.1),
                });
            }

            return result;
        }

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return Mp3;
        }

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>(
                [new VoiceDescriptor { VoiceId = "voice-1", Name = "Test" }]);
    }

    /// <summary>Throws an error that echoes the provider's body.</summary>
    private sealed class FailingSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "test-voice";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new TraconException("Failed to synthesize speech: HTTP 401. API key is invalid.");

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new TraconException("Failed to synthesize speech: HTTP 401.");

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }

    /// <summary>Returns raw PCM: it doesn't match any magic bytes.</summary>
    private sealed class HeaderlessSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "test-voice";

        public int MaxCharactersPerRequest => 5000;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new SpeechAudio
            {
                Data = new byte[] { 0x01, 0x02, 0x03, 0x04 },
                MediaType = "audio/mpeg",
            });

        public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new byte[] { 0x01 };
        }

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }

    /// <summary>Carries an artificially low 10-character limit; tests the limit check.</summary>
    private sealed class LimitedSynthesizer : ISpeechSynthesizer
    {
        public string ProviderName => "test-voice";

        public int MaxCharactersPerRequest => 10;

        public ValueTask<SpeechAudio> SynthesizeAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "A request over the limit should NEVER have reached the provider.");

        public IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
            SpeechRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(
                "A request over the limit should NEVER have reached the provider.");

        public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<VoiceDescriptor>>([]);
    }
}
