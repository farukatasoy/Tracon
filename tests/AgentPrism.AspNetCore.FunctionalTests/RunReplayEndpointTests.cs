using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

// 🚨 `using AgentPrism.Testing;` YAZILMAZ: paketteki AgentPrismTestHost ile bu
// projenin kendi (TestServer tabanli) AgentPrismTestHost'u AYNI ada sahiptir ve
// ikisi birden goruldugunde CS0104 verir (K-269). Tek gereken tip takma adla
// alinir.
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// Yeniden oynatma, girdi ve karsilastirma uclarinin testleri (Faz 47).
/// </summary>
public sealed class RunReplayEndpointTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";
    private const string AgentName = "oynatilabilir";
    private const string ModelId = "echo-1";

    [Fact]
    public async Task Kayitli_girdi_polimorfik_icerigiyle_geri_okunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedAsync(
            host,
            [
                new ChatMessage(
                    ChatRole.User,
                    [
                        new TextContent("bu goruntuyu acikla"),
                        new UriContent("https://ornek/gorsel.png", "image/png"),
                    ]),
            ]);

        using var response = await host.Client.GetAsync(InputUri(runId));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        var contents = body.GetProperty("messages")[0].GetProperty("contents");

        contents.GetArrayLength().ShouldBe(2);
        contents[0].GetProperty("text").GetString().ShouldBe("bu goruntuyu acikla");
        contents[1].GetProperty("mediaType").GetString().ShouldBe("image/png");
    }

    [Fact]
    public async Task Girdi_kaydi_olmayan_calistirma_oynatilamaz_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        // Girdi YAZILMADAN acilan bir calistirma: RecordRunInput kapaliyken
        // baslamis veya saklama politikasiyla silinmis bir satirin karsiligi.
        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = AgentName,
            StartedAt = DateTimeOffset.UtcNow,
        });

        using var input = await host.Client.GetAsync(InputUri(runId));
        using var replay = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });

        input.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        replay.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Yeniden_oynatma_yeni_calistirma_acar_ve_soy_bagi_tasir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "merhaba")]);

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        var replayId = body.GetProperty("runId").GetGuid();

        body.GetProperty("sourceRunId").GetGuid().ShouldBe(runId);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var replayed = await runs.GetRunAsync(replayId);
        var source = await runs.GetRunAsync(runId);

        replayed.ShouldNotBeNull();
        replayed!.ReplayOfRunId.ShouldBe(runId);

        // 🚨 Kaynak calistirma DEGISMEZ: soy bagi tek yonludur.
        source!.ReplayOfRunId.ShouldBeNull();
    }

    [Fact]
    public async Task ReplayTools_modunda_HICBIR_tool_gercekten_kosmaz()
    {
        ReplayProbeTools.Reset();

        await using var host = await AgentPrismTestHost.StartAsync(ConfigureToolAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "ORD-7 nerede")]);

        await RecordToolCallAsync(host, runId, "get_order_status", "orderId=ORD-7", "kargoda");

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Tool govdesi CALISMADI ama model tool'u gordu ve kayitli sonucu aldi.
        ReplayProbeTools.Calls.ShouldBe(0);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("output").GetString().ShouldNotBeNull().ShouldContain("kargoda");
    }

    [Fact]
    public async Task Eslesmeyen_tool_cagrisinda_422_doner_ve_tool_adini_yazar()
    {
        ReplayProbeTools.Reset();

        await using var host = await AgentPrismTestHost.StartAsync(ConfigureToolAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "ORD-7 nerede")]);

        // Kayitli sonuc BASKA bir argumana ait; model ORD-7 ile cagiracak.
        await RecordToolCallAsync(host, runId, "get_order_status", "orderId=ORD-9", "teslim edildi");

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        body.GetProperty("toolName").GetString().ShouldBe("get_order_status");
        body.GetProperty("arguments").GetString().ShouldBe("orderId=ORD-7");

        // 🚨 Sessizce atlanmadi ve canli calistirilmadi.
        ReplayProbeTools.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Onay_gerektiren_tool_LiveTools_ile_409_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureApprovalAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "siparisi iptal et")]);

        using var live = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "LiveTools" });
        using var replayed = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });

        live.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Ayni agent yan etkisiz bir modda oynatilabilir kalmalidir.
        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Kalici_tanimi_olmayan_agent_bindirmeyle_oynatilamaz_400_doner()
    {
        // Kod agent'inin AgentDefinition karsiligi yoktur; model bindirmesi ve
        // tool modlari yeniden derlemeyi gerektirir ve sessizce LiveTools'a
        // dusmek K1'e aykiridir.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition("kod-agent")));

        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "merhaba")], agentName: "kod-agent");

        using var response = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "ReplayTools" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Baska_kiracinin_calistirmasi_oynatilamaz_AYNI_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(builder =>
        {
            ConfigureAgent(builder);
            builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            });
        });

        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "merhaba")], tenantId: "kiraci-a");

        using var missing = await SendAsTenant(host, ReplayUri(AgentPrismId.NewId()), "kiraci-b");
        using var wrongTenant = await SendAsTenant(host, ReplayUri(runId), "kiraci-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var missingBody = await AgentPrismTestHost.ReadJsonAsync(missing);
        var wrongBody = await AgentPrismTestHost.ReadJsonAsync(wrongTenant);

        missingBody.GetProperty("title").GetString().ShouldBe(wrongBody.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Iki_calistirma_yan_yana_karsilastirilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(ConfigureAgent);
        var runId = await SeedAsync(host, [new ChatMessage(ChatRole.User, "merhaba")]);

        using var replayResponse = await host.Client.PostAsJsonAsync(ReplayUri(runId), new { toolMode = "NoTools" });
        var replayId = (await AgentPrismTestHost.ReadJsonAsync(replayResponse)).GetProperty("runId").GetGuid();

        using var compare = await host.Client.GetAsync(
            new Uri($"/agentprism/api/runs/{runId}/compare/{replayId}", UriKind.Relative));

        compare.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(compare);

        body.GetProperty("left").GetProperty("runId").GetGuid().ShouldBe(runId);
        body.GetProperty("right").GetProperty("runId").GetGuid().ShouldBe(replayId);
        body.GetProperty("right").GetProperty("replayOfRunId").GetGuid().ShouldBe(runId);
        body.GetProperty("right").GetProperty("output").GetString().ShouldNotBeNullOrEmpty();
    }

    private static void ConfigureAgent(IAgentPrismBuilder builder)
        => SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Kisa yanit ver.",
            Model = TestData.Model(),
        });

    private static void ConfigureToolAgent(IAgentPrismBuilder builder)
    {
        builder
            .AddModelProvider(new FakeModelProvider("tool-echo")
                .CallsTool("get_order_status", new { orderId = "ORD-7" })
                .EchoesLastToolResult())
            .AddToolsFrom(typeof(ReplayProbeTools));

        SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Kisa yanit ver.",
            Model = new ModelBinding { Provider = "tool-echo", Model = ModelId },
            ToolNames = ["get_order_status"],
        });
    }

    private static void ConfigureApprovalAgent(IAgentPrismBuilder builder)
    {
        builder.AddToolsFrom(typeof(ReplayProbeTools));

        SaveDefinition(builder, new AgentDefinition
        {
            Name = AgentName,
            Instructions = "Kisa yanit ver.",
            Model = TestData.Model(),
            ToolNames = ["cancel_order"],
        });
    }

    /// <summary>
    /// Tanimi <em>veritabani</em> kaynakli olarak kaydeder.
    /// </summary>
    /// <remarks>
    /// Yeniden oynatma tanimi yeniden derler; <c>AddAgent</c> ile eklenen kod
    /// agent'inin tanimi katalogda vardir ama <see cref="IAgentDefinitionStore"/>
    /// icinde YOKTUR ve bindirme uygulanamaz.
    /// </remarks>
    private static void SaveDefinition(IAgentPrismBuilder builder, AgentDefinition definition)
        => builder.Services.AddSingleton<IStartupSeed>(new StartupSeed(definition));

    private static async ValueTask<Guid> SeedAsync(
        AgentPrismTestHost host,
        IReadOnlyList<ChatMessage> messages,
        string agentName = AgentName,
        string? tenantId = null)
    {
        await SeedDefinitionsAsync(host);

        var runs = host.Services.GetRequiredService<IRunStore>();
        var inputs = host.Services.GetRequiredService<IRunInputStore>();
        var tenants = host.Services.GetRequiredService<ITenantContext>();
        var tenant = tenantId ?? tenants.TenantId;
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = agentName,
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = tenant,
            ModelId = ModelId,
        });

        await runs.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
        });

        await inputs.SaveAsync(new RunInputRecord
        {
            RunId = runId,
            TenantId = tenant,
            Messages = messages,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        return runId;
    }

    private static async ValueTask SeedDefinitionsAsync(AgentPrismTestHost host)
    {
        var store = host.Services.GetRequiredService<IAgentDefinitionStore>();

        foreach (var seed in host.Services.GetServices<IStartupSeed>())
        {
            await store.SaveAsync(seed.Definition);
        }
    }

    private static async ValueTask RecordToolCallAsync(
        AgentPrismTestHost host,
        Guid runId,
        string toolName,
        string arguments,
        string result)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();

        await runs.RecordToolInvocationAsync(new ToolInvocationRecord
        {
            Id = AgentPrismId.NewId(),
            RunId = runId,
            ToolName = toolName,
            Arguments = arguments,
            Result = result,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static async Task<HttpResponseMessage> SendAsTenant(
        AgentPrismTestHost host,
        Uri uri,
        string tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new { toolMode = "NoTools" }),
        };

        request.Headers.Add(TenantHeader, tenantId);

        return await host.Client.SendAsync(request);
    }

    private static Uri ReplayUri(Guid runId) => new($"/agentprism/api/runs/{runId}/replay", UriKind.Relative);

    private static Uri InputUri(Guid runId) => new($"/agentprism/api/runs/{runId}/input", UriKind.Relative);

    /// <summary>Kurulum sirasinda kaydedilecek tanimlari tasiyan isaret.</summary>
    private interface IStartupSeed
    {
        AgentDefinition Definition { get; }
    }

    private sealed record StartupSeed(AgentDefinition Definition) : IStartupSeed;
}

