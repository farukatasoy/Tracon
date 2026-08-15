using System.Globalization;
using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// <c>/api/models/health</c> uclarini, <c>/api/models</c>'in durum alanini ve devre
/// kesicinin saglik ucuna yansimasini dogrular.
/// </summary>
/// <remarks>
/// Ollama bu makinede kurulu degil; F-05'in yerel/anahtarsiz baglanti mekanizmasi
/// <see cref="FakeOpenAiCompatibleServer"/> ile gercek bir soket uzerinden
/// dogrulanir. Gercek OpenAI/OpenRouter cagrisi yapan test <strong>yoktur</strong> —
/// ag gerektiren dogrulama elle yapilir (Faz 3'teki ayni kararla tutarli:
/// <c>docs/03-SAGLAYICI-VE-DERLEYICI.md</c>).
/// </remarks>
public sealed class ModelHealthEndpointsTests
{
    [Fact]
    public async Task Saglik_kontrolu_uygulamayan_saglayici_Unknown_doner()
    {
        // AgentPrismTestHost varsayilan olarak "echo" saglayicisini kaydeder;
        // o IModelProviderHealthCheck uygulamaz — bu bir hata degildir.
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/echo", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("status").GetString().ShouldBe("Unknown");
    }

    [Fact]
    public async Task Bilinmeyen_saglayici_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/yok-boyle", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Models_ucu_onbellekten_okur_ilk_cagriya_kadar_aga_gitmez()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress));

        // Henuz hic denetim yapilmadi: /api/models onbellekten okur, aga gitmez.
        using (var models = await host.Client.GetAsync(new Uri("/agentprism/api/models", UriKind.Relative)))
        {
            var body = await AgentPrismTestHost.ReadJsonAsync(models);
            body.EnumerateArray()
                .Single(static p => string.Equals(p.GetProperty("name").GetString(), "local-test", StringComparison.Ordinal))
                .GetProperty("status").GetString().ShouldBe("Unknown");
        }

        server.ModelsCallCount.ShouldBe(0);

        // Gercek bir denetim tetikle.
        using (var health = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative)))
        {
            var body = await AgentPrismTestHost.ReadJsonAsync(health);
            body.GetProperty("status").GetString().ShouldBe("Healthy");
            body.GetProperty("models").EnumerateArray().Select(static m => m.GetString())
                .ShouldContain(static m => string.Equals(m, "fake-local-model", StringComparison.Ordinal));
        }

        server.ModelsCallCount.ShouldBe(1);

        // /api/models artik onbellekten Healthy gorur, YINE aga gitmez.
        using (var modelsAfter = await host.Client.GetAsync(new Uri("/agentprism/api/models", UriKind.Relative)))
        {
            var body = await AgentPrismTestHost.ReadJsonAsync(modelsAfter);
            body.EnumerateArray()
                .Single(static p => string.Equals(p.GetProperty("name").GetString(), "local-test", StringComparison.Ordinal))
                .GetProperty("status").GetString().ShouldBe("Healthy");
        }

        server.ModelsCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Refresh_true_onbellegi_atlar_ve_yeniden_denetler()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress));

        using (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative)))
        {
        }

        server.ModelsCallCount.ShouldBe(1);

        using (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative)))
        {
        }

        server.ModelsCallCount.ShouldBe(1); // onbellekten dondu

        using (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test?refresh=true", UriKind.Relative)))
        {
        }

        server.ModelsCallCount.ShouldBe(2); // onbellek atlandi
    }

    [Fact]
    public async Task Sunucu_hata_dondurunce_Unhealthy_ve_detay_adres_sizdirmaz()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        server.ModelsStatusCode = StatusCodes.Status503ServiceUnavailable;

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        var detail = body.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldContain("503");
        detail.ShouldNotContain(server.BaseAddress.Host);
        detail.ShouldNotContain(server.BaseAddress.Port.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Baglanamayan_saglayicinin_detayinda_ne_anahtar_ne_adres_gorunur()
    {
        const string secret = "cok-gizli-openrouter-anahtari-DENEME";
        // Kapali/ayrilmis bir port (1): gercek bir baglanti reddi uretir.
        var deadEndpoint = new Uri("http://127.0.0.1:1");

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("dead", o =>
            {
                o.Endpoint = deadEndpoint;
                o.ApiKey = secret;
                o.Timeout = TimeSpan.FromSeconds(5);
            }));

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/dead", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        var detail = body.GetProperty("detail").GetString();
        detail.ShouldNotBeNull();
        detail.ShouldNotContain(secret);
        detail.ShouldNotContain("127.0.0.1");
        detail.ShouldNotContain(":1\"");

        var raw = await (await host.Client.GetAsync(new Uri("/agentprism/api/models/health/dead", UriKind.Relative)))
            .Content.ReadAsStringAsync();
        raw.ShouldNotContain(secret);
    }

    [Fact]
    public async Task Ardisik_hatada_devre_acilir_ve_saglik_ucu_bunu_yansitir()
    {
        await using var server = await FakeOpenAiCompatibleServer.StartAsync();
        // 400 (bilerek 500 DEGIL): System.ClientModel'in varsayilan yeniden deneme
        // ilkesi 5xx/408/429'u otomatik tekrar dener, bu da ham istek sayisini
        // ongorulemez hale getirirdi. 400 yeniden denenmez; sayim deterministik kalir.
        server.ChatCompletionStatusCode = StatusCodes.Status400BadRequest;

        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseOpenAICompatible("local-test", o => o.Endpoint = server.BaseAddress),
            configureServices: services => services.Configure<AgentPrismOptions>(options =>
            {
                options.CircuitBreaker.FailureThreshold = 2;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(5);
            }));

        var registry = host.Services.GetRequiredService<IModelProviderRegistry>();
        var binding = new ModelBinding { Provider = "local-test", Model = "fake-local-model" };
        ChatMessage[] messages = [new ChatMessage(ChatRole.User, "merhaba")];

        for (var i = 0; i < 2; i++)
        {
            using var chatClient = registry.CreateChatClient(binding);
            await Should.ThrowAsync<Exception>(() => chatClient.GetResponseAsync(messages));
        }

        // Esik asildi: ucuncu deneme saglayiciya HIC GITMEZ.
        using (var chatClient = registry.CreateChatClient(binding))
        {
            await Should.ThrowAsync<AgentPrismProviderUnavailableException>(() => chatClient.GetResponseAsync(messages));
        }

        server.ChatCompletionCallCount.ShouldBe(2);

        using var health = await host.Client.GetAsync(new Uri("/agentprism/api/models/health/local-test", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(health);

        body.GetProperty("status").GetString().ShouldBe("Unhealthy");
        var circuitDetail = body.GetProperty("detail").GetString();
        circuitDetail.ShouldNotBeNull();
        circuitDetail.ShouldContain("Circuit breaker");
    }
}
