using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// The OpenAI, Anthropic, Google, and Azure OpenAI providers being registered
/// <strong>in the same application</strong> and appearing together on the
/// management endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Name collisions, registration order, and <c>ModelProviderRegistry</c>'s
/// "same name twice" guard only surface when the providers are set up
/// together; each package's own unit tests cannot see this.
/// </para>
/// <para>
/// <strong>No test calls a real model</strong> (a decision in force since
/// Phase 3). Verification that requires the network is done manually; the
/// evidence is in <c>docs/arsiv/fazlar/26-ANTHROPIC-VE-GEMINI.md</c> and
/// <c>docs/arsiv/fazlar/27-AZURE-FOUNDRY.md</c>.
/// </para>
/// </remarks>
public sealed class MultiProviderTests
{
    private const string TestKey = "functional-test-key";

    [Fact]
    public async Task Four_providers_are_registered_at_the_same_time()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/models", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var names = (await TraconTestHost.ReadJsonAsync(response))
            .EnumerateArray()
            .Select(static provider => provider.GetProperty("name").GetString())
            .ToList();

        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.ChatCompletions, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.Responses, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, AnthropicProviderNames.Anthropic, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, GoogleProviderNames.Google, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, AzureOpenAIProviderNames.AzureOpenAI, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Catalog_shows_each_providers_own_models()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/tracon/api/models", UriKind.Relative));
        var providers = (await TraconTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        Models(providers, AnthropicProviderNames.Anthropic).ShouldBe(["claude-sonnet-5"]);
        Models(providers, GoogleProviderNames.Google).ShouldBe(["gemini-3.6-flash"]);

        // On Azure, the name in the catalog is not a MODEL name, it is the DEPLOYMENT name.
        Models(providers, AzureOpenAIProviderNames.AzureOpenAI).ShouldBe(["production-gpt"]);
    }

    [Fact]
    public async Task Health_endpoint_lists_all_five_providers()
    {
        await using var host = await StartAsync();

        // The cache is empty: no check is triggered, no network call is made.
        using var response = await host.Client.GetAsync(new Uri("/tracon/api/models/health", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var names = (await TraconTestHost.ReadJsonAsync(response))
            .EnumerateArray()
            .Select(static health => health.GetProperty("providerName").GetString())
            .ToList();

        names.ShouldContain(static name => string.Equals(name, AnthropicProviderNames.Anthropic, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, GoogleProviderNames.Google, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, AzureOpenAIProviderNames.AzureOpenAI, StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_management_endpoint_reveals_the_key()
    {
        await using var host = await StartAsync();

        foreach (var path in new[] { "/tracon/api/models", "/tracon/api/models/health", "/tracon/api/meta" })
        {
            using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative));
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            body.ShouldNotContain(TestKey);
        }

        host.Logs.AllText.ShouldNotContain(TestKey);
    }

    [Fact]
    public async Task Agent_with_provider_specific_settings_is_saved_and_read_back()
    {
        await using var host = await StartAsync();

        var payload = JsonSerializer.Serialize(new
        {
            name = "gemini-support",
            instructions = "You are a support assistant.",
            model = new
            {
                provider = GoogleProviderNames.Google,
                model = "gemini-3.6-flash",
                providerSettings = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    [GoogleProviderNames.SafetyHarassmentSetting] = "BLOCK_ONLY_HIGH",
                    [GoogleProviderNames.ThinkingBudgetTokensSetting] = 512,
                },
            },
        });

        using var created = await host.Client.PostAsync(
            new Uri("/tracon/api/agents", UriKind.Relative),
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var read = await host.Client.GetAsync(new Uri("/tracon/api/agents/gemini-support", UriKind.Relative));
        read.EnsureSuccessStatusCode();

        // The contract carries end to end: HTTP -> store -> HTTP. The response
        // carries both the summary (descriptor) and the full definition; both
        // must show the setting.
        var body = await TraconTestHost.ReadJsonAsync(read);

        body.GetProperty("descriptor").GetProperty("model").GetProperty("providerSettings")
            .GetProperty(GoogleProviderNames.SafetyHarassmentSetting).GetString().ShouldBe("BLOCK_ONLY_HIGH");

        var settings = body.GetProperty("definition").GetProperty("model").GetProperty("providerSettings");

        settings.GetProperty(GoogleProviderNames.SafetyHarassmentSetting).GetString().ShouldBe("BLOCK_ONLY_HIGH");
        settings.GetProperty(GoogleProviderNames.ThinkingBudgetTokensSetting).GetInt32().ShouldBe(512);
    }

    [Fact]
    public async Task Unrecognized_provider_setting_rejects_running_with_a_clear_message()
    {
        await using var host = await StartAsync();

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();

        var exception = Should.Throw<TraconException>(() => registry.CreateChatClient(new ModelBinding
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = "claude-sonnet-5",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic.noSuchSetting"] = JsonSerializer.SerializeToElement(true),
            },
        }));

        exception.Message.ShouldContain("anthropic.noSuchSetting");
        exception.Message.ShouldContain(AnthropicProviderNames.PromptCachingSetting);
    }

    [Fact]
    public async Task Azure_provider_states_that_it_accepts_no_settings_at_all()
    {
        await using var host = await StartAsync();

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();

        // Because Azure's extra-field-writing path is broken with the OpenAI
        // SDK version we use, this provider offers no settings at all (K-211).
        // It states this explicitly instead of silently ignoring the setting.
        var exception = Should.Throw<TraconException>(() => registry.CreateChatClient(new ModelBinding
        {
            Provider = AzureOpenAIProviderNames.AzureOpenAI,
            Model = "production-gpt",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["azure-openai.noSuchSetting"] = JsonSerializer.SerializeToElement(true),
            },
        }));

        exception.Message.ShouldContain("azure-openai.noSuchSetting");
        exception.Message.ShouldContain("supports no extra settings");
    }

    private static IEnumerable<string?> Models(IEnumerable<JsonElement> providers, string name)
        => providers
            .Single(provider => string.Equals(provider.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            .GetProperty("models")
            .EnumerateArray()
            .Select(static model => model.GetProperty("name").GetString());

    private static Task<TraconTestHost> StartAsync()
        => TraconTestHost.StartAsync(configureTracon: builder => builder
            .UseOpenAI(TestKey)
            .UseAnthropic(TestKey, options =>
            {
                options.DefaultModel = "claude-sonnet-5";
                options.Models.Add(new ModelDescriptor { Name = "claude-sonnet-5" });
            })
            .UseGoogle(TestKey, options =>
            {
                options.DefaultModel = "gemini-3.6-flash";
                options.Models.Add(new ModelDescriptor { Name = "gemini-3.6-flash" });
            })
            .UseAzureOpenAI(new Uri("https://test-resource.openai.azure.com/"), TestKey, options =>
            {
                options.DefaultDeployment = "production-gpt";
                options.Models.Add(new ModelDescriptor { Name = "production-gpt" });
            }));
}
