using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AgentPrism.Core.UnitTests.Images;

/// <summary>Verifies that the configured provider selects the matching keyed image generator.</summary>
#pragma warning disable MEAI001
public sealed class ImageGeneratorResolverTests
{
    [Fact]
    public void Resolves_the_generator_keyed_by_the_configured_provider()
    {
        var expected = new StubImageGenerator();
        var services = new ServiceCollection();
        services.AddOptions<AgentPrismImageOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Provider = "second";
            options.Model = "image-1";
        });
        services.AddKeyedSingleton<IImageGenerator>("first", new StubImageGenerator());
        services.AddKeyedSingleton<IImageGenerator>("second", expected);
        services.AddSingleton<ImageGeneratorResolver>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ImageGeneratorResolver>().Resolve().ShouldBeSameAs(expected);
    }

    [Fact]
    public void Falls_back_to_an_unkeyed_consumer_generator()
    {
        var expected = new StubImageGenerator();
        var services = new ServiceCollection();
        services.AddOptions<AgentPrismImageOptions>().Configure(options =>
        {
            options.Enabled = true;
            options.Provider = "custom";
            options.Model = "image-1";
        });
        services.AddSingleton<IImageGenerator>(expected);
        services.AddSingleton<ImageGeneratorResolver>();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ImageGeneratorResolver>().Resolve().ShouldBeSameAs(expected);
    }

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
        }
    }
}
#pragma warning restore MEAI001
