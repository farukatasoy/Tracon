using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.AI;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The support table of the shipped providers, measured from the model adapters, as the
/// validation endpoint and the save path report it. A field the provider's API surface would drop
/// is an error that names the field and the provider; it is never dropped silently.
/// </summary>
/// <remarks>
/// <para>
/// The table below is the one measured for this feature (MEAI.OpenAI 10.11.0, Google.GenAI 1.26.1, decompiled
/// 2026-10-20) with the Anthropic row measured again on 2026-10-28 (SDK 13.3.0: no temperature, top-p
/// or top-k) and the Google row on SDK 1.27.0. A row changes when an SDK upgrade changes what its adapter sends;
/// the <c>maf-api-kesfi</c> skill's fourth step measures it again.
/// </para>
/// <para>
/// The providers are the real ones, built without a network call, so a row fails if the provider
/// package declares something other than the measured support.
/// </para>
/// </remarks>
public sealed class ModelSamplingSupportTests
{
    private const string TestKey = "functional-test-key";
    private const string CompatibleProvider = "compatible-test";

    /// <summary>The fields in the order of the support table.</summary>
    private static readonly string[] Fields =
    [
        "seed", "topK", "stopSequences", "frequencyPenalty", "presencePenalty", "toolMode", "allowMultipleToolCalls", "reasoningOutput", "temperature", "topP",
    ];

    // true: the provider's API surface sends the field. One column per provider name.
    private static readonly Dictionary<string, bool[]> Support = new(StringComparer.Ordinal)
    {
        // seed, topK, stop, freq, pres, toolMode, multiple, reasoningOutput, temperature, topP
        [OpenAIProviderNames.ChatCompletions] = [true, false, true, true, true, true, true, false, true, true],
        [OpenAIProviderNames.Responses] = [false, false, false, false, false, true, true, true, true, true],
        [CompatibleProvider] = [true, false, true, true, true, true, true, false, true, true],
        [CompatibleProvider + "-responses"] = [false, false, false, false, false, true, true, true, true, true],
        [AzureOpenAIProviderNames.AzureOpenAI] = [true, false, true, true, true, true, true, false, true, true],
        [AnthropicProviderNames.Anthropic] = [false, false, true, false, false, true, true, true, false, false],
        [GoogleProviderNames.Google] = [true, true, true, true, true, true, false, true, true, true],
    };

    public static TheoryData<string, string, bool> Table()
    {
        var data = new TheoryData<string, string, bool>();

        foreach (var (provider, support) in Support)
        {
            for (var index = 0; index < Fields.Length; index++)
            {
                data.Add(provider, Fields[index], support[index]);
            }
        }

        return data;
    }

    /// <param name="provider">The provider name.</param>
    /// <param name="field">The request field the definition sets.</param>
    /// <param name="supported">Whether the provider's API surface sends it.</param>
    [Theory]
    [MemberData(nameof(Table))]
    public async Task The_validation_report_agrees_with_the_measured_support_table(string provider, string field, bool supported)
    {
        await using var host = await StartAsync();

        using var response = await ValidateAsync(host, provider, field);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await TraconTestHost.ReadJsonAsync(response);
        var errors = body.GetProperty("messages").EnumerateArray().ToList();

        if (supported)
        {
            body.GetProperty("valid").GetBoolean().ShouldBeTrue(body.ToString());
            errors.ShouldBeEmpty();
            return;
        }

        body.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var error = errors.ShouldHaveSingleItem();
        error.GetProperty("code").GetString().ShouldBe("sampling_field_unsupported");
        error.GetProperty("path").GetString().ShouldBe($"model.{field}");

        var message = error.GetProperty("message").GetString().ShouldNotBeNull();
        message.ShouldContain($"'{provider}' provider", Case.Sensitive);
        message.ShouldContain($"ModelBinding.{char.ToUpperInvariant(field[0])}{field[1..]}", Case.Sensitive);
    }

    /// <summary>The save path runs the same check: the agent is not stored, and the answer names the field and the provider.</summary>
    [Fact]
    public async Task Saving_a_seed_for_the_Responses_API_is_a_400_that_names_the_field_and_the_provider()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            Body(OpenAIProviderNames.Responses, "seed"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await TraconTestHost.ReadJsonAsync(response);
        var detail = problem.GetProperty("detail").GetString().ShouldNotBeNull();

        detail.ShouldContain("ModelBinding.Seed", Case.Sensitive);
        detail.ShouldContain("'openai-responses' provider", Case.Sensitive);
        detail.ShouldContain("openai", Case.Sensitive);

        using var stored = await host.Client.GetAsync(new Uri("/tracon/api/agents/field-probe", UriKind.Relative));

        stored.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Anthropic SDK 13 has no member for temperature, top-p or top-k. A definition that sets one used to run with
    /// the field silently gone; now it is not stored, and the answer names the field and the provider.
    /// </summary>
    [Theory]
    [InlineData("temperature", "Temperature")]
    [InlineData("topP", "TopP")]
    [InlineData("topK", "TopK")]
    public async Task Saving_a_sampling_field_the_Anthropic_SDK_cannot_send_is_a_400_that_names_the_field_and_the_provider(string field, string member)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            Body(AnthropicProviderNames.Anthropic, field));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString().ShouldNotBeNull();

