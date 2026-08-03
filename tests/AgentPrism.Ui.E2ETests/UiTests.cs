using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AgentPrism.Ui.E2ETests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace AgentPrism.Ui.E2ETests;

/// <summary>
/// Gomulu arayuzu gercek bir tarayicida dogrular.
/// </summary>
/// <remarks>
/// Her test kendi sunucusunu ve kendi tarayici baglamini kurar; testler arasinda
/// paylasilan durum yoktur. Depolar bellek ici oldugu icin kurulum maliyeti
/// dusuktur.
/// </remarks>
public sealed class UiTests(BrowserFixture browsers)
{
    /// <summary>Playground'daki calistirma baglantisini bulan desen.</summary>
    private static readonly Regex RunLinkPattern =
        new("^run ", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Is detay basligini bulan desen.</summary>
    private static readonly Regex JobHeadingPattern =
        new("^Job ", RegexOptions.None, TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Arayuz_acilir_ve_ana_ekran_cizilir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" }).WaitForAsync();

        (await session.Page.TitleAsync()).ShouldBe("AgentPrism");

        // Tum yonetim ekranlari gezinme cubugunda olmalidir.
        foreach (var screen in new[] { "Agents", "Playground", "Sessions", "Workflows", "Jobs", "Runs", "Tools", "Skills", "Models", "MCP", "Settings" })
        {
            (await session.Page.GetByRole(AriaRole.Link, new() { Name = screen }).CountAsync())
                .ShouldBeGreaterThan(0, $"'{screen}' baglantisi bulunamadi.");
        }
    }

    [Fact]
    public async Task Kod_agenti_listede_gorunur_ve_duzenlenemez()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents");

        await session.Page.GetByText("Support assistant").WaitForAsync();

        await session.Page.GetByText("Support assistant").ClickAsync();

        // Kodda tanimli agent icin yazma uclari 409 doner; arayuz duzenleme
        // dugmesini hic gostermemelidir.
        await session.Page.GetByText("This agent is declared in code").WaitForAsync();

        (await session.Page.GetByRole(AriaRole.Link, new() { Name = "Edit" }).CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Playground_akisi_gelir_ve_tool_karti_dolar()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

        await session.Page.GetByTestId("playground-input").FillAsync("ORD-7 nerede");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        // Tool karti, gercek bir FunctionCallContent geldiginde olusur.
        var card = session.Page.GetByTestId("tool-card").First;

        await card.WaitForAsync(new() { Timeout = 20_000 });
        await card.GetByText("get_order_status").WaitForAsync();
        await card.GetByText("done").WaitForAsync(new() { Timeout = 20_000 });

        // Model yaniti tool sonucundan sonra akar.
        await session.Page.GetByText("Echo: ORD-7 nerede").WaitForAsync(new() { Timeout = 20_000 });

        // Calistirma kaydina koprü: ilk SSE cercevesi calistirma kimligini tasir.
        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Playground_dosya_yuklenir_onizleme_gorunur_ve_calistirma_devam_eder()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        var pngPath = Path.Combine(Path.GetTempPath(), $"agentprism-e2e-{Guid.NewGuid():N}.png");

        // 8 baytlik PNG imzasi + biraz dolgu: sihirli bayt denetimini gecmesi
        // icin gecerli bir imza yeter, tam bir PNG govdesi gerekmez.
        await File.WriteAllBytesAsync(
            pngPath,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]);

        try
        {
            await session.Page.GotoAsync($"{host.UiAddress}/playground/support");

            await session.Page.GetByTestId("attachment-input").SetInputFilesAsync(pngPath);

            var chip = session.Page.GetByTestId("attachment-chip").First;
            await chip.WaitForAsync(new() { Timeout = 10_000 });
            await chip.GetByText(Path.GetFileName(pngPath)).WaitForAsync();

            await session.Page.GetByTestId("playground-input").FillAsync("bu resmi tanimla");
            await session.Page.GetByTestId("playground-send").ClickAsync();

            // Ek referansi kucuk bir gonderim onizlemesi olarak turda da kalir.
            await session.Page.GetByTestId("attachment-chip").First.WaitForAsync(new() { Timeout = 20_000 });
            await session.Page.GetByText("Echo: bu resmi tanimla").WaitForAsync(new() { Timeout = 20_000 });
        }
        finally
        {
            File.Delete(pngPath);
        }
    }

