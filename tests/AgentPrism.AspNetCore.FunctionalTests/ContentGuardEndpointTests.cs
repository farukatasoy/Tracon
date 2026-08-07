using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AgentPrism.AspNetCore.FunctionalTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>Icerik guard'inin HTTP yuzeyindeki davranisi (Faz 48).</summary>
/// <remarks>
/// 🚨 <c>422</c> yalnizca <strong>akissiz</strong> dalda mumkundur. Akisli dal
/// varsayilandir ve SSE basliklari calistirma baslamadan gonderilir; guard model
/// boru hattinda oldugu icin karar durum kodu yazildiktan SONRA olusur. Akissiz
/// dal <c>Idempotency-Key</c> basligiyla secilir (Faz 43).
/// </remarks>
public sealed class ContentGuardEndpointTests
{
    private const string DeniedTerm = "gizli-proje";
    private const string CardNumber = "4539578763621486";

    private static readonly Uri Run = new("/agentprism/api/agents/kod-agent/run", UriKind.Relative);
    private static readonly Uri Audit = new("/agentprism/api/audit?action=content.blocked", UriKind.Relative);

    [Fact]
    public async Task Girise_takilan_engelleme_422_doner()
    {
        await using var host = await StartAsync();

        using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = $"{DeniedTerm} nedir" });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        problem.GetProperty("title").GetString().ShouldBe("Icerik engellendi");
        problem.GetProperty("errorType").GetString().ShouldBe("content_blocked");
        problem.GetProperty("guard").GetString().ShouldBe("pattern");
        problem.GetProperty("rule").GetString().ShouldBe("denied-term");
        problem.GetProperty("direction").GetString().ShouldBe("Input");
    }

    [Fact]
    public async Task ProblemDetails_engellenen_metni_tasimaz()
    {
        // 🚨 Yasak sozcuk listesi kurumsal bir sirdir; yanit govdesine yazmak onu
        // istemciye sizdirirdi.
        await using var host = await StartAsync();

        using var response = await PostBufferedAsync(
            host,
            new AgentRunRequest { Message = $"{DeniedTerm} kod adiyla anilan urun" });

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldNotContain(DeniedTerm, Case.Insensitive);
        body.ShouldNotContain("kod adiyla anilan urun", Case.Insensitive);
    }

    [Fact]
    public async Task Engelleme_calistirma_kaydina_content_blocked_yazar()
    {
        await using var host = await StartAsync();

        using (var response = await PostBufferedAsync(host, new AgentRunRequest { Message = DeniedTerm }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/agentprism/api/runs?errorType=content_blocked", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var run = runs.ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Failed);
        run.Error!.Type.ShouldBe("content_blocked");
        run.Error.Class.ShouldBe(RunErrorClass.ContentBlocked);
    }

    [Fact]
    public async Task Engelleme_devre_kesiciyi_acmaz()
    {
        // 🚨 On engelleme ust uste geldiginde saglayici KAPANMAMALIDIR: bir
        // politika karari bir kesintiye donusmemeli.
        await using var host = await StartAsync();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = DeniedTerm });

            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        // Davranissal dogrulama: zararsiz bir istek hâlâ gecmeli. Devre acilmis
        // olsaydi bu istek AgentPrismProviderUnavailableException ile duserdi.
        using var afterwards = await PostBufferedAsync(host, new AgentRunRequest { Message = "zararsiz istem" });

        afterwards.StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Services
            .GetRequiredService<ModelProviderCircuitBreaker>()
            .IsOpen("fake", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Denetim_izi_kural_adini_yazar_metni_yazmaz()
    {
        await using var host = await StartAsync();

        using (var response = await PostBufferedAsync(host, new AgentRunRequest { Message = $"{DeniedTerm} detaylari" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var entries = await host.Client.GetFromJsonAsync<List<AuditEntry>>(
            Audit,
            TestContext.Current.CancellationToken);

        var entry = entries.ShouldHaveSingleItem();

        entry.Action.ShouldBe("content.blocked");
        entry.After.ShouldNotBeNull();
        entry.After!.ShouldContain("denied-term", Case.Sensitive);
        entry.After!.ShouldNotContain(DeniedTerm, Case.Insensitive);
    }

    [Fact]
    public async Task Maskeleme_calistirmayi_kesmez_ve_olay_yazar()
    {
        await using var host = await StartAsync();

        using (var response = await PostBufferedAsync(
            host,
            new AgentRunRequest { Message = $"kart numaram {CardNumber}" }))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var runs = await host.Client.GetFromJsonAsync<List<RunRecord>>(
            new Uri("/agentprism/api/runs", UriKind.Relative),
            TestContext.Current.CancellationToken);

        var run = runs.ShouldHaveSingleItem();

        run.Status.ShouldBe(RunStatus.Completed);

        var events = await host.Client.GetStringAsync(
            new Uri($"/agentprism/api/runs/{run.Id}/events", UriKind.Relative),
            TestContext.Current.CancellationToken);

        events.ShouldContain("ContentMasked", Case.Sensitive);

        // 🚨 Guard'in KENDI olayi icerik tasimaz. Olcum: ContentMasked satirini
        // ayikla ve kart numarasini arama.
        //
        // 🚨 Akisin BUTUNU icin ayni sey soylenemez ve bu bilincli bir sinirdir:
        // RunStarted olayi kullanicinin ham istemini tasir (Faz 45, uretimden eval
        // vakasi terfisinin tek kaynagi). Maskeleme MODEL SINIRINDA bir kontroldur;
        // AgentPrism'in kendi kayitlarini geriye donuk temizlemez. O ayri bir istir
        // (saklama/redaksiyon) ve bu fazin kapsami disindadir.
        var maskedLines = events
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(static line => line.Contains("ContentMasked", StringComparison.Ordinal))
            .ToList();

        maskedLines.ShouldNotBeEmpty();

        foreach (var line in maskedLines)
        {
            line.ShouldNotContain(CardNumber, Case.Sensitive);
        }
    }

    [Fact]
    public async Task Guard_kapaliyken_davranis_degismez()
    {
        // Varsayilan kurulum hicbir guard kaydetmez: K1'in kapisi kaydin kendisidir.
        await using var host = await AgentPrismTestHost.StartAsync(
            static builder => builder.AddAgent(TestData.Definition()));

        using var response = await PostBufferedAsync(host, new AgentRunRequest { Message = $"{DeniedTerm} nedir" });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        host.Services.GetServices<IContentGuard>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Akisli_dalda_engelleme_SSE_hata_olayi_olur()
    {
        // 🚨 Plandan sapma: akisli yolda 422 fiziksel olarak imkansizdir. SSE
        // basliklari calistirma baslamadan gonderilir ve durum kodu 200'dur.
        // Engelleme akista bir 'error' olayi olarak gorunur; calistirma kaydi
        // yine content_blocked yazar.
        await using var host = await StartAsync();

        using var response = await host.Client.PostAsJsonAsync(
            Run,
            new AgentRunRequest { Message = DeniedTerm },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain("AgentPrismContentBlockedException", Case.Sensitive);
        body.ShouldNotContain(DeniedTerm, Case.Insensitive);
    }

    private static Task<AgentPrismTestHost> StartAsync()
        => AgentPrismTestHost.StartAsync(static builder => builder
            .AddAgent(TestData.Definition())
            .AddPatternContentGuard(static options =>
            {
                options.MaskedPii = PiiPatterns.CreditCard;
                options.DeniedTerms.Add(DeniedTerm);
            }));

    /// <summary>
    /// Akissiz dala girer. <c>Idempotency-Key</c> basligi tasiyan istek SSE
    /// yerine tek bir JSON yanitla calisir (Faz 43) ve yalniz o dalda bir durum
    /// kodu donebilir.
    /// </summary>
    private static async Task<HttpResponseMessage> PostBufferedAsync(AgentPrismTestHost host, AgentRunRequest body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Run) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        return await host.Client.SendAsync(request, TestContext.Current.CancellationToken).ConfigureAwait(false);
    }
}
