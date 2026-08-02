using System.Text.RegularExpressions;
using AgentPrism.Ui.E2ETests.Infrastructure;
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

    [Fact]
    public async Task Arayuz_acilir_ve_ana_ekran_cizilir()
    {
        await using var host = await UiHost.StartAsync();
        await using var session = await Session.OpenAsync(browsers, host);

        await session.Page.GotoAsync(host.UiAddress);

        await session.Page.GetByRole(AriaRole.Heading, new() { Name = "Agents" }).WaitForAsync();

        (await session.Page.TitleAsync()).ShouldBe("AgentPrism");

        // Yedi ekranin tamami gezinme cubugunda olmalidir.
        foreach (var screen in new[] { "Agents", "Playground", "Sessions", "Runs", "Tools", "Models", "Settings" })
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
}
