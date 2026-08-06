using System.Net;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Calistirma iptali ucunun testleri (Faz 32).</summary>
public sealed class CancelRunEndpointTests
{
    private const string TenantHeader = "X-AgentPrism-Tenant";

    [Fact]
    public async Task Defterde_kayitli_calistirma_202_doner_ve_kaynak_iptal_edilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var cts = new CancellationTokenSource();
        var registry = host.Services.GetRequiredService<IRunCancellationRegistry>();
        using var registration = registry.Register(runId, runId, "default", cts);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Olmayan_calistirma_iptal_edilmeye_calisilinca_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsync(CancelUri(AgentPrismId.NewId()), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Calisan_ama_defterde_olmayan_calistirma_409_doner()
    {
        // Baska bir ornekte yurutuluyor ya da surec calistirma sirasinda
        // yeniden baslamis: ikisinde de 'runs' Running gorunur ama bu surecin
        // bellek ici defterinde hicbir kayit yoktur.
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Running);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Bitmis_calistirma_409_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();
        var runId = await SeedRunAsync(host, RunStatus.Completed);

        using var response = await host.Client.PostAsync(CancelUri(runId), content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Baska_kiracinin_calistirmasi_iptal_edilemez_AYNI_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.UseTenancy(static options =>
            {
                options.Enabled = true;
                options.AllowHeaderResolution = true;
            }));

        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "kiraci-a",
        });

        // "yok" ile "baska kiraciya ait" AYNI 404'u dondurmelidir; ayni gerekce
        // RunFeedbackEndpointTests'teki gibi -- varlik sizdirmamak icin.
        using var missing = await SendAsTenant(host, CancelUri(AgentPrismId.NewId()), "kiraci-b");
        using var wrongTenant = await SendAsTenant(host, CancelUri(runId), "kiraci-b");

        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var missingBody = await AgentPrismTestHost.ReadJsonAsync(missing);
        var wrongTenantBody = await AgentPrismTestHost.ReadJsonAsync(wrongTenant);
        missingBody.GetProperty("title").GetString().ShouldBe(wrongTenantBody.GetProperty("title").GetString());
    }

    private static async Task<Guid> SeedRunAsync(AgentPrismTestHost host, RunStatus status)
    {
        var runs = host.Services.GetRequiredService<IRunStore>();
        var runId = AgentPrismId.NewId();

        await runs.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = "default",
        });

        if (status != RunStatus.Running)
        {
            await runs.CompleteRunAsync(new RunCompletion
            {
                RunId = runId,
                Status = status,
                CompletedAt = DateTimeOffset.UtcNow,
            });
        }

        return runId;
    }

    private static Uri CancelUri(Guid runId) => new($"/agentprism/api/runs/{runId}/cancel", UriKind.Relative);

    private static async Task<HttpResponseMessage> SendAsTenant(AgentPrismTestHost host, Uri uri, string tenant)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        request.Headers.Add(TenantHeader, tenant);

        return await host.Client.SendAsync(request);
    }
}
