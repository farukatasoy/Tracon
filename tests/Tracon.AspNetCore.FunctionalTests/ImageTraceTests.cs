using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Phase 266: an image the run generates is a span of the run.
/// </summary>
/// <remarks>
/// <para>
/// The resolver handed out the consumer's generator as it was, so the slowest call of an image run (the tool timeout
/// of <c>generate_image</c> exists because a request routinely needs 30 seconds) read in the trace as one opaque tool
/// span. The generator is now inside Microsoft.Extensions.AI's OpenTelemetry ring, and the ring is built once, not
/// per call: the resolver is asked on every generation.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class ImageTraceTests
{
    private const string ProviderName = "trace-images";
    private const string AgentName = "image-trace-agent";

    // The library names every non-chat call "generate_content" and tells them apart by the output type.
    private const string GenerateContent = "generate_content";
    private const string ImageOutput = "image";

    [Fact]
    public async Task The_generate_image_tool_writes_an_image_span_into_the_run_trace()
    {
        await using var host = await StartAsync(new StubImageGenerator());

        var (_, trace) = await RunTraceReader.RunAsync(host, AgentName);

        var span = RunTraceReader.Operations(trace, GenerateContent).ShouldHaveSingleItem();

        span.Attributes["gen_ai.output.type"].ShouldBe(ImageOutput);
    }

    [Fact]
    public async Task Every_way_to_the_generator_hands_out_the_same_ring_and_never_the_raw_generator()
    {
        var generator = new StubImageGenerator();
        await using var host = await StartAsync(generator);

        var resolver = host.Services.GetRequiredService<ImageGeneratorResolver>();

        var startup = resolver.Resolve();
        var generation = await resolver.ResolveForTenantAsync("default", TestContext.Current.CancellationToken);

        startup.ShouldNotBeSameAs(generator, "the startup check handed out the raw generator");
        generation.ShouldNotBeSameAs(generator, "the generation path handed out the raw generator");
        generation.ShouldBeSameAs(startup, "a ring per call would leak an activity source and a meter per generated image");
    }

    private static Task<TraconTestHost> StartAsync(StubImageGenerator generator)
        => TraconTestHost.StartAsync(
            builder => builder
                .AddModelProvider(new FakeModelProvider("image-trace-model")
                    .CallsTool("generate_image", new { prompt = "a teal lighthouse" })
                    .EchoesLastToolResult())
                .AddAgent(new AgentDefinition
                {
                    Name = AgentName,
                    Instructions = "Generate the requested image.",
                    Model = new ModelBinding { Provider = "image-trace-model", Model = "image-1" },
                    ToolNames = ["generate_image"],
                }),
            configureServices: services =>
            {
                services.Configure<TraconImageOptions>(options =>
                {
                    options.Enabled = true;
                    options.Provider = ProviderName;
                    options.Model = "image-1";
                });
                services.Configure<TraconOptions>(static options => options.Observability.SuccessSampleRatio = 1);
                services.AddSingleton<IImageGenerator>(generator);
            });

    private sealed class StubImageGenerator : IImageGenerator
    {
        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[8]];

            return Task.FromResult(new ImageGenerationResponse([new DataContent(png, "image/png")]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