/// <summary>
/// Yeniden oynatma testlerinin tool'lari. Govdenin GERCEKTEN calisip
/// calismadigini sayarak olcer.
/// </summary>
internal static class ReplayProbeTools
{
    private static int _calls;

    /// <summary>Tool govdesinin kac kez calistigi.</summary>
    public static int Calls => Volatile.Read(ref _calls);

    /// <summary>Sayaci sifirlar.</summary>
    public static void Reset() => Volatile.Write(ref _calls, 0);

    /// <summary>Bir siparisin durumunu dondurur.</summary>
    /// <param name="orderId">Siparis kimligi.</param>
    /// <returns>Durum metni.</returns>
    [AgentPrismTool("get_order_status", "Bir siparisin kargo durumunu dondurur.")]
    public static string GetOrderStatus(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId}: CANLI CALISTI";
    }

    /// <summary>Bir siparisi iptal eder. Onay ister.</summary>
    /// <param name="orderId">Siparis kimligi.</param>
    /// <returns>Sonuc metni.</returns>
    [AgentPrismTool("cancel_order", "Bir siparisi iptal eder.", RequiresApproval = true)]
    public static string CancelOrder(string orderId)
    {
        Interlocked.Increment(ref _calls);

        return $"{orderId} iptal edildi.";
    }
}
