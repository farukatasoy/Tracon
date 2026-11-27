using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// <c>GET /api/diagnostics</c> and the startup warning name the speech, transcription and image models the host configured
/// when no price covers them.
/// </summary>
/// <remarks>
/// 🚨 What this closes: the report listed catalog models only, and a voice or image model is never in a catalog. A consumer
/// measured two voice models unpriced while every one of 27 voice sessions recorded no cost, and neither the report nor the
/// startup log said so. The cases run the real host: the option sources, the price table and the report meet only there.
/// </remarks>
#pragma warning disable MEAI001
public sealed class DiagnosticsMediaPricingTests
{
    private const string ImageProvider = "studio-images";
    private static readonly Uri Diagnostics = new("/tracon/api/diagnostics", UriKind.Relative);

    [Fact]
    public async Task The_report_and_the_startup_warning_name_the_configured_media_models_that_carry_no_price()
    {
        await using var host = await StartAsync(static pricing =>
            pricing.Voice["elevenlabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["eleven_v4_turbo"] = new() { PerMillionCharacters = 30m },
            });

        using var response = await host.Client.GetAsync(Diagnostics);
        response.EnsureSuccessStatusCode();

        var pricing = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("pricing");

        var unpriced = pricing.GetProperty("unpricedMediaModels").EnumerateArray()
            .Select(static model => model.GetString())
            .ToList();

        // The priced synthesis model is absent; the unpriced transcription and image models are named, in kind order.
        unpriced.ShouldBe(["elevenlabs/scribe_v2 (transcription)", $"{ImageProvider}/image-1 (image generation)"]);

        // The same list, once, in the log an operator reads at startup.
        var warning = host.Logs.Entries
            .Where(static entry => entry.Contains("configured speech, transcription or image model(s)", StringComparison.Ordinal))
            .ShouldHaveSingleItem();

        warning.ShouldContain("elevenlabs/scribe_v2 (transcription)");
        warning.ShouldContain($"{ImageProvider}/image-1 (image generation)");
        warning.ShouldNotContain("eleven_v4_turbo");
    }

    [Fact]
    public async Task A_fully_priced_media_configuration_is_an_empty_list_and_no_warning()
    {
        await using var host = await StartAsync(static pricing =>
        {
            pricing.Voice["elevenlabs"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["eleven_v4_turbo"] = new() { PerMillionCharacters = 30m },
                ["scribe_v2"] = new() { PerMinute = 0.4m },
            };
            pricing.Images[ImageProvider] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["image-1"] = new() { PerImage = 0.04m },
            };
        });

        using var response = await host.Client.GetAsync(Diagnostics);
        response.EnsureSuccessStatusCode();

        var pricing = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("pricing");

        // Present as an array, not absent: a consumer reads the field without a null check.
        pricing.GetProperty("unpricedMediaModels").GetArrayLength().ShouldBe(0);
        host.Logs.AllText.ShouldNotContain("configured speech, transcription or image model(s)");
    }

    private static Task<TraconTestHost> StartAsync(Action<TraconPricingOptions> configurePricing)
        => TraconTestHost.StartAsync(
            configureTracon: static builder => builder.UseVoice(static options =>
            {
                options.ApiKey = "test-voice-key";
                options.SynthesisModelId = "eleven_v4_turbo";
                options.TranscriptionModelId = "scribe_v2";
            }),
            configureEndpoints: static options => options.EnableDiagnosticsEndpoint = true,
            configureServices: services =>
            {
                services.Configure<TraconOptions>(options => configurePricing(options.Pricing));
                services.Configure<TraconImageOptions>(static options =>
                {
                    options.Enabled = true;
                    options.Provider = ImageProvider;
                    options.Providers[ImageProvider] = new TraconImageProviderOptions { Model = "image-1" };
                });
                services.AddSingleton<IImageGenerator>(new StubImageGenerator());
            });

    private sealed class StubImageGenerator : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResponse());

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
            // Nothing to release.
        }
    }
}
