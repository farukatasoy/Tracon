using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Zamanlama ve is kuyrugu uclarinin bellek ici davranis testleri (Faz 17).</summary>
public sealed class SchedulingEndpointTests
{
    private static readonly Uri Schedules = new("/agentprism/api/schedules", UriKind.Relative);
    private static readonly Uri Jobs = new("/agentprism/api/jobs", UriKind.Relative);

    [Fact]
    public async Task Zamanlama_olusturulur_guncellenir_ve_silinir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(
                   new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
                   Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("targetName").GetString().ShouldBe("ozetleyici");
            body.GetProperty("nextRunAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
        }

        using (var updated = await host.Client.PutAsJsonAsync(
                   new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
                   Request() with { TargetName = "yeni-hedef" }))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await AgentPrismTestHost.ReadJsonAsync(updated)).GetProperty("targetName").GetString()
                .ShouldBe("yeni-hedef");
        }

        using (var listed = await host.Client.GetAsync(Schedules))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using var deleted = await host.Client.DeleteAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var getAfterDelete = await host.Client.GetAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative));
        getAfterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Gecersiz_cron_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { Cron = "* * * *" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Gecersiz_saat_dilimi_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { TimeZone = "Bolge/Yok" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Oge_sayisi_sinir_asarsa_kayit_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.UseScheduling(o => o.MaxItemsPerJob = 1));

        using var response = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request() with { Payload = JsonDocument.Parse("""["a","b"]""").RootElement });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Var_olmayan_zamanlama_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri("/agentprism/api/schedules/yok", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Tetikleme_is_olusturur_ve_ogelerini_uretir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative), Request());

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu/trigger", UriKind.Relative),
            new { });

        triggered.StatusCode.ShouldBe(HttpStatusCode.OK);
        var job = await AgentPrismTestHost.ReadJsonAsync(triggered);
        var jobId = job.GetProperty("id").GetGuid();
        job.GetProperty("status").GetString().ShouldBe("Pending");
        job.GetProperty("totalItems").GetInt32().ShouldBe(2);

        using var detail = await host.Client.GetAsync(new Uri($"/agentprism/api/jobs/{jobId}", UriKind.Relative));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(detail);
        body.GetProperty("job").GetProperty("id").GetGuid().ShouldBe(jobId);
        body.GetProperty("items").GetArrayLength().ShouldBe(2);

        using var listed = await host.Client.GetAsync(Jobs);
        (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Is_iptal_edilir_ve_ikinci_iptal_409_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative), Request());

        using var triggered = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu/trigger", UriKind.Relative),
            new { });
        var jobId = (await AgentPrismTestHost.ReadJsonAsync(triggered)).GetProperty("id").GetGuid();

        using var cancelled = await host.Client.PostAsync(
            new Uri($"/agentprism/api/jobs/{jobId}/cancel", UriKind.Relative),
            content: null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var secondCancel = await host.Client.PostAsync(
            new Uri($"/agentprism/api/jobs/{jobId}/cancel", UriKind.Relative),
            content: null);
        secondCancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Var_olmayan_is_404_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(new Uri($"/agentprism/api/jobs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Operator_tetikleyebilir_ama_zamanlama_kaydedemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Operator, static policy => policy.RequireAssertion(static _ => true))
                .AddPolicy(AgentPrismPolicies.Admin, static policy => policy.RequireAssertion(static _ => false)));

        // Zamanlama HTTP yetkilendirmesini atlayarak dogrudan depoya yazilir:
        // bu testte Admin ucu kapali, o yuzden zamanlama baska bir yoldan tohumlanir.
        await host.Services.GetRequiredService<IJobScheduleStore>().SaveAsync(
            new JobSchedule
            {
                TenantId = "default",
                Name = "gece-raporu",
                Kind = JobKind.AgentBatch,
                TargetName = "ozetleyici",
                Payload = JsonDocument.Parse("""["a"]""").RootElement,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

        using var trigger = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu/trigger", UriKind.Relative),
            new { });
        trigger.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var save = await host.Client.PutAsJsonAsync(
            new Uri("/agentprism/api/schedules/gece-raporu", UriKind.Relative),
            Request());
        save.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static JobScheduleSaveRequest Request()
        => new()
        {
            Kind = JobKind.AgentBatch,
            TargetName = "ozetleyici",
            Cron = "0 3 * * *",
            TimeZone = "UTC",
            Payload = JsonDocument.Parse("""["a","b"]""").RootElement,
            Enabled = true,
        };
}
