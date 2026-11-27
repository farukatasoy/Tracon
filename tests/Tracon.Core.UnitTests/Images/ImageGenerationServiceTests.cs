using System.Text.Json;
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

    [Fact]
    public async Task A_call_setting_replaces_the_registered_one_for_that_call_only()
    {
        var generator = new Generator();
        await using var provider = BuildOpenAI(generator, registered => registered.ProviderSettings["openai.quality"] = Text("medium"));
        var images = provider.GetRequiredService<IImageGenerationService>();
        var request = new TraconImageRequest { Prompt = "a kite", ProviderSettings = { ["openai.quality"] = Text("high") } };

        await images.GenerateAsync(request, TestContext.Current.CancellationToken);
        await images.GenerateAsync(new TraconImageRequest { Prompt = "a kite" }, TestContext.Current.CancellationToken);

        generator.Options.Count.ShouldBe(2);
        QualityOf(generator.Options[0]).ShouldBe("high", "the call's value wins");
        QualityOf(generator.Options[1]).ShouldBe("medium", "the next call starts from the registered value again");

        provider.GetRequiredService<ImageProviderSelection>().Options.Providers["openai"].ProviderSettings["openai.quality"]
            .GetString().ShouldBe("medium", "the registered settings are not mutated");
        request.ProviderSettings["openai.quality"].GetString().ShouldBe("high", "the request's own dictionary is not mutated either");
    }

    [Fact]
    public async Task A_flat_per_image_price_is_one_price_for_every_quality_the_call_asks_for()
    {
        // The documented limit of the measurement: the quality of a call is not a price factor. A host that varies the quality
        // prices a token-billed model by tokens instead.
        var generator = new Generator();
        await using var provider = BuildOpenAI(
            generator,
            configureServices: services => services.Configure<TraconOptions>(options =>
            {
                options.Pricing.Currency = "USD";
                options.Pricing.Images["openai"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["image-1"] = new ImagePriceOverride { PerImage = 0.04m },
                };
            }));
        var images = provider.GetRequiredService<IImageGenerationService>();

        var high = await images.GenerateAsync(
            new TraconImageRequest { Prompt = "a kite", ProviderSettings = { ["openai.quality"] = Text("high") } },
            TestContext.Current.CancellationToken);
        var low = await images.GenerateAsync(
            new TraconImageRequest { Prompt = "a kite", ProviderSettings = { ["openai.quality"] = Text("low") } },
            TestContext.Current.CancellationToken);

        high.Usage.Cost.ShouldBe(0.04m);
        low.Usage.Cost.ShouldBe(high.Usage.Cost, "a flat per-image rate does not follow the quality");
    }

    [Fact]
    public async Task A_call_setting_with_no_registered_one_reaches_the_provider()
    {
        var generator = new Generator();
        await using var provider = BuildOpenAI(generator);
        var images = provider.GetRequiredService<IImageGenerationService>();

        await images.GenerateAsync(
            new TraconImageRequest { Prompt = "a kite", ProviderSettings = { ["openai.quality"] = Text("low") } },
            TestContext.Current.CancellationToken);

        QualityOf(generator.Options.ShouldHaveSingleItem()).ShouldBe("low");
    }

    [Fact]
    public async Task A_call_key_in_another_letter_case_replaces_the_registered_key_and_still_reaches_the_provider()
    {
        var generator = new Generator();
        await using var provider = BuildOpenAI(generator, registered => registered.ProviderSettings["openai.quality"] = Text("medium"));
        var images = provider.GetRequiredService<IImageGenerationService>();

        await images.GenerateAsync(
            new TraconImageRequest { Prompt = "a kite", ProviderSettings = { ["OpenAI.Quality"] = Text("high") } },
            TestContext.Current.CancellationToken);

        var options = generator.Options.ShouldHaveSingleItem();

        options.AdditionalProperties.ShouldNotBeNull().Count.ShouldBe(1, "one setting, not the registered one beside the call's");
        QualityOf(options).ShouldBe("high", "the lookup of the lower-case key finds the call's value: the options' dictionary ignores letter case");
    }

    [Theory]
    [InlineData("openai.size")]
    [InlineData("azure-openai.quality")]
    [InlineData("google.quality")]
    [InlineData("quality")]
    [InlineData("tracon.maxRetries")]
    public async Task A_call_setting_the_provider_does_not_support_is_refused_before_the_provider_is_called(string key)
    {
        var generator = new Generator();
        await using var provider = BuildOpenAI(generator);
        var images = provider.GetRequiredService<IImageGenerationService>();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await images.GenerateAsync(
                new TraconImageRequest { Prompt = "a kite", ProviderSettings = { [key] = Text("high") } },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(key, Case.Sensitive);
        failure.Message.ShouldNotContain("high", Case.Sensitive);
        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_call_setting_of_the_wrong_type_is_refused_and_the_message_names_no_value()
    {
        var generator = new Generator();
        await using var provider = BuildOpenAI(generator);
        var images = provider.GetRequiredService<IImageGenerationService>();

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await images.GenerateAsync(
                new TraconImageRequest { Prompt = "a kite", ProviderSettings = { ["openai.quality"] = JsonSerializer.SerializeToElement(4242) } },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("openai.quality", Case.Sensitive);
        failure.Message.ShouldNotContain("4242", Case.Sensitive);
        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_provider_that_supports_no_setting_refuses_every_call_setting()
    {
        var generator = new Generator();
        await using var provider = Build(generator);
        var images = provider.GetRequiredService<IImageGenerationService>();

        await Should.ThrowAsync<TraconException>(
            async () => await images.GenerateAsync(
                new TraconImageRequest { Prompt = "a kite", ProviderSettings = { ["openai.quality"] = Text("high") } },
                TestContext.Current.CancellationToken));

        generator.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task A_request_whose_settings_are_null_is_the_same_as_one_with_none()
    {
        var generator = new Generator();
        await using var provider = BuildOpenAI(generator, registered => registered.ProviderSettings["openai.quality"] = Text("medium"));
        var images = provider.GetRequiredService<IImageGenerationService>();

        await images.GenerateAsync(
            new TraconImageRequest { Prompt = "a kite", ProviderSettings = null! },
            TestContext.Current.CancellationToken);

        QualityOf(generator.Options.ShouldHaveSingleItem()).ShouldBe("medium");
    }

    [Fact]
    public async Task A_retry_ceiling_of_the_provider_travels_with_every_call_and_none_travels_when_none_is_set()
    {
        var capped = new Generator();
        await using (var provider = BuildOpenAI(capped, registered => registered.MaxRetries = 0))
        {
            await provider.GetRequiredService<IImageGenerationService>()
                .GenerateAsync(new TraconImageRequest { Prompt = "a kite" }, TestContext.Current.CancellationToken);
        }

        capped.Options.ShouldHaveSingleItem().AdditionalProperties.ShouldNotBeNull()
            [ImageProviderSettingKeys.MaxRetriesProperty].ShouldBe(0, "zero is a ceiling, not the absence of one");

        var open = new Generator();
        await using (var provider = BuildOpenAI(open))
        {
            await provider.GetRequiredService<IImageGenerationService>()
                .GenerateAsync(new TraconImageRequest { Prompt = "a kite" }, TestContext.Current.CancellationToken);
        }

        (open.Options.ShouldHaveSingleItem().AdditionalProperties?.ContainsKey(ImageProviderSettingKeys.MaxRetriesProperty) ?? false)
            .ShouldBeFalse("no ceiling is asked for, so the client's own count applies");
    }

    private static JsonElement Text(string value) => JsonSerializer.SerializeToElement(value);

    private static string? QualityOf(ImageGenerationOptions options)
        => options.AdditionalProperties is { } properties && properties.TryGetValue("openai.quality", out var value)
            ? (value as JsonElement?)?.GetString()
            : null;

    private static ServiceProvider BuildOpenAI(
        Generator generator,
        Action<TraconImageProviderOptions>? registered = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();

        services.AddTracon()
            .ConfigureImages(images =>
            {
                images.Provider = "openai";
                images.Providers["openai"] = new TraconImageProviderOptions { Model = "image-1" };
                registered?.Invoke(images.Providers["openai"]);
            });

        services.AddKeyedSingleton<IImageGenerator>("openai", generator);
        configureServices?.Invoke(services);

        return services.BuildServiceProvider();
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

        /// <summary>Gets the options of every call that reached the generator, in order.</summary>
        public List<ImageGenerationOptions> Options { get; } = [];

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);

            if (options is not null)
            {
                lock (Options)
                {
                    Options.Add(options);
                }
            }

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
