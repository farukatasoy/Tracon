using System.Net;
using System.Net.Http.Json;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The value ranges the shipped providers state (<see cref="ISamplingSupport.SamplingLimits"/>), as the validation
/// endpoint and the save path report them. A value the provider would refuse, or silently change, is an error that names
/// the field, the value and the provider.
/// </summary>
/// <remarks>
/// <para>
/// Measured on 2026-10-27. Google: the Gemini API takes a 32-bit seed (<c>3000000000</c> is a <c>400</c>) and at most five
/// stop sequences (a sixth is a <c>400</c>), and the Google.GenAI adapter wraps a seed outside the 32-bit range into another
/// number (<c>GoogleSamplingLimitsTests</c> reads the wire). OpenAI Chat Completions and Azure OpenAI: four stop sequences
/// (documented; a real <c>400</c> could not be provoked because every model of the measuring account rejects <c>stop</c> first).
/// Anthropic: no count limit (the Messages API bounds the total length of the stop sequences instead). A server at another
/// address than OpenAI's states none.
/// </para>
/// <para>
/// The providers are the real ones, built without a network call, so a row fails if the provider package states something
/// other than the measured limit.
/// </para>
/// </remarks>
public sealed class ModelSamplingLimitsTests
{
    private const string TestKey = "functional-test-key";
    private const string CompatibleProvider = "compatible-test";

    private static readonly Uri ValidateUri = new("/tracon/api/agents/validate", UriKind.Relative);

    // provider, seed (null = unset), number of stop sequences (0 = unset), the field path the report names (null = clean)
    public static TheoryData<string, long?, int, string?> Cases() => new()
    {
        { OpenAIProviderNames.ChatCompletions, null, 4, null },
        { OpenAIProviderNames.ChatCompletions, null, 5, "model.stopSequences" },
        { OpenAIProviderNames.ChatCompletions, 3_000_000_000, 0, null },
        { OpenAIProviderNames.ChatCompletions, long.MinValue, 0, null },
        { CompatibleProvider, null, 5, null },
        { CompatibleProvider, 3_000_000_000, 0, null },
        { AzureOpenAIProviderNames.AzureOpenAI, null, 4, null },
        { AzureOpenAIProviderNames.AzureOpenAI, null, 5, "model.stopSequences" },
        { AzureOpenAIProviderNames.AzureOpenAI, 3_000_000_000, 0, null },
        { AnthropicProviderNames.Anthropic, null, 50, null },
        { GoogleProviderNames.Google, null, 5, null },
        { GoogleProviderNames.Google, null, 6, "model.stopSequences" },
        { GoogleProviderNames.Google, int.MaxValue, 0, null },
        { GoogleProviderNames.Google, int.MinValue, 0, null },
        { GoogleProviderNames.Google, 3_000_000_000, 0, "model.seed" },
        { GoogleProviderNames.Google, -2_147_483_649, 0, "model.seed" },
    };

    /// <param name="provider">The provider name.</param>
    /// <param name="seed">The seed the definition sets, or <see langword="null"/>.</param>
    /// <param name="stops">The number of stop sequences the definition sets.</param>
    /// <param name="flagged">The path the report names, or <see langword="null"/> when the definition is valid.</param>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_validation_report_agrees_with_the_limits_each_provider_states(string provider, long? seed, int stops, string? flagged)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(ValidateUri, Definition(provider, seed, stops));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var report = await TraconTestHost.ReadJsonAsync(response);

        if (flagged is null)
        {
            report.GetProperty("valid").GetBoolean().ShouldBeTrue(report.ToString());
            report.GetProperty("messages").GetArrayLength().ShouldBe(0);

            return;
        }

        report.GetProperty("valid").GetBoolean().ShouldBeFalse();

        var finding = report.GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        finding.GetProperty("code").GetString().ShouldBe("sampling_value_out_of_range");
        finding.GetProperty("path").GetString().ShouldBe(flagged);
        finding.GetProperty("severity").GetString().ShouldBe("Error");
        finding.GetProperty("message").GetString().ShouldNotBeNull().ShouldContain($"'{provider}' provider", Case.Sensitive);
    }

    [Fact]
    public async Task The_message_names_the_value_and_the_range()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(ValidateUri, Definition(GoogleProviderNames.Google, 3_000_000_000, 0));

        var message = (await TraconTestHost.ReadJsonAsync(response))
            .GetProperty("messages").EnumerateArray().ShouldHaveSingleItem()
            .GetProperty("message").GetString().ShouldNotBeNull();

        message.ShouldContain("ModelBinding.Seed is 3000000000", Case.Sensitive);
        message.ShouldContain("only values from -2147483648 to 2147483647", Case.Sensitive);
    }

    /// <summary>The save path runs the same check: the agent is not stored, and the answer names the field and the value.</summary>
    [Fact]
    public async Task Saving_a_seed_outside_the_range_is_a_400_that_names_the_field_and_the_provider()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            Definition(GoogleProviderNames.Google, 3_000_000_000, 0));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var detail = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("detail").GetString().ShouldNotBeNull();

        detail.ShouldContain("ModelBinding.Seed", Case.Sensitive);
        detail.ShouldContain("3000000000", Case.Sensitive);
        detail.ShouldContain("'google' provider", Case.Sensitive);

        using var stored = await host.Client.GetAsync(new Uri("/tracon/api/agents/range-probe", UriKind.Relative));

        stored.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>A fallback link receives the primary call's request options, so its provider's range holds for the definition.</summary>
    [Fact]
    public async Task A_fallback_link_on_a_provider_that_would_wrap_the_seed_is_reported_with_the_links_provider()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            ValidateUri,
            new
            {
                name = "range-probe",
                instructions = "Answer briefly.",
                model = new
                {
                    provider = OpenAIProviderNames.ChatCompletions,
                    model = "gpt-4o-mini",
                    seed = 3_000_000_000,
                    fallbacks = new[] { new { provider = GoogleProviderNames.Google, model = "gemini-3.6-flash" } },
                },
            });

        var finding = (await TraconTestHost.ReadJsonAsync(response)).GetProperty("messages").EnumerateArray().ShouldHaveSingleItem();

        finding.GetProperty("code").GetString().ShouldBe("sampling_value_out_of_range");

        var message = finding.GetProperty("message").GetString().ShouldNotBeNull();

        message.ShouldContain("'google' provider", Case.Sensitive);
        message.ShouldNotContain("'openai' provider", Case.Sensitive);
    }

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(configureTracon: static builder => builder
            .UseOpenAI(TestKey)
            .UseOpenAICompatible(CompatibleProvider, static options => options.Endpoint = new Uri("http://127.0.0.1:9/v1"))
            .UseAnthropic(TestKey, static options => options.DefaultModel = "claude-sonnet-5")
            .UseGoogle(TestKey, static options => options.DefaultModel = "gemini-3.6-flash")
            .UseAzureOpenAI(new Uri("https://test-resource.openai.azure.com/"), TestKey, static options => options.DefaultDeployment = "production-gpt"));

    /// <summary>A definition on <paramref name="provider"/> that sets the seed and the stop sequences given.</summary>
    private static Dictionary<string, object?> Definition(string provider, long? seed, int stops)
        => new(StringComparer.Ordinal)
        {
            ["name"] = "range-probe",
            ["instructions"] = "Answer briefly.",
            ["model"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["provider"] = provider,
                ["model"] = "gpt-4o-mini",
                ["seed"] = seed,
                ["stopSequences"] = stops == 0 ? null : Enumerable.Range(0, stops).Select(static index => $"stop-{index}").ToArray(),
            },
        };
}
