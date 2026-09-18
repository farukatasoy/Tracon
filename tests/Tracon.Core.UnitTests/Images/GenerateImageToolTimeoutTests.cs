using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Images;

/// <summary>
/// The shipped <c>generate_image</c> registration carries its own timeout
/// instead of inheriting the installation-wide default.
/// </summary>
/// <remarks>
/// 🚨 What this closes: the registration set no timeout, so it inherited
/// <c>Tracon:Tools:DefaultTimeout</c> — 30 seconds, a sensible bound for a tool
/// that queries something. A measured <c>gpt-image-1</c> request routinely needs
/// 30 to 35 seconds, so the shipped default cut the call off just before it
/// succeeded: the model was told the tool failed while the provider produced
/// and billed a real image.
/// </remarks>
#pragma warning disable MEAI001
public sealed class GenerateImageToolTimeoutTests
{
    private static async Task<ToolDescriptor> DescribeAsync(Action<TraconImageOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTracon();
        services.Configure<TraconImageOptions>(options =>
        {
            options.Enabled = true;
            options.Provider = "custom";
            options.Model = "image-1";
            configure?.Invoke(options);
        });
        services.AddSingleton<IImageGenerator>(new StubImageGenerator());

        await using var provider = services.BuildServiceProvider();

        return provider.GetRequiredService<IToolRegistry>()
            .List()
            .Single(static tool => string.Equals(tool.Name, "generate_image", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_shipped_registration_does_not_inherit_the_generic_thirty_second_default()
    {
        var descriptor = await DescribeAsync();

        descriptor.Timeout.ShouldBe(TimeSpan.FromMinutes(2));

        // The value it used to inherit, named so the regression is unmistakable.
        descriptor.Timeout.ShouldNotBe(new TraconToolOptions().DefaultTimeout);
    }

    [Fact]
    public async Task An_installation_can_shorten_it()
    {
        // A shorter bound is a legitimate choice: a late-finishing call is
        // cancelled, and a provider that finishes anyway still has its spend
        // recorded against the original call.
        var descriptor = await DescribeAsync(static options => options.Timeout = TimeSpan.FromSeconds(45));

        descriptor.Timeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    private sealed class StubImageGenerator : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ImageGenerationResponse([new DataContent(new byte[] { 1 }, "image/png")]));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