    [Fact]
    public async Task Arayuzden_agent_olusturulur_ve_hemen_calistirilir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("ui-agent");
        await session.Page.GetByTestId("agent-display-name").FillAsync("Console agent");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModelProvider.ModelName);

        await session.Page.GetByTestId("agent-save").ClickAsync();

        // Kayittan sonra detay ekranina gidilir ve yeni tanim gorunur.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Console agent" })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/playground/ui-agent");
        await session.Page.GetByTestId("playground-input").FillAsync("merhaba");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo: merhaba").WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Baglam_paneli_secilen_stratejiyi_istek_onizlemesine_yansitir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents/new");

        await session.Page.GetByTestId("agent-name").FillAsync("baglam-agenti");
        await session.Page.GetByTestId("agent-model").FillAsync(ScriptedModelProvider.ModelName);

        // Strateji secilene kadar koşullu alanlar (tetikleyici vb.) gorunmez.
        (await session.Page.GetByLabel("Trigger: message count").CountAsync()).ShouldBe(0);

        await session.Page.GetByLabel("Compaction strategy").SelectOptionAsync("SlidingWindow");
        await session.Page.GetByLabel("Trigger: message count").FillAsync("40");
        await session.Page.GetByLabel("Enable todo tracking").CheckAsync();

        var preview = await session.Page.Locator("pre").First.TextContentAsync();

        preview.ShouldNotBeNull();
        preview.ShouldContain("\"strategy\": \"SlidingWindow\"");
        preview.ShouldContain("\"triggerMessages\": 40");
        preview.ShouldContain("\"enableTodo\": true");
    }

    [Fact]
    public async Task Arayuzden_skill_olusturulur_ve_listelenir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/skills/new");

        await session.Page.GetByPlaceholder("invoice-analysis").FillAsync("invoice-review");
        await session.Page.GetByRole(AriaRole.Textbox, new() { Name = "Description" })
            .FillAsync("Reviews invoices.");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Skills" }).WaitForAsync();
        await session.Page.GetByText("invoice-review", new() { Exact = true }).WaitForAsync();
    }

    [Fact]
    public async Task Farkli_onek_altinda_varliklar_yuklenir()
    {
        await using var host = await UiHost.StartAsync(prefix: "/panel");
        await using var session = await Session.OpenAsync(browsers, host);

        var failures = new List<string>();

        session.Page.Response += (_, response) =>
        {
            if (response.Status >= 400)
            {
                failures.Add($"{response.Status} {response.Url}");
            }
        };

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" }).WaitForAsync();

        failures.ShouldBeEmpty();

        // Derinlemesine bir rota da kabugu almalidir; yonlendirmeyi istemci yapar.
        await session.Page.GotoAsync($"{host.UiAddress}/settings");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Settings" }).WaitForAsync();

        await session.Page.GetByText("/panel", new() { Exact = false }).First.WaitForAsync();
    }

    [Fact]
    public async Task Koyu_tema_gecisi_calisir_ve_kalici_olur()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" }).WaitForAsync();

        var before = await session.Page.GetAttributeAsync("html", "data-theme");

        await session.Page.GetByTestId("theme-toggle").ClickAsync();

        var after = await session.Page.GetAttributeAsync("html", "data-theme");

        string.Equals(after, before, StringComparison.Ordinal)
            .ShouldBeFalse("Tema degismedi.");
        (after is "light" or "dark").ShouldBeTrue($"Beklenmeyen tema: {after}");

        // Tercih localStorage'da yasar; yeniden yukleme onu korumalidir.
        await session.Page.ReloadAsync();
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" }).WaitForAsync();

        string.Equals(await session.Page.GetAttributeAsync("html", "data-theme"), after, StringComparison.Ordinal)
            .ShouldBeTrue("Tema tercihi yeniden yuklemede korunmadi.");
    }

    [Fact]
    public async Task Token_gerektiginde_kabuk_acilir_ve_token_sorulur()
    {
        await using var host = await UiHost.StartAsync(authToken: "gizli-token");
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);

        // Kabuk bearer token katmanindan muaftir; aksi halde kullanici token'i
        // girebilecegi ekrani hicbir zaman goremezdi.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Access token required" })
            .WaitForAsync(new() { Timeout = 15_000 });

        await session.Page.GetByLabel("Token").FillAsync("gizli-token");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Continue" }).ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" })
            .WaitForAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Calistirma_olaylari_ekranda_adim_adim_gorunur()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("ORD-7 nerede");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo: ORD-7 nerede").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        // Olay adlari kararli sozlesmedir; ekran onlari oldugu gibi gosterir.
        foreach (var name in new[] { "run.started", "tool.invoking", "tool.invoked", "run.completed" })
        {
            await session.Page.GetByText(name, new() { Exact = true }).First
                .WaitForAsync(new() { Timeout = 20_000 });
        }
    }

    [Fact]
    public async Task Alt_agent_calistirmasi_agac_olarak_gorunur()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/yonlendirici");
        await session.Page.GetByTestId("playground-input").FillAsync("ORD-7 nerede");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GetByRole(AriaRole.Link, new() { NameRegex = RunLinkPattern }).First.ClickAsync();

        // Ozet olaylar kok akista gorunur; alt calistirmanin tam akisi
        // aynalanmaz (olay hacmi agac boyunca katlanirdi).
        await session.Page.GetByText("child.started", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });

        // Agac paneli hem koku hem alt calistirmayi gostermelidir.
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Call tree" })
            .WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GetByText("this run", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Runs_ekrani_varsayilan_olarak_yalniz_kokleri_listeler()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/playground/yonlendirici");
        await session.Page.GetByTestId("playground-input").FillAsync("ORD-7 nerede");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo:").First.WaitForAsync(new() { Timeout = 30_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // Varsayilan gorunum yalniz kokleri listeler ve alt calistirma sayisini
        // rozetle bildirir.
        await session.Page.GetByText("1 child run", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });

        (await session.Page.GetByText("depth 1", new() { Exact = true }).CountAsync()).ShouldBe(0);

        await session.Page.GetByRole(AriaRole.Combobox).Last.SelectOptionAsync("all");

        await session.Page.GetByText("depth 1", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 30_000 });
    }

    [Fact]
    public async Task Mcp_ekrani_guvenlik_sinirini_yazar_ve_sunucu_eklenir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/mcp");

        // Guvenlik siniri ekranda YAZILI olmalidir: MCP sunucusu eklemek,
        // tool tanimlarini disaridan kabul etmek demektir.
        await session.Page.GetByText("Security boundary").WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add server" }).ClickAsync();

        // Yer tutucu "github", "AgentPrism:Mcp:GithubToken" ile de eslesir;
        // tam eslesme istenmelidir.
        await session.Page.GetByPlaceholder("github", new() { Exact = true }).FillAsync("ornek");
        await session.Page.GetByPlaceholder("https://mcp.example.com/mcp")
            .FillAsync("https://mcp.ornek.test/mcp");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByText("ornek").First.WaitForAsync(new() { Timeout = 10_000 });

        // Onay rozeti gorunmelidir: MCP tool'lari varsayilan olarak onay ister.
        await session.Page.GetByText("approval", new() { Exact = true }).First.WaitForAsync();
    }

    [Fact]
    public async Task Models_ekraninda_saglik_rozeti_gorunur()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/models");

        // ScriptedModelProvider IModelProviderHealthCheck uygulamaz; bu bir hata
        // degildir ve rozet "unknown" gostermelidir. Faz 6'dan kalan "provider
        // connectivity checks are still missing" notu artik ekranda olmamalidir.
        await session.Page.GetByText("unknown", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 10_000 });

        (await session.Page.GetByText("Provider connectivity checks are still missing").CountAsync()).ShouldBe(0);

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Check now" }).First.ClickAsync();

        await session.Page.GetByText("unknown", new() { Exact = true }).First.WaitForAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task Tool_ekraninda_cagri_sayisi_gorunur()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        // Once gercek bir tool cagrisi ureten bir tur calistirilir.
        await session.Page.GotoAsync($"{host.UiAddress}/playground/support");
        await session.Page.GetByTestId("playground-input").FillAsync("ORD-9 nerede");
        await session.Page.GetByTestId("playground-send").ClickAsync();

        await session.Page.GetByText("Echo: ORD-9 nerede").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/tools");

        // Faz 5'te bu ekran "gozlemlenebilirlik fazinda gelir" notu tasiyordu
        // (sapma S5); artik gercek sayilar gosterilmelidir.
        await session.Page.GetByText("calls").First.WaitForAsync(new() { Timeout = 20_000 });
    }

    [Fact]
    public async Task Audit_ekrani_denetim_kaydini_listeler()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        using var client = new HttpClient { BaseAddress = new Uri(host.BaseAddress) };

        using (var created = await client.PostAsJsonAsync(
            $"{host.Prefix}/api/agents",
            new
            {
                name = "audit-e2e",
                instructions = "test",
                model = new { provider = ScriptedModelProvider.ProviderName, model = ScriptedModelProvider.ModelName },
                toolNames = Array.Empty<string>(),
            }))
        {
            created.EnsureSuccessStatusCode();
        }

        await session.Page.GotoAsync($"{host.UiAddress}/audit");

        await session.Page.GetByText("agent.create").First.WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("agent:audit-e2e").First.WaitForAsync();
    }

    [Fact]
    public async Task Reader_rolunde_yazma_dugmeleri_gizlenir()
    {
        // Faz 9: Admin policy'si basarisiz olunca arayuz yazma dugmelerini
        // gizlemelidir — sunucu tarafi yetkilendirme yine de tek gercektir,
        // bu yalnizca kotu bir deneyimi (gostersin, 403 alsin) onler.
        await using var host = await UiHost.StartAsync(
            configureServices: services => TestAuthenticationHandler.Add(services)
                .AddAuthorizationBuilder()
                .AddPolicy(AgentPrismPolicies.Admin, policy => policy.RequireAssertion(_ => false)));

        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/agents");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" }).WaitForAsync();

        (await session.Page.GetByRole(AriaRole.Link, new() { Name = "New agent" }).CountAsync()).ShouldBe(0);

        // Admin rolu karsilanmadigi icin Audit sekmesi de gezinme cubugunda
        // gorunmemelidir.
        (await session.Page.GetByRole(AriaRole.Link, new() { Name = "Audit" }).CountAsync()).ShouldBe(0);

        await session.Page.GotoAsync($"{host.UiAddress}/mcp");
        (await session.Page.GetByRole(AriaRole.Button, new() { Name = "Add server" }).CountAsync()).ShouldBe(0);
    }

    /// <summary>Tek bir testin tarayici baglami.</summary>
    private sealed class Session : IAsyncDisposable
    {
        private readonly IBrowserContext _context;

        private Session(IBrowserContext context, IPage page)
        {
            _context = context;
            Page = page;
        }

        public IPage Page { get; }

        public static async Task<Session> OpenAsync(BrowserFixture browsers, UiHost host)
        {
            var context = await browsers.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = host.BaseAddress,
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
            });

            return new Session(context, await context.NewPageAsync());
        }

        public async ValueTask DisposeAsync() => await _context.CloseAsync();
    }
    // --- Faz 16: workflow grafi ve human-in-the-loop ---

    [Fact]
    public async Task Workflow_grafi_cizilir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows");

        await session.Page.GetByText("ozetle-ve-cevir").WaitForAsync();
        await session.Page.GetByText("ozetle-ve-cevir").ClickAsync();

        var graph = session.Page.GetByTestId("workflow-graph");

        await graph.WaitForAsync();

        // Hazir desen kullanicinin yazmadigi bir cikti dugumu ekler: iki agent
        // + OutputMessages. Graf tanimdan degil DERLENMIS workflow'dan cizilir,
        // bu yuzden o dugum de gorunur.
        var nodes = session.Page.GetByTestId("workflow-node");

        (await nodes.CountAsync()).ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Mermaid_metni_kopyalanabilir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await session.Page.GotoAsync($"{host.UiAddress}/workflows/ozetle-ve-cevir");

        await session.Page.GetByTestId("workflow-graph").WaitForAsync();
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Copy" }).First.ClickAsync();

        var copied = await session.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");

        // Kopyalanan metin Microsoft Agent Framework'un uretimidir; arayuz onu
        // cizmez, yalnizca disari verir (bundle butcesi, K-002).
        copied.ShouldContain("flowchart", Case.Sensitive);
    }

    [Fact]
    public async Task Bekleyen_istek_karti_cevaplanabilir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/onay-akisi");

        await session.Page.GetByTestId("workflow-graph").WaitForAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("raporu yayinla");
        await session.Page.GetByTestId("workflow-run").ClickAsync();

        // Calistirma bir insan yaniti bekleyerek durur; kart o zaman belirir.
        var card = session.Page.GetByTestId("workflow-pending-request");

        await card.WaitForAsync(new() { Timeout = 20_000 });

        (await card.InnerTextAsync()).ShouldContain("raporu yayinla", Case.Sensitive);

        await session.Page.GetByTestId("workflow-approve").ClickAsync();

        // Yanit YENI bir calistirma acar ve graf ciktisini uretir.
        var output = session.Page.GetByTestId("workflow-output");

        await output.WaitForAsync(new() { Timeout = 20_000 });

        (await output.InnerTextAsync()).ShouldContain("onaylandi", Case.Sensitive);
    }

    [Fact]
    public async Task Bekleyen_calistirma_Runs_listesinde_ayri_durumla_gorunur()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/workflows/onay-akisi");
        await session.Page.GetByTestId("workflow-graph").WaitForAsync();

        await session.Page.GetByTestId("workflow-message").FillAsync("raporu yayinla");
        await session.Page.GetByTestId("workflow-run").ClickAsync();
        await session.Page.GetByTestId("workflow-pending-request").WaitForAsync(new() { Timeout = 20_000 });

        await session.Page.GotoAsync($"{host.UiAddress}/runs");

        // Ne tamamlandi ne basarisiz: kendi durumu vardir.
        //
        // 🚨 Exact zorunlu: durum SUZGECINDE de gizli bir <option>Awaiting input</option>
        // vardir ve alt dize eslemesi once onu bulup gorunur olmasini bekler.
        // Rozet kucuk harflidir, secenek buyuk; Exact ikisini ayirir.
        await session.Page
            .GetByText("awaiting input", new() { Exact = true })
            .WaitForAsync(new() { Timeout = 20_000 });
    }

    // --- Faz 17: toplu ve zamanlanmis calistirma ---

    [Fact]
    public async Task Zamanlama_olusturulur_tetiklenir_ve_is_tamamlanir()
    {
        await using var host = await UiHost.StartAsync(
            configureServices: services => services.UseScheduling(options =>
            {
                // Testin gercek zamanda beklemesi gerekmez; isci hemen yoklar.
                options.PollInterval = TimeSpan.FromMilliseconds(200);
                options.LeaseDuration = TimeSpan.FromSeconds(10);
            }));
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync($"{host.UiAddress}/jobs");
        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Jobs", Exact = true }).WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "New schedule" }).ClickAsync();

        await session.Page.GetByPlaceholder("nightly-report").FillAsync("e2e-toplu-is");
        await session.Page.GetByPlaceholder("summarizer").FillAsync("support");
        await session.Page.Locator("textarea").FillAsync("[\"merhaba\"]");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await session.Page.GetByText("e2e-toplu-is").WaitForAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Trigger" }).ClickAsync();

        // Isci hizli yoklama araligiyla isi kiralayip yurutur; tamamlanma
        // rozeti "Recent jobs" panelinde gorunur.
        await session.Page.GetByText("completed", new() { Exact = true }).First
            .WaitForAsync(new() { Timeout = 15_000 });

        // Is detayina gecilir: oge girdisi ve calistirma baglantisi gorunur.
        // "Recent jobs" sayfadaki IKINCI tablodur (once Schedules gelir); ilk
        // satirinin baglantisi is kimligine gider.
        await session.Page.Locator("table").Last.Locator("tbody tr").First
            .GetByRole(AriaRole.Link).First.ClickAsync();

        await session.Page.GetByRole(AriaRole.Heading, new() { NameRegex = JobHeadingPattern })
            .WaitForAsync(new() { Timeout = 10_000 });
        await session.Page.GetByText("merhaba").WaitForAsync();
    }
}
