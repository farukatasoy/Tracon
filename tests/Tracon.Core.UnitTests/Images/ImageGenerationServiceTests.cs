using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tracon.Tests.Common;

namespace Tracon.Core.UnitTests.Images;

/// <summary>
/// The host's own image call refuses what the tool and the endpoint refuse, before the provider is called, and ends a call that
/// outlives its limit as a provider failure.
/// </summary>
/// <remarks>
/// The cases count the calls that reached the generator: a refusal that still sent the prompt would pass a case that only read
/// the exception. The limit runs on the host's clock, so the timeout case advances a manual clock instead of waiting for it.
/// </remarks>
#pragma warning disable MEAI001
public sealed class ImageGenerationServiceTests
{
    private const string Provider = "service-images";

    [Fact]
    public async Task A_blank_prompt_and_a_count_over_the_limit_are_refused_before_the_provider_is_called()
    {
        var generator = new Generator();
        await using var provider = Build(generator);
        var images = provider.GetRequiredService<IImageGenerationService>();

        await Should.ThrowAsync<ArgumentException>(
            async () => await images.GenerateAsync(new TraconImageRequest { Prompt = "  " }, TestContext.Current.CancellationToken));

        await Should.ThrowAsync<ArgumentException>(
            async () => await images.GenerateAsync(new TraconImageRequest { Prompt = "a kite", Count = 0 }, TestContext.Current.CancellationToken));

        var over = await Should.ThrowAsync<TraconException>(
            async () => await images.GenerateAsync(new TraconImageRequest { Prompt = "a kite", Count = 2 }, TestContext.Current.CancellationToken));

        over.Message.ShouldContain("Tracon:Images:MaxImagesPerRequest", Case.Sensitive);
        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_provider_that_is_in_no_registration_is_refused_and_the_default_is_not_used_in_its_place()
    {
        var generator = new Generator();
        await using var provider = Build(generator);
        var images = provider.GetRequiredService<IImageGenerationService>();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await images.GenerateAsync(
                new TraconImageRequest { Prompt = "a kite", Provider = "service-imgs" },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("service-imgs", Case.Sensitive);
        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_call_that_outlives_the_limit_ends_as_a_provider_failure_and_records_no_measurement()
    {
        var clock = new ManualClock();
        var generator = new Generator { Hold = true };

        await using var provider = Build(generator, services => services.AddSingleton<TimeProvider>(clock));
        var images = provider.GetRequiredService<IImageGenerationService>();
        var steps = provider.GetRequiredService<ITraconStepRunner>();
        var runId = Guid.CreateVersion7();

        var run = steps.RunAsync(
            new TraconStepOptions { Name = "slow-image", RunId = runId },
            async token => await images.GenerateAsync(new TraconImageRequest { Prompt = "a kite" }, token).ConfigureAwait(false),
            TestContext.Current.CancellationToken).AsTask();

        await generator.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(3));

        var failure = await Should.ThrowAsync<ProviderInvocationException>(async () => await run);

        failure.Provider.ShouldBe(Provider);
        failure.Message.ShouldContain("Tracon:Images:Timeout", Case.Sensitive);

        (await provider.GetRequiredService<IRunStore>().ListNonTokenUsageAsync(runId, TestContext.Current.CancellationToken))
            .ShouldBeEmpty("a call that never returned an image spent nothing Tracon could measure");
    }

    private static ServiceProvider Build(Generator generator, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddTracon()
            .ConfigureImages(static images =>
            {
                images.Provider = Provider;
                images.Providers[Provider] = new TraconImageProviderOptions { Model = "image-1" };
            });

        services.AddKeyedSingleton<IImageGenerator>(Provider, generator);
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private sealed class Generator : IImageGenerator
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public bool Hold { get; init; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            Entered.TrySetResult();

            if (Hold)
            {
                // Held until the caller cancels it: the limit's own token is what ends this call.
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false); // delay: simulated
            }

            byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

            return new ImageGenerationResponse([new DataContent(png, "image/png")]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
