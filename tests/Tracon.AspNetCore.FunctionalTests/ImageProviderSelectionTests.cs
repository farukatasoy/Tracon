using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// A host that registers two image providers sends each request to the provider it names, with that provider's own model and
/// settings.
/// </summary>
/// <remarks>
/// <para>
/// The two <c>Use*Images</c> calls used to write to one shared options object, so the provider was the first call's and the model
/// the last call's: the OpenAI generator was sent Gemini's model name, and the cost was looked up under the wrong model. The cases
/// here use the real registration calls and read what reached each generator, because a case that read the options would pass for
/// a request that went out with the wrong model.
/// </para>
/// <para>
/// The generators are recorders registered under the provider names before the <c>Use*Images</c> calls: those calls keep a
/// registration that is already there, so the call under test is the one that selects, not the one that builds.
/// </para>
/// </remarks>
#pragma warning disable MEAI001
public sealed class ImageProviderSelectionTests
{
    private const string OpenAIModel = "gpt-image-1";
    private const string GoogleModel = "gemini-3-pro-image-preview";
    private const string OtherOpenAIModel = "gpt-image-1-mini";
    private const decimal OpenAIPrice = 0.04m;
    private const decimal OtherOpenAIPrice = 0.01m;
    private const decimal GooglePrice = 0.12m;

    private static readonly Uri GenerateUri = new("/tracon/api/images/generate", UriKind.Relative);

    [Fact]
    public async Task Each_provider_is_sent_its_own_model_and_the_default_provider_draws()
    {
        var openai = new RecordingGenerator();
        var google = new RecordingGenerator();

        await using var host = await StartAsync(openai, google, defaultProvider: "openai");

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        google.Calls.ShouldBeEmpty("the default provider is OpenAI: nothing is sent to the other one");
        openai.Calls.ShouldHaveSingleItem().ModelId.ShouldBe(OpenAIModel, "the model of the call is the provider's own, never the one the last call wrote");

        // The price is looked up under the pair that drew. Each provider has its own price, so a pair taken from two places
        // would be priced at the other's rate, or at none.
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("usage").GetProperty("cost").GetDecimal().ShouldBe(OpenAIPrice);
    }

    [Fact]
    public async Task Naming_the_other_provider_moves_the_draw_and_its_model_with_it()
    {
        var openai = new RecordingGenerator();
        var google = new RecordingGenerator();

        await using var host = await StartAsync(openai, google, defaultProvider: "google");

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        openai.Calls.ShouldBeEmpty();
        google.Calls.ShouldHaveSingleItem().ModelId.ShouldBe(GoogleModel);
    }

    [Fact]
    public async Task The_providers_settings_reach_the_generator_of_that_provider_and_not_the_other()
    {
        var openai = new RecordingGenerator();
        var google = new RecordingGenerator();

        await using var host = await StartAsync(
            openai,
            google,
            defaultProvider: "openai",
            configureImages: static images => images.Providers["openai"].ProviderSettings["openai.quality"] = JsonSerializer.SerializeToElement("low"));

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        var properties = openai.Calls.ShouldHaveSingleItem().AdditionalProperties.ShouldNotBeNull();

        properties["openai.quality"].ShouldBeOfType<JsonElement>().GetString().ShouldBe("low");
    }

