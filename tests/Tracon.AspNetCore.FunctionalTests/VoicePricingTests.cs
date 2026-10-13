using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>POST /api/voice/speak</c> prices a synthesis by the provider and the model that
/// produced it.
/// </summary>
/// <remarks>
/// F-298 (§198.5): the endpoint asked the pricing reader for "characters" only, which
/// priced every synthesis as the default synthesizer's configured model. A named
/// synthesizer was then billed at another engine's rate, or not at all. The reader
/// here records what it was asked, so a wrong provider or model is visible even when
/// the amount happens to match.
/// </remarks>
public sealed class VoicePricingTests
{
    private static readonly Uri SpeakUri = new("/tracon/api/voice/speak", UriKind.Relative);

    [Fact]
    public async Task A_named_synthesizer_is_priced_by_its_provider_and_the_model_it_reported()
    {
        var pricing = new RecordingPricing();
        await using var host = await StartAsync(pricing, new CountingSpeechProvider("studio-engine", reportedModel: "studio-v2"));

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("cost").GetDecimal().ShouldBe(0.5m);
        pricing.Asked.ShouldBe([("studio-engine", "studio-v2")]);
        pricing.DefaultAsked.ShouldBe(0);
    }

    [Fact]
    public async Task A_named_synthesizer_without_a_model_is_not_priced_as_the_default()
    {
        var pricing = new RecordingPricing();
        await using var host = await StartAsync(pricing, new CountingSpeechProvider("studio-engine"));

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello", provider = "studio" });

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        (!body.TryGetProperty("cost", out var cost) || cost.ValueKind == System.Text.Json.JsonValueKind.Null).ShouldBeTrue();
        pricing.DefaultAsked.ShouldBe(0, "a named synthesizer borrowed the default synthesizer's price");
    }

    [Fact]
    public async Task The_default_synthesizer_keeps_the_older_reader_member_as_its_fallback()
    {
        // A pricing reader written before the provider-aware member answers only
        // ForCharacters, and the default synthesizer is the one that member describes.
        var pricing = new RecordingPricing();
        await using var host = await StartAsync(pricing, new CountingSpeechProvider("studio-engine"), asDefault: true);

        using var response = await host.Client.PostAsJsonAsync(SpeakUri, new { text = "hello" });

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        (await TraconTestHost.ReadJsonAsync(response)).GetProperty("cost").GetDecimal().ShouldBe(0.25m);
        pricing.DefaultAsked.ShouldBe(1);
    }

    private static Task<TraconTestHost> StartAsync(RecordingPricing pricing, CountingSpeechProvider engine, bool asDefault = false)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder.AddSpeechSynthesizer("studio", _ => engine),
            configureServices: services =>
            {
                services.AddSingleton<IVoicePricingReader>(pricing);

                if (asDefault)
                {
                    services.AddSingleton<ISpeechSynthesizer>(engine);
                }
            });

    /// <summary>Prices only <c>studio-v2</c>, and records every question.</summary>
    private sealed class RecordingPricing : IVoicePricingReader
    {
        private int _defaultAsked;

        public ConcurrentQueue<(string Provider, string? Model)> Asked { get; } = new();

        public int DefaultAsked => Volatile.Read(ref _defaultAsked);

        public string? Currency => "USD";

        public decimal? ForCharacters(decimal characters)
        {
            Interlocked.Increment(ref _defaultAsked);

            return 0.25m;
        }

        public decimal? ForSynthesis(string providerName, string? modelId, decimal characters)
        {
            Asked.Enqueue((providerName, modelId));

            return string.Equals(modelId, "studio-v2", StringComparison.Ordinal) ? 0.5m : null;
        }
    }
}
