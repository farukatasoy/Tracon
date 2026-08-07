using System.Net;
using System.Net.Http.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using FakeModelProvider = AgentPrism.Testing.FakeModelProvider;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary><c>Idempotency-Key</c> destegi testleri (Faz 43).</summary>
public sealed class IdempotencyTests
{
    private const string HeaderName = "Idempotency-Key";

    private static readonly Uri Run = new("/agentprism/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Responses = new("/agentprism/v1/responses", UriKind.Relative);
    private static readonly Uri Retention = new("/agentprism/api/retention/idempotency_keys", UriKind.Relative);
    private static readonly Uri Quotas = new("/agentprism/api/quotas", UriKind.Relative);

    private static async Task<HttpResponseMessage> PostWithKeyAsync(AgentPrismTestHost host, Uri uri, object body, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        request.Headers.Add(HeaderName, key);

        return await host.Client.SendAsync(request).ConfigureAwait(false);
    }

    [Fact]
    public async Task Ayni_anahtar_ayni_govde_agenti_ikinci_kez_calistirmaz()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "merhaba" };

        using (var first = await PostWithKeyAsync(host, Run, body, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
            first.Headers.Contains("Idempotency-Replayed").ShouldBeFalse();
        }

        using (var second = await PostWithKeyAsync(host, Run, body, key))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
            second.Headers.GetValues("Idempotency-Replayed").ShouldContain("true", StringComparer.Ordinal);
        }

        var provider = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();
        provider.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Ayni_anahtar_FARKLI_govde_422_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");

        using (var first = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "merhaba" }, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var mismatched = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "BASKA" }, key);

        mismatched.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Eszamanli_ayni_anahtar_istegi_agenti_YALNIZ_BIR_KEZ_calistirir()
    {
        // 🚨 Test sunucusu (TestServer) es zamanli istekleri gercek bir agin
        // aksine SIRALI islemeye de karar verebilir; bu durumda ikinci istek
        // 409 yerine REPLAY edilmis 200 alir (kayit zaten Completed'dir). Bu
        // yuzden "kim 409 aldi" yerine ayirmanin ATOMIK oldugunu gosteren
        // TEK degismez dogrulanir: agent kac istek gelirse gelsin YALNIZ BIR
        // KEZ calisir. Ayirmanin gercek yaris testi (8 eszamanli cagri, ag
        // katmani olmadan) `IdempotencyStoreContract`'tadir ve uc SQL
        // saglayicisinda da kosar.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "merhaba" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostWithKeyAsync(host, Run, body, key)));

        try
        {
            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
            responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThanOrEqualTo(1);

            var provider = host.Services.GetServices<IModelProvider>().OfType<FakeModelProvider>().Single();
            provider.Requests.Count.ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Basarisiz_istekten_sonra_ayni_anahtarla_yeniden_deneme_calisir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        var key = Guid.NewGuid().ToString("N");

        // Bos mesaj RunAsync icinde 400 ile reddedilir; IdempotencyFilter bu
        // basarisizligi SAKLAMAMALI ve kaydi SILMELIDIR.
        using (var failed = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "  " }, key))
        {
            failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var retried = await PostWithKeyAsync(host, Run, new AgentRunRequest { Message = "  " }, key);

        // 🚨 409 (InProgress) veya saklanan-eski-hata DEGIL: anahtar serbest
        // birakilmis olmalidir, ayni sonuc (400) TEKRAR uretilir.
        retried.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Akisli_istekte_Idempotency_Key_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostWithKeyAsync(
            host,
            Responses,
            new { model = "kod-agent", input = "merhaba", stream = true },
            Guid.NewGuid().ToString("N"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tekrarlanan_istek_kotayi_ikinci_kez_tuketmez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using (var created = await host.Client.PutAsJsonAsync(
                   Quotas,
                   new QuotaSaveRequest { AgentName = "kod-agent", Period = QuotaPeriod.Daily, MaxRuns = 1, Enabled = true }))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "merhaba" };

        using (var first = await PostWithKeyAsync(host, Run, body, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Kota tukendi (maxRuns=1); YENI bir istek 429 alirdi. Ama AYNI
        // anahtar+govde saklanan yaniti dondurmelidir — QuotaGate'e HIC ugramaz.
        using var replayed = await PostWithKeyAsync(host, Run, body, key);

        replayed.StatusCode.ShouldBe(HttpStatusCode.OK);
        replayed.Headers.GetValues("Idempotency-Replayed").ShouldContain("true", StringComparer.Ordinal);
    }

    [Fact]
    public async Task Hiz_sinirina_tabidir()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.Configure<AgentPrismRateLimitOptions>(
                static options =>
                {
                    options.Enabled = true;
                    options.PermitLimit = 1;
                    options.Window = TimeSpan.FromMinutes(5);
                }));

        var key = Guid.NewGuid().ToString("N");
        var body = new AgentRunRequest { Message = "merhaba" };

        using (var first = await PostWithKeyAsync(host, Run, body, key))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        // Hiz siniri IdempotencyFilter'DAN ONCE calisir (43.1): ayni anahtarla
        // gelen tekrar bile pencere dolunca 429 alir.
        using var limited = await PostWithKeyAsync(host, Run, body, key);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Kapaliyken_baslik_tasiyan_istek_501_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()),
            configureServices: static services => services.Configure<AgentPrismIdempotencyOptions>(
                static options => options.Enabled = false));

        using var response = await PostWithKeyAsync(
            host, Run, new AgentRunRequest { Message = "merhaba" }, Guid.NewGuid().ToString("N"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotImplemented);
    }

    [Fact]
    public async Task Baslik_yokken_davranis_degismez()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await host.Client.PostAsJsonAsync(Run, new AgentRunRequest { Message = "merhaba" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/event-stream");
    }

    [Fact]
    public async Task Cok_uzun_anahtar_400_doner()
    {
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostWithKeyAsync(
            host, Run, new AgentRunRequest { Message = "merhaba" }, new string('a', 300));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Idempotency_keys_saklama_hedefi_olarak_taninir()
    {
        await using var host = await AgentPrismTestHost.StartAsync();

        using var response = await host.Client.PutAsJsonAsync(
            Retention,
            new RetentionPolicySaveRequest { MaxAgeDays = 1, Enabled = true });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