    [Fact]
    public async Task The_operator_request_chooses_the_provider_and_the_model_of_one_call()
    {
        var openai = new RecordingGenerator();
        var google = new RecordingGenerator();

        await using var host = await StartAsync(openai, google, defaultProvider: "openai");

        using var other = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse", provider = "google" });
        using var model = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a harbor", model = "gpt-image-1-mini" });

        other.StatusCode.ShouldBe(HttpStatusCode.OK, await other.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        model.StatusCode.ShouldBe(HttpStatusCode.OK, await model.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        google.Calls.ShouldHaveSingleItem().ModelId.ShouldBe(GoogleModel, "a provider named alone draws with its own model");
        openai.Calls.ShouldHaveSingleItem().ModelId.ShouldBe("gpt-image-1-mini");

        var usage = (await other.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("usage");

        usage.GetProperty("provider").GetString().ShouldBe("google");
        usage.GetProperty("model").GetString().ShouldBe(GoogleModel);
        usage.GetProperty("cost").GetDecimal().ShouldBe(GooglePrice);

        (await model.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("usage").GetProperty("cost").GetDecimal().ShouldBe(OtherOpenAIPrice);
    }

    [Theory]
    [InlineData("OpenAI")]
    [InlineData("OPENAI")]
    public async Task The_letter_case_of_a_provider_name_does_not_change_which_generator_draws(string spelled)
    {
        // The name is validated without regard to case and the generator is looked up by the registered spelling. A generator
        // that is found by the caller's own spelling would miss the keyed one and fall to an unkeyed generator of the host.
        var openai = new RecordingGenerator();
        var google = new RecordingGenerator();
        var custom = new RecordingGenerator();

        await using var host = await StartAsync(openai, google, defaultProvider: "google", extraServices: services => services.AddSingleton<IImageGenerator>(custom));

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse", provider = spelled });
        var answer = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, answer);
        openai.Calls.ShouldHaveSingleItem().ModelId.ShouldBe(OpenAIModel);
        custom.Calls.ShouldBeEmpty("the host's unkeyed generator was not asked to draw for a provider that has its own");
        google.Calls.ShouldBeEmpty();

        using var parsed = JsonDocument.Parse(answer);

        parsed.RootElement.GetProperty("usage").GetProperty("provider").GetString().ShouldBe("openai", "the registered spelling");
    }

    [Fact]
    public async Task A_tenant_policy_that_does_not_name_the_requested_provider_refuses_the_request_before_the_prompt_leaves()
    {
        var openai = new RecordingGenerator();
        var google = new RecordingGenerator();

        await using var host = await StartAsync(openai, google, defaultProvider: "openai");

        await host.Services.GetRequiredService<ITenantEgressPolicyStore>()
            .UpsertAsync("default", ["openai"], TestContext.Current.CancellationToken);

        using var refused = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse", provider = "google" });
        using var allowed = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        google.Calls.ShouldBeEmpty("the policy was asked about the provider the request named");
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK, await allowed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        openai.Calls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An_operator_request_that_names_an_unregistered_provider_is_refused_before_anything_is_sent()
    {
        var openai = new RecordingGenerator();
        var google = new RecordingGenerator();

        await using var host = await StartAsync(openai, google, defaultProvider: "openai");

        using var response = await host.Client.PostAsJsonAsync(GenerateUri, new { prompt = "a lighthouse", provider = "openia" });
        var answer = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, answer);
        answer.ShouldContain("openia", Case.Sensitive);
        openai.Calls.ShouldBeEmpty("a typo does not fall back to the default");
        google.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Two_providers_and_no_default_stop_the_host_at_startup_and_name_the_way_out()
    {
        var failure = await Should.ThrowAsync<OptionsValidationException>(
            async () =>
            {
                await using var host = await StartAsync(new RecordingGenerator(), new RecordingGenerator(), defaultProvider: null);
            });

        failure.Message.ShouldContain("Tracon:Images:Provider", Case.Sensitive, "the message says where to name the default");
        failure.Message.ShouldContain("openai", Case.Sensitive);
        failure.Message.ShouldContain("google", Case.Sensitive);
    }

    [Fact]
    public async Task A_provider_that_is_in_no_registration_is_not_a_default()
    {
        // The model is given, so the host is not stopped for a missing model: what stops it is that no generator is registered for
        // the name.
        var failure = await Should.ThrowAsync<TraconException>(
            async () =>
            {
                await using var host = await StartAsync(
                    new RecordingGenerator(),
                    new RecordingGenerator(),
                    defaultProvider: "nope",
                    configureImages: static images => images.Providers["nope"] = new TraconImageProviderOptions { Model = "m" });
            });

        failure.Message.ShouldContain("no image generator is registered for provider 'nope'", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync(
        RecordingGenerator openai,
        RecordingGenerator google,
        string? defaultProvider,
        Action<TraconImageOptions>? configureImages = null,
        Action<IServiceCollection>? extraServices = null)
        => TraconTestHost.StartAsync(
            configureTracon: builder => builder
                .UseOpenAI("sk-test-not-a-key")
                .UseGoogle("g-test-not-a-key")
                .UseOpenAIImages(static provider => provider.Model = OpenAIModel)
                .UseGoogleImages(static provider => provider.Model = GoogleModel)
                .ConfigureImages(images =>
                {
                    images.Enabled = true;
                    images.Provider = defaultProvider;
                    configureImages?.Invoke(images);
                }),
            configureServices: services =>
            {
                services.AddKeyedSingleton<IImageGenerator>("openai", openai);
                services.AddKeyedSingleton<IImageGenerator>("google", google);
                services.Configure<TraconOptions>(static options =>
                {
                    options.Pricing.Currency = "USD";
                    options.Pricing.Images["openai"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
                    {
                        [OpenAIModel] = new ImagePriceOverride { PerImage = OpenAIPrice },
                        [OtherOpenAIModel] = new ImagePriceOverride { PerImage = OtherOpenAIPrice },
                    };
                    options.Pricing.Images["google"] = new Dictionary<string, ImagePriceOverride>(StringComparer.OrdinalIgnoreCase)
                    {
                        [GoogleModel] = new ImagePriceOverride { PerImage = GooglePrice },
                    };
                });
                extraServices?.Invoke(services);
            });

    /// <summary>Records the options of every call and answers with one small image.</summary>
    private sealed class RecordingGenerator : IImageGenerator
    {
        private readonly Lock _gate = new();
        private readonly List<ImageGenerationOptions> _calls = [];

        public IReadOnlyList<ImageGenerationOptions> Calls
        {
            get
            {
                lock (_gate)
                {
                    return [.. _calls];
                }
            }
        }

        public Task<ImageGenerationResponse> GenerateAsync(
            ImageGenerationRequest request,
            ImageGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _calls.Add(options ?? new ImageGenerationOptions());
            }

            byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
            byte[] image = [.. signature, .. new byte[8]];

            return Task.FromResult(new ImageGenerationResponse([new DataContent(image, "image/png")]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
#pragma warning restore MEAI001
