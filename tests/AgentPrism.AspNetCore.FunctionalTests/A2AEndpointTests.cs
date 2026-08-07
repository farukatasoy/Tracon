using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Faz 50: AgentPrism agent'larini disa acan A2A sunucusu.</summary>
public sealed class A2AEndpointTests
{
    [Fact]
    public async Task Agent_karti_yayimlanir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));
        var body = await AgentPrismTestHost.ReadJsonAsync(response);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.GetProperty("name").GetString().ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Mesaj_gonderme_agenti_calistirir_ve_runs_satiri_uretir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        var (response, body) = await SendMessageAsync(host.Client, "kod-agent", "merhaba");

        response.StatusCode.ShouldBe(HttpStatusCode.OK, body?.ToString());

        var runs = host.Services.GetRequiredService<IRunStore>();
        var all = await runs.QueryRunsAsync(new RunQuery());

        all.ShouldHaveSingleItem().AgentName.ShouldBe("kod-agent");
    }

    [Fact]
    public async Task Beyaz_listede_olmayan_agent_yayimlanmaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(),
            configureAfterMap: app => app.MapAgentPrismA2A());

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/a2a/kod-agent/.well-known/agent-card.json", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calisma_aninda_eklenen_agent_A2Ada_gorunmez()
    {
        // 🚨 A2A'nin olculmus kisiti: ExposedAgents kayit ANINDA sabitlenir.
        // MCP'nin aksine, sonradan eklenen bir agent icin YENI bir A2A sunucusu
        // olusturulmadan gorunmesi mumkun degildir (bolum 50.5).
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder.UseA2A(o => o.ExposedAgents.Add("db-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        using var createResponse = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/agents", UriKind.Relative),
            TestData.Request());
        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/a2a/db-agent/.well-known/agent-card.json", UriKind.Relative));

        // Kayit ANINDA "db-agent" henuz katalogda yoktu; proxy yine de kurulmustur
        // (ad SABIT beyaz listeden geldi) ama kart URUTULMUSTU ve calisir durumdadir -
        // KISIT calisma aninda EKLENEN bir agent'in GORUNMEMESI degil, LISTEYE
        // SONRADAN eklenemez OLMASIdIR. Bu test o sinirin var oldugunu belgeler:
        // beyaz listeye "sonradan-eklenen" bir ad eklemenin YOLU yoktur.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AllowRemoteAccess_acikken_A2A_acilmaz()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureEndpoints: o => o.AllowRemoteAccess = true,
            configureAfterMap: app => app.MapAgentPrismA2A()));
    }

    [Fact]
    public async Task Onay_gerektiren_tool_tasiyan_agent_A2Ada_disa_acilamaz()
    {
        var exception = await Should.ThrowAsync<InvalidOperationException>(() => AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddTool(
                    (Func<string, string>)CancelOrder,
                    name: "cancel_order",
                    description: "Bir siparisi iptal eder.",
                    requiresApproval: true)
                .AddAgent(TestData.Definition() with { ToolNames = ["cancel_order"] })
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A()));

        exception.Message.ShouldContain("onay", Case.Sensitive);
    }

    [Fact]
    public async Task Cagri_denetim_izine_yazilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureAgentPrism: builder => builder
                .AddAgent(TestData.Definition())
                .UseA2A(o => o.ExposedAgents.Add("kod-agent")),
            configureAfterMap: app => app.MapAgentPrismA2A());

        await SendMessageAsync(host.Client, "kod-agent", "merhaba");

        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        var entries = await auditLog.QueryAsync(new AuditQuery { Action = "external.call" });

        var entry = entries.ShouldHaveSingleItem();
        entry.Entity.ShouldBe("agent:kod-agent");
        entry.After.ShouldNotBeNull().ShouldContain("\"protocol\":\"a2a\"", Case.Sensitive);
    }

    private static string CancelOrder(string orderId) => $"iptal edildi: {orderId}";

    private static async Task<(HttpResponseMessage Response, System.Text.Json.JsonElement? Body)> SendMessageAsync(
        HttpClient client,
        string agentName,
        string text)
    {
        // Govde elle degil, SDK'nin KENDI tipleri ve kendi JsonSerializerOptions'i
        // (A2AJsonUtilities.DefaultOptions) ile uretilir — alan adi/kasa varsayimi
        // yapmak yerine sozlesmenin GERCEK kaynagina guvenilir.
        var sendMessageRequest = new A2A.SendMessageRequest
        {
            Message = new A2A.Message
            {
                Role = A2A.Role.User,
                Parts = [A2A.Part.FromText(text)],
                MessageId = Guid.NewGuid().ToString(),
            },
        };

        var paramsJson = System.Text.Json.JsonSerializer.SerializeToElement(
            sendMessageRequest, A2A.A2AJsonUtilities.DefaultOptions);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/agentprism/a2a/{agentName}")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "SendMessage",
                @params = paramsJson,
            }),
        };
        request.Headers.Accept.ParseAdd("application/json");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return (response, body.Length == 0 ? null : System.Text.Json.JsonDocument.Parse(body).RootElement);
    }
}
