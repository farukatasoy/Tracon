using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Images;

/// <summary>Verifies that the configured provider selects the matching keyed image generator.</summary>
#pragma warning disable MEAI001
public sealed class ImageGeneratorResolverTests
{
    [Fact]
    public void Resolves_the_generator_keyed_by_the_configured_provider()
    {
        var expected = new StubImageGenerator();
        var services = new ServiceCollection();
        services.AddOptions<TraconImageOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Provider = "second";
            options.Model = "image-1";
        });
        services.AddKeyedSingleton<IImageGenerator>("first", new StubImageGenerator());
        services.AddKeyedSingleton<IImageGenerator>("second", expected);
        services.AddSingleton<ImageGeneratorResolver>();

        using var provider = services.BuildServiceProvider();

        AssertRingOver(expected, provider.GetRequiredService<ImageGeneratorResolver>().Resolve());
    }

    [Fact]
    public void Falls_back_to_an_unkeyed_consumer_generator()
    {
        var expected = new StubImageGenerator();
        var services = new ServiceCollection();
        services.AddOptions<TraconImageOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Provider = "custom";
            options.Model = "image-1";
        });
        services.AddSingleton<IImageGenerator>(expected);
        services.AddSingleton<ImageGeneratorResolver>();

        using var provider = services.BuildServiceProvider();

        AssertRingOver(expected, provider.GetRequiredService<ImageGeneratorResolver>().Resolve());
    }

    [Fact]
    public void Hands_out_the_same_ring_every_time_it_is_asked()
    {
        var generator = new StubImageGenerator();
        var services = new ServiceCollection();
        services.AddOptions<TraconImageOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Provider = "custom";
            options.Model = "image-1";
        });
        services.AddSingleton<IImageGenerator>(generator);
        services.AddSingleton<ImageGeneratorResolver>();

        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ImageGeneratorResolver>();

        // The resolver is asked on every generation; a ring per call would leak an activity source and a meter each time.
        resolver.Resolve().ShouldBeSameAs(resolver.Resolve());
    }

    // The resolver returns the registered generator inside the library's OpenTelemetry ring: the ring is what a
    // consumer's generator is called through, and the registered instance is what the ring wraps.
    private static void AssertRingOver(StubImageGenerator registered, IImageGenerator resolved)
    {
        resolved.ShouldNotBeSameAs(registered);
        resolved.GetService(typeof(ActivitySource)).ShouldNotBeNull("the generator is not inside the OpenTelemetry ring");
        resolved.GetService(typeof(StubImageGenerator)).ShouldBeSameAs(registered);
    }

    private sealed class StubImageGenerator : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResponse());

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
#pragma warning restore MEAI001
