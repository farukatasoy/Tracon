using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Veri saklama uclarinin testleri (Faz 25).</summary>
public sealed class RetentionEndpointTests
{
    private static readonly Uri Policies = new("/agentprism/api/retention", UriKind.Relative);
    private static readonly Uri RunEventsPolicy = new("/agentprism/api/retention/run_events", UriKind.Relative);
    private static readonly Uri Preview = new("/agentprism/api/retention/preview", UriKind.Relative);
    private static readonly Uri Run = new("/agentprism/api/retention/run", UriKind.Relative);
    private static readonly Uri History = new("/agentprism/api/retention/history", UriKind.Relative);

    [Fact]
    public async Task Politika_olusturulur_listelenir_ve_silinir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 30)))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("target").GetString().ShouldBe("run_events");
            body.GetProperty("maxAgeDays").GetInt32().ShouldBe(30);
        }

        using (var listed = await host.Client.GetAsync(Policies))
        {
            listed.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using (var deleted = await host.Client.DeleteAsync(RunEventsPolicy))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var afterDelete = await host.Client.GetAsync(RunEventsPolicy);
        afterDelete.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Ayni_hedef_ikinci_kez_kaydedilince_uzerine_yazilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 30));
        await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 7));

        using var listed = await host.Client.GetAsync(Policies);
        var body = await AgentPrismTestHost.ReadJsonAsync(listed);

        body.GetArrayLength().ShouldBe(1);
        body[0].GetProperty("maxAgeDays").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task Bilinmeyen_hedef_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        var unknown = new Uri("/agentprism/api/retention/not_a_real_table", UriKind.Relative);

        using var get = await host.Client.GetAsync(unknown);
        get.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var put = await host.Client.PutAsJsonAsync(unknown, Request(maxAgeDays: 30));
        put.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var delete = await host.Client.DeleteAsync(unknown);
        delete.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var preview = await host.Client.GetAsync(
            new Uri("/agentprism/api/retention/preview?target=not_a_real_table", UriKind.Relative));
        preview.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sifirdan_kucuk_maxAgeDays_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(RunEventsPolicy, Request(maxAgeDays: 0));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Politika_yokken_onizleme_kapali_gorunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/retention/preview?target=run_events", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body[0].GetProperty("target").GetString().ShouldBe("run_events");
        body[0].GetProperty("enabled").GetBoolean().ShouldBeFalse();
        body[0].GetProperty("matchingRows").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Onizleme_argumansiz_tum_hedefleri_dondurur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(Preview);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetArrayLength().ShouldBe(RetentionTargets.All.Count);
    }

    [Fact]
    public async Task Simdi_calistir_bir_is_kuyruga_yazar()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsync(
            new Uri("/agentprism/api/retention/run?target=run_events", UriKind.Relative),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("target").GetString().ShouldBe("run_events");
        var jobId = body.GetProperty("jobId").GetGuid();

        var jobStore = host.Services.GetRequiredService<IJobStore>();
        var job = await jobStore.GetAsync("default", jobId);

        job.ShouldNotBeNull();
        job.Kind.ShouldBe(JobKind.Retention);
        job.TargetName.ShouldBe("run_events");
    }

    [Fact]
    public async Task Bos_gecmis_bos_liste_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.GetAsync(History);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(0);
    }

    private static RetentionPolicySaveRequest Request(int? maxAgeDays)
        => new() { MaxAgeDays = maxAgeDays, Enabled = true };
}
