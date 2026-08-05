using System.Net;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// OpenAI, Anthropic ve Google saglayicilarinin <strong>ayni uygulamada</strong>
/// kayitli olmasi ve yonetim uclarinda birlikte gorunmesi.
/// </summary>
/// <remarks>
/// <para>
/// Ad cakismasi, kayit sirasi ve <c>ModelProviderRegistry</c>'nin "ayni ad iki kez"
/// korumasi yalnizca saglayicilar birlikte kurulunca ortaya cikar; her paketin
/// kendi birim testi bunu goremez.
/// </para>
/// <para>
/// <strong>Gercek model cagrisi yapan test yoktur</strong> (Faz 3'ten beri gecerli
/// karar). Ag gerektiren dogrulama elle yapilir; kaniti
/// <c>docs/26-ANTHROPIC-VE-GEMINI.md</c> icindedir.
/// </para>
/// </remarks>
public sealed class MultiProviderTests
{
    private const string TestKey = "fonksiyonel-test-anahtari";

    [Fact]
    public async Task Uc_saglayici_ayni_anda_kayitli_olur()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var names = (await AgentPrismTestHost.ReadJsonAsync(response))
            .EnumerateArray()
            .Select(static provider => provider.GetProperty("name").GetString())
            .ToList();

        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.ChatCompletions, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, OpenAIProviderNames.Responses, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, AnthropicProviderNames.Anthropic, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, GoogleProviderNames.Google, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Katalog_her_saglayici_icin_kendi_modellerini_gosterir()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models", UriKind.Relative));
        var providers = (await AgentPrismTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();

        Models(providers, AnthropicProviderNames.Anthropic).ShouldBe(["claude-sonnet-5"]);
        Models(providers, GoogleProviderNames.Google).ShouldBe(["gemini-3.6-flash"]);
    }

    [Fact]
    public async Task Saglik_ucu_dort_saglayiciyi_da_listeler()
    {
        await using var host = await StartAsync();

        // Onbellek bos: hicbir denetim tetiklenmez, aga cikilmaz.
        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var names = (await AgentPrismTestHost.ReadJsonAsync(response))
            .EnumerateArray()
            .Select(static health => health.GetProperty("providerName").GetString())
            .ToList();

        names.ShouldContain(static name => string.Equals(name, AnthropicProviderNames.Anthropic, StringComparison.Ordinal));
        names.ShouldContain(static name => string.Equals(name, GoogleProviderNames.Google, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Yonetim_uclarinin_hicbirinde_anahtar_gorunmez()
    {
        await using var host = await StartAsync();

        foreach (var path in new[] { "/agentprism/api/models", "/agentprism/api/models/health", "/agentprism/api/meta" })
        {
            using var response = await host.Client.GetAsync(new Uri(path, UriKind.Relative));
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            body.ShouldNotContain(TestKey);
        }

        host.Logs.AllText.ShouldNotContain(TestKey);
    }

    [Fact]
    public async Task Saglayiciya_ozgu_ayarli_agent_kaydedilir_ve_geri_okunur()
    {
        await using var host = await StartAsync();

        var payload = JsonSerializer.Serialize(new
        {
            name = "gemini-destek",
            instructions = "Sen bir destek asistanisin.",
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
            new Uri("/agentprism/api/agents", UriKind.Relative),
            new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));

        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var read = await host.Client.GetAsync(new Uri("/agentprism/api/agents/gemini-destek", UriKind.Relative));
        read.EnsureSuccessStatusCode();

        // Sozlesme uctan uca tasinir: HTTP -> depo -> HTTP. Yanit hem ozeti
        // (descriptor) hem tam tanimi (definition) tasir; ikisi de ayari gostermelidir.
        var body = await AgentPrismTestHost.ReadJsonAsync(read);

        body.GetProperty("descriptor").GetProperty("model").GetProperty("providerSettings")
            .GetProperty(GoogleProviderNames.SafetyHarassmentSetting).GetString().ShouldBe("BLOCK_ONLY_HIGH");

        var settings = body.GetProperty("definition").GetProperty("model").GetProperty("providerSettings");

        settings.GetProperty(GoogleProviderNames.SafetyHarassmentSetting).GetString().ShouldBe("BLOCK_ONLY_HIGH");
        settings.GetProperty(GoogleProviderNames.ThinkingBudgetTokensSetting).GetInt32().ShouldBe(512);
    }

    [Fact]
    public async Task Taninmayan_saglayici_ayari_calistirmayi_anlasilir_bicimde_reddeder()
    {
        await using var host = await StartAsync();

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();

        var exception = Should.Throw<AgentPrismException>(() => registry.CreateChatClient(new ModelBinding
        {
            Provider = AnthropicProviderNames.Anthropic,
            Model = "claude-sonnet-5",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["anthropic.yokBoyleAyar"] = JsonSerializer.SerializeToElement(true),
            },
        }));

        exception.Message.ShouldContain("anthropic.yokBoyleAyar");
        exception.Message.ShouldContain(AnthropicProviderNames.PromptCachingSetting);
    }

    private static IEnumerable<string?> Models(IEnumerable<JsonElement> providers, string name)
        => providers
            .Single(provider => string.Equals(provider.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            .GetProperty("models")
            .EnumerateArray()
            .Select(static model => model.GetProperty("name").GetString());

    private static Task<AgentPrismTestHost> StartAsync()
        => AgentPrismTestHost.StartAsync(configureAgentPrism: builder => builder
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
            }));
}