        detail.ShouldContain($"ModelBinding.{member}", Case.Sensitive);
        detail.ShouldContain("'anthropic' provider", Case.Sensitive);

        using var stored = await host.Client.GetAsync(new Uri("/tracon/api/agents/field-probe", UriKind.Relative));

        stored.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>A fallback link receives the primary call's request options, so it is held to the same support.</summary>
    [Fact]
    public async Task A_fallback_link_that_would_drop_a_field_is_reported_with_the_links_provider()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new
            {
                name = "field-probe",
                instructions = "Answer briefly.",
                model = new
                {
                    provider = OpenAIProviderNames.ChatCompletions,
                    model = "gpt-4o-mini",
                    seed = 7,
                    fallbacks = new[] { new { provider = AnthropicProviderNames.Anthropic, model = "claude-sonnet-5" } },
                },
            });

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var error = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();
        var message = error.GetProperty("message").GetString().ShouldNotBeNull();

        message.ShouldContain("'anthropic' provider", Case.Sensitive);
        message.ShouldNotContain("'openai' provider", Case.Sensitive);
    }

    /// <summary>
    /// The same holds when Anthropic is only the fallback: the link receives the primary call's temperature, and the
    /// SDK drops it there too.
    /// </summary>
    [Fact]
    public async Task A_temperature_that_only_an_Anthropic_fallback_link_would_drop_is_reported_with_the_links_provider()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents/validate", UriKind.Relative),
            new
            {
                name = "field-probe",
                instructions = "Answer briefly.",
                model = new
                {
                    provider = OpenAIProviderNames.ChatCompletions,
                    model = "gpt-4o-mini",
                    temperature = 0.2,
                    fallbacks = new[] { new { provider = AnthropicProviderNames.Anthropic, model = "claude-sonnet-5" } },
                },
            });

        var body = await TraconTestHost.ReadJsonAsync(response);

        body.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var error = body.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        error.GetProperty("path").GetString().ShouldBe("model.temperature");
        error.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain("'anthropic' provider", Case.Sensitive);
    }

    /// <summary>
    /// A provider that implements no <c>ISamplingSupport</c> keeps sending temperature and top-p, as every adapter did before
    /// the two were named; the other fields stay unsupported for it.
    /// </summary>
    [Fact]
    public async Task A_provider_that_states_no_support_is_read_as_sending_temperature_and_top_p_only()
    {
        await using var host = await TraconTestHost.StartAsync(configureTracon: static builder => builder.AddModelProvider(new UndeclaringProvider()));

        foreach (var (field, accepted) in new[] { ("temperature", true), ("topP", true), ("seed", false) })
        {
            using var response = await ValidateAsync(host, UndeclaringProvider.ProviderName, field);

            var body = await TraconTestHost.ReadJsonAsync(response);

            body.GetProperty("valid").GetBoolean().ShouldBe(accepted, $"{field}: {body}");
        }
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(configureTracon: static builder => builder
            .UseOpenAI(TestKey)
            .UseOpenAICompatible(CompatibleProvider, static options =>
            {
                options.Endpoint = new Uri("http://127.0.0.1:9/v1");
                options.EnableResponsesSurface = true;
            })
            .UseAnthropic(TestKey, static options => options.DefaultModel = "claude-sonnet-5")
            .UseGoogle(TestKey, static options => options.DefaultModel = "gemini-3.6-flash")
            .UseAzureOpenAI(new Uri("https://test-resource.openai.azure.com/"), TestKey, static options => options.DefaultDeployment = "production-gpt"));

    private static Task<HttpResponseMessage> ValidateAsync(TraconTestHost host, string provider, string field)
        => host.Client.PostAsJsonAsync(new Uri("/tracon/api/agents/validate", UriKind.Relative), Body(provider, field));

    /// <summary>A definition that sets exactly <paramref name="field"/> on <paramref name="provider"/>.</summary>
    private static Dictionary<string, object> Body(string provider, string field)
    {
        var model = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["provider"] = provider,
            ["model"] = "gpt-4o-mini",
        };

        model[field] = field switch
        {
            "seed" => 7,
            "topK" => 40,
            "stopSequences" => new[] { "END" },
            "frequencyPenalty" => 0.5,
            "presencePenalty" => 0.5,
            "toolMode" => "auto",
            "allowMultipleToolCalls" => false,
            "reasoningOutput" => "Summary",
            "temperature" => 0.5,
            "topP" => 0.5,
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        return new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["name"] = "field-probe",
            ["instructions"] = "Answer briefly.",
            ["model"] = model,
        };
    }

    /// <summary>A third-party provider written before <c>ISamplingSupport</c> existed.</summary>
    private sealed class UndeclaringProvider : IModelProvider
    {
        public const string ProviderName = "undeclaring";

        public string Name => ProviderName;

        public IReadOnlyList<ModelDescriptor> Models { get; } = [new ModelDescriptor { Name = "gpt-4o-mini" }];

        public IChatClient CreateChatClient(ModelBinding binding) => new AnsweringChatClient();
    }

    /// <summary>A client that answers every request; the validation path builds one, no test sends a request through it.</summary>
    private sealed class AnsweringChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
