using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Webhook uclarinin testleri (Faz 21).</summary>
/// <remarks>
/// 🚨 En onemli test <see cref="Yanit_ve_kayit_hicbir_sir_tasimaz"/>: sozlesmede
/// sir alani <strong>hic yoktur</strong> ve fazladan gonderilen bir alan
/// baglanmaz (K-059).
/// </remarks>
public sealed class WebhookEndpointTests
{
    private static readonly Uri Webhooks = new("/agentprism/api/webhooks", UriKind.Relative);
    private static readonly Uri Orders = new("/agentprism/api/webhooks/orders", UriKind.Relative);

    [Fact]
    public async Task Abonelik_olusturulur_okunur_ve_silinir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var body = await AgentPrismTestHost.ReadJsonAsync(created);
            body.GetProperty("url").GetString().ShouldBe("https://example.com/hook");
            body.GetProperty("enabled").GetBoolean().ShouldBeTrue();
            body.GetProperty("events").GetArrayLength().ShouldBe(2);
        }

        using (var listed = await host.Client.GetAsync(Webhooks))
        {
            (await AgentPrismTestHost.ReadJsonAsync(listed)).GetArrayLength().ShouldBe(1);
        }

        using (var deleted = await host.Client.DeleteAsync(Orders))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var missing = await host.Client.GetAsync(Orders))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Yanit_ve_kayit_hicbir_sir_tasimaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        // Istemci fazladan bir 'secret' alani gonderiyor. Sozlesmede boyle bir
        // alan YOKTUR; baglanmamali ve hicbir yanitta gorunmemelidir.
        using (var created = await host.Client.PutAsJsonAsync(
                   Orders,
                   new
                   {
                       url = "https://example.com/hook",
                       events = new[] { "run.completed" },
                       secretConfigurationKey = "AgentPrism:Webhooks:Secrets:orders",
                       secret = "super-gizli-deger",
                       signingSecret = "baska-gizli-deger",
                       enabled = true,
                   }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);

            var raw = await created.Content.ReadAsStringAsync();

            raw.ShouldNotContain("super-gizli-deger");
            raw.ShouldNotContain("baska-gizli-deger");

            // Anahtarin ADI donmelidir — deger degil.
            raw.ShouldContain("AgentPrism:Webhooks:Secrets:orders");
        }

        using var listed = await host.Client.GetAsync(Webhooks);
        var listRaw = await listed.Content.ReadAsStringAsync();

        listRaw.ShouldNotContain("super-gizli-deger");
        listRaw.ShouldNotContain("baska-gizli-deger");
    }

    [Fact]
    public async Task Http_adresi_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(url: "http://example.com/hook"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        problem.GetProperty("detail").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/hook")]
    [InlineData("not-a-url")]
    public async Task Gecersiz_sema_veya_bicim_reddedilir(string url)
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(url: url));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Taninmayan_olay_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Orders,
            Request(events: ["run.completed", "uydurma.olay"]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var problem = await AgentPrismTestHost.ReadJsonAsync(response);
        problem.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("uydurma.olay");
    }

    [Fact]
    public async Task Bos_olay_listesi_reddedilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(Orders, Request(events: []));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sinama_olayi_kuyruga_yazilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/webhooks/orders/test", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await AgentPrismTestHost.ReadJsonAsync(response);
        body.GetProperty("queued").GetBoolean().ShouldBeTrue();

        // Abonelik 'test.ping' olayina abone degildi; yine de sinama gonderilir
        // ve olay listesi kalici olarak degismez.
        using var reloaded = await host.Client.GetAsync(Orders);
        var subscription = await AgentPrismTestHost.ReadJsonAsync(reloaded);

        subscription.GetProperty("events").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task Teslim_gecmisi_okunur()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using (var created = await host.Client.PutAsJsonAsync(Orders, Request()))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var test = await host.Client.PostAsJsonAsync(
                   new Uri("/agentprism/api/webhooks/orders/test", UriKind.Relative),
                   new { }))
        {
            test.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var response = await host.Client.GetAsync(
            new Uri("/agentprism/api/webhooks/orders/deliveries", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AgentPrismTestHost.ReadJsonAsync(response)).GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Olmayan_abonelige_sinama_gonderilemez()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            new Uri("/agentprism/api/webhooks/yok/test", UriKind.Relative),
            new { });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Loopback_http_adresi_izin_acikken_kabul_edilir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            configureServices: static services => services.Configure<AgentPrismWebhookOptions>(
                static options => options.AllowInsecureHttp = true));

        using var response = await host.Client.PutAsJsonAsync(
            Orders,
            Request(url: "http://localhost:9999/hook"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static WebhookSaveRequest Request(
        string url = "https://example.com/hook",
        IReadOnlyList<string>? events = null)
        => new()
        {
            Url = url,
            Events = events ?? ["run.completed", "run.failed"],
            Enabled = true,
        };
}
