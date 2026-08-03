// AgentPrism ornek barindirici — Faz 3.
//
// Bu proje AgentPrism'in su anki yeteneklerini gercek bir ASP.NET Core
// uygulamasinda gosterir: tool kaydi, kod agent'i, katalog, calistirma kaydi,
// PostgreSQL kaliciligi ve OpenAI saglayicisi.
//
// Iki yapilandirma da desteklenir ("sifir surpriz" kurali):
//   - API anahtari tanimliysa  -> gercek OpenAI modelleri
//   - tanimli degilse          -> ag cagrisi yapmayan EchoModelProvider
//   - baglanti dizesi tanimliysa -> PostgreSQL; degilse bellek ici depolar
//
// HTTP katmani Faz 4'te geldi: tek bir `app.MapAgentPrism("/agentprism")` cagrisi
// yonetim API'sini ve OpenAI uyumlu calistirma uclarini baglar.
// Arayuz Faz 5'te geldi: `.UseUI()` cagrisi arayuz varliklarini kaydeder ve
// http://localhost:5080/agentprism adresinde calisan bir kontrol duzlemi acilir.
// Faz 6 gozlemlenebilirlik, tool onayi ve uzak MCP tool'lari getirdi:
//   - span'ler ve metrikler kendiliginden uretilir (AgentPrismDiagnostics)
//   - `cancel_order` tool'u onay ister; arayuzde onay karti cikar
//   - `.UseMcp()` uzak MCP sunucularinin tool'larini kesfeder
// Faz 8 saglayici genislemesi ve saglik denetimi getirdi:
//   - `.UseOpenAICompatible(ad, ...)` herhangi bir OpenAI uyumlu uca baglanir
//     (OpenRouter, Groq, vLLM, yerel Ollama/LM Studio...)
//   - `/agentprism/api/models/health` saglayicilarin erisilebilirligini denetler
//     (ucret uretmez); devre kesici ardisik hatada saglayiciyi gecici durdurur
// Faz 12 agent'in agent'i cagirmasini getirdi:
//   - `CallableAgentNames` bir agent'a baska agent'lari cagirma yetkisi verir
//   - her alt cagri AYRI bir `runs` satiri uretir; arayuz agaci cizer
//   - derinlik, token ve sayi sinirlari `AgentPrism:AgentGraph` ile ayarlanir
// Faz 15 workflow yurutmesini getirdi:
//   - `.UseWorkflows()` motoru acar; `AddWorkflow(...)` kodda serbest graf tanimlar
//   - arayuzden tanimlanan workflow yalnizca katalogdaki agent'lari diziler (K2)
//   - her yurutme bir `runs` satiridir; icindeki agent'lar altina baglanir
//   - her super-step'te kontrol noktasi yazilir; yarim kalan is surdurulebilir
// Bkz. docs/04-HTTP-API.md, docs/05-AGENTPRISM-UI.md, docs/06-GOZLEMLENEBILIRLIK.md,
//      docs/08-SAGLAYICI-GENISLEMESI.md, docs/12-AGENT-CAGRI-GRAFIGI.md,
//      docs/15-WORKFLOWS-YURUTME.md
//
// Calistirmadan once sirlari ayarlayin:
//   dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Database=AgentPrism;Username=...;Password=..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAICompatible:openrouter:ApiKey" "sk-or-..."

using AgentPrism;
using AgentPrism.Api;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

// OpenAPI belgesi TUKETICININ tercihidir. AgentPrism.AspNetCore bu pakete
// BAGIMLI DEGILDIR; uclar paylasilan cerceveden gelen ustveriyi tasir ve
// AddOpenApi() cagrildiginda belgede kendiliginden gorunur.
builder.Services.AddOpenApi();

var agentPrism = builder.AddAgentPrism()
    // Tool'lar YALNIZCA kodda tanimlanir. Arayuz bu listeden secim yaptirir;
    // tool kodu yazdirmaz. Bu bir guvenlik sinirdir.
    // [AgentPrismTool] ile isaretlenmemis metotlar taranmaz.
    .AddToolsFrom(typeof(OrderTools))
    // Uzak MCP sunuculari. Sunucu tanimi arayuzden veya /api/mcp-servers
    // ucundan eklenir; kesif arka planda yapilir. Kayitli sunucu yoksa hicbir
    // sey olmaz. MCP tool'lari varsayilan olarak onay ister.
    .UseMcp()
    // Workflow yurutme motoru (Faz 15). Katalogdaki agent'lar hazir desenlerle
    // birbirine baglanir. Motor kayitli degilse tanimlar yine yonetilebilir,
    // yalnizca calistirma ucu 501 doner.
    .UseWorkflows()
    // Gomulu yonetim arayuzu. Ayri bir esleme cagrisi gerekmez:
    // MapAgentPrism kaydi bulur ve arayuzu ayni onek altina baglar.
    .UseUI();

// Saglayici istege baglidir. API anahtari yoksa uygulama ag cagrisi yapmayan
// ornek saglayici ile calisir; hicbir sey kirilmaz.
var openAi = builder.Configuration.GetSection(OpenAIProviderOptions.SectionName);
var openAiEnabled = !string.IsNullOrWhiteSpace(openAi["ApiKey"]);

if (openAiEnabled)
{
    // Bu tek cagri iki saglayici kaydeder: "openai" (Chat Completions) ve
    // "openai-responses" (Responses API). Agent tanimi hangisini kullanacagini
    // ModelBinding.Provider ile secer.
    agentPrism.UseOpenAI(openAi);
}
else
{
    agentPrism.AddModelProvider(new EchoModelProvider());
}

// OpenAI UYUMLU herhangi bir uc (F-03). Ayni yapilandirma sekli, farkli alt
// bolum: AgentPrism:Providers:OpenAICompatible:openrouter:*. ApiKey yoksa
// saglayici hic kaydedilmez — "sifir surpriz" kurali burada da gecerli.
var openRouter = builder.Configuration.GetSection($"{OpenAICompatibleProviderOptions.SectionName}:openrouter");
var openRouterEnabled = !string.IsNullOrWhiteSpace(openRouter["ApiKey"]);

if (openRouterEnabled)
{
    agentPrism.UseOpenAICompatible("openrouter", openRouter);
}

// Yerel model sunucusu ornegi (F-05, Ollama/LM Studio). Kurulum F-03 ile AYNI
// cagridir; tek fark ApiKey vermemek (yerel sunucu istemiyor) ve yerel adrese
// isaret etmek. Bu ornek varsayilan olarak KAPALIDIR: cogu gelistirici
// makinesinde Ollama calismiyor olabilir ve kapali bir port hicbir sey
// bozmadan sadece o saglayiciyi listeden dusurur. Denemek icin:
//
//   ollama serve
//   ollama pull llama3.1
//
// ve asagidaki iki satiri etkinlestirin:
//
// agentPrism.UseOpenAICompatible("ollama", o =>
// {
//     o.Endpoint = new Uri("http://localhost:11434/v1");
//     o.DefaultModel = "llama3.1";
//     // ApiKey YOK — yerel sunucu istemiyor. OpenAIClient bos kimlik kabul
//     // etmedigi icin AgentPrism sabit bir yer tutucu kullanir (OPENAI001
//     // ile ilgisizdir, saglayici bunu hic gormez).
// });
//
// Bilinen fark: Ollama'nin tool_choice destegi modele gore degisir; akista
// usage gondermeyen sunucularda RunRecord.TotalTokens null kalir — bu bir
// hata degildir (bkz. docs/08-SAGLAYICI-GENISLEMESI.md, bolum 8.2).

var model = openAiEnabled
    ? new ModelBinding
    {
        Provider = OpenAIProviderNames.ChatCompletions,
        Model = openAi["DefaultModel"] ?? "gpt-5.4-mini",
    }
    : new ModelBinding { Provider = "echo", Model = "echo-1" };

agentPrism
    // Kodda bildirimsel agent. Katalogda "code" kaynagi ile gorunur ve
    // ayni ada sahip bir veritabani tanimina karsi oncelik kazanir.
    .AddAgent(new AgentDefinition
    {
        Name = "support",
        DisplayName = "Destek Asistani",
        Description = "Siparis ve kargo sorularini yanitlar.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. " +
                       "Siparis sorularinda mutlaka tool kullan.",
        Model = model,
        // cancel_order onay ister: model cagirmaya kalktiginda calistirma
        // durur ve arayuzde onay karti cikar.
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    })

    // Harness ayarli agent: baglam sikistirma ve todo takibi devrede.
    // Dosya erisimi HarnessSettings icinde bilerek yoktur; MAF'ta yalnizca
    // deger atandiginda etkinlesir ve AgentPrism o degeri hic atamaz (K-062).
    .AddAgent(new AgentDefinition
    {
        Name = "arastirmaci",
        DisplayName = "Arastirmaci",
        Description = "Siparis kayitlarini inceler ve bulgularini ozetler.",
        Instructions = "Sen bir arastirmacisin. Adim adim ilerle ve bulgularini ozetle.",
        Model = model,
        ToolNames = ["get_order_status"],
        Harness = new HarnessSettings
        {
            MaxContextWindowTokens = 32_000,
            MaximumIterationsPerRequest = 8,
            DisableWebSearch = true,
            DisableFileMemory = true,
        },
    })

    // Faz 12: agent'in agent'i cagirmasi. Yonlendirici kendisi tool kullanmaz;
    // isi uzman agent'a devreder. Her devir AYRI bir `runs` satiri uretir ve
    // arayuzun calistirma detayinda agac olarak gorunur.
    //
    // Sinirlar AgentPrism:AgentGraph bolumunden gelir (varsayilan: derinlik 3,
    // agac basina 200.000 token, 25 alt calistirma) ve agac boyunca TEK bir
    // butce nesnesiyle paylasilir.
    //
    // Alt agent olarak BILEREK "support" secildi, "arastirmaci" degil:
    // arastirmaci harness kullanir ve K-053'te belgelenen harness kusuru
    // (tool cagrisi baglanmiyor) alt calistirmayi da vururdu. Orneklerin
    // calisir olmasi, ornegin mimariyi anlatmasindan once gelir.
    .AddAgent(new AgentDefinition
    {
        Name = "yonlendirici",
        DisplayName = "Yonlendirici",
        Description = "Gelen istegi dogru uzman agent'a devreder.",
        Instructions = "Sen bir yonlendiricisin. Siparis sorularini 'support' agent'ina " +
                       "devret, sonucunu bekle ve kullaniciya ozetle. Kendi basina tool cagirma.",
        Model = model,
        CallableAgentNames = ["support"],
    })

    // Faz 15'in Sequential zinciri icin iki halka. Ikisi de tool kullanmaz:
    // zincirin kaniti, her halkanin bir oncekinin CIKTISINI gormesidir ve
    // tool cagrisi bu kaniti bulaniklastirirdi.
    .AddAgent(new AgentDefinition
    {
        Name = "ozetleyici",
        DisplayName = "Ozetleyici",
        Description = "Gelen metni uc maddede ozetler.",
        Instructions = "Gelen metni en fazla uc kisa madde halinde ozetle. Yorum ekleme.",
        Model = model,
    })
    .AddAgent(new AgentDefinition
    {
        Name = "cevirmen",
        DisplayName = "Cevirmen",
        Description = "Gelen metni Ingilizceye cevirir.",
        Instructions = "Gelen metni Ingilizceye cevir. Yalnizca cevirilmis metni dondur.",
        Model = model,
    });

// Faz 15: KODDA tanimli workflow. Serbest graf yalnizca burada kurulabilir -
// arayuzden yalnizca hazir desenler tanimlanir (tasarim kurali K2). Bu ornek
// hazir desen fabrikasini kullanir; `WorkflowBuilder` ile ozel `Executor`
// tipleri baglamak da mumkundur.
agentPrism.AddWorkflow(
    "ozetle-ve-cevir",
    static services => AgentWorkflowBuilder.BuildSequential(
        "ozetle-ve-cevir",
        [
            // 🚨 Agent'lar `GetWorkflowAgent` ile baglanir, katalogdan DOGRUDAN
            // alinmaz. Dogrudan alinan agent kendi kok `runs` satirini acar ve
            // workflow agaci bos gorunur. Olculdu: ornek uygulamada agac uc
            // satir yerine bir satir dondu (bkz. docs/15-WORKFLOWS-YURUTME.md).
            services.GetWorkflowAgent("ozetle-ve-cevir", "ozetleyici", "Gelen metni uc maddede ozetler."),
            services.GetWorkflowAgent("ozetle-ve-cevir", "cevirmen", "Gelen metni Ingilizceye cevirir."),
        ]),
    "Metni ozetler, sonra Ingilizceye cevirir. Kodda tanimlidir.");

// Faz 16: insan girdisi bekleyen workflow. Graf bir DIS ISTEK PORTUNA ulasinca
// yurutme durur, durumu bir kontrol noktasina yazilir ve calistirma
// `AwaitingInput` olarak kapanir. Yanit
// `POST /api/workflows/runs/{runId}/respond` ile verilir ve YENI bir
// calistirma acar - olay akisi append-only'dir (K-014).
agentPrism.AddWorkflow(
    "ozetle-ve-onayla",
    static services =>
    {
        var port = RequestPort.Create<string, bool>("yayin-onayi");

        var summarize = services.GetWorkflowAgent(
            "ozetle-ve-onayla",
            "ozetleyici",
            "Gelen metni uc maddede ozetler.");

        // 🚨 Agent ile port arasina bir CEVIRICI konur. Agent host'u
        // `List<ChatMessage>` yayar, port ise `string` bekler; ikisi dogrudan
        // baglanirsa port cagirilir ama mesaji ISLEYEMEZ ve hicbir istek
        // uretmez - calistirma sessizce cikti uretmeden "tamamlandi" olur.
        // Olculdu (Faz 16): port uc kez cagrildi, sifir RequestInfoEvent.
        var ask = ExecutorBindingExtensions.BindAsExecutor(
            static (List<ChatMessage> messages) =>
                "Bu ozet yayinlansin mi?" + Environment.NewLine + Environment.NewLine +
                (messages.LastOrDefault(static message => !string.IsNullOrWhiteSpace(message.Text))?.Text
                 ?? string.Empty),
            id: "onay-sorusu");

        // 🚨 Cikti tipi ISLEYICININ DONUS TIPINDEN bildirilir. Govdesinde
        // YieldOutputAsync cagiran, donusu olmayan bir isleyici hicbir cikti
        // tipi beyan etmez ve calisma aninda "Cannot output object of type ...
        // Expecting one of []" ile duser (Faz 16'da olculdu).
        var publish = ExecutorBindingExtensions.BindAsExecutor(
            static (bool approved) => approved
                ? "Ozet yayinlandi."
                : "Yayin iptal edildi; ozet arsivde birakildi.",
            id: "yayin");

        // Baglamalar birer KEZ kurulup yeniden kullanilir: her cagri yeni bir
        // nesne uretir ve kenarlar ayni dugume degil, iki ayri dugume baglanmis
        // gorunurdu.
        // 🚨 `ForwardIncomingMessages` kapatilir. Acikken agent host'u hem gelen
        // mesaji hem kendi yanitini asagi yollar; sonraki dugum IKI kez calisir
        // ve tek bir onay yerine iki ayri bekleyen istek olusur. Olculdu
        // (Faz 16): gercek bir calistirmada `/requests` iki kayit dondu.
        var summarizeBinding = new AIAgentBinding(
            summarize,
            new AIAgentHostOptions
            {
                EmitAgentResponseEvents = true,
                EmitAgentUpdateEvents = true,
                ForwardIncomingMessages = false,
            });
        var portBinding = port.BindAsExecutor(allowWrappedRequests: false);

        return new WorkflowBuilder(summarizeBinding)
            .AddEdge(summarizeBinding, ask)
            .AddEdge(ask, portBinding)
            .AddEdge(portBinding, publish)
            .WithOutputFrom(publish)
            .WithName("ozetle-ve-onayla")
            .Build();
    },
    "Metni ozetler, sonra yayin icin insan onayi bekler. Kodda tanimlidir.");

if (openRouterEnabled)
{
    // Ayni destek senaryosu, farkli saglayici. F-03'un kaniti: agent tanimi
    // yalnizca ModelBinding.Provider degistirerek OpenAI'dan tamamen farkli
    // (resmi OpenAI olmayan) bir uca yonlenir.
    agentPrism.AddAgent(new AgentDefinition
    {
        Name = "openrouter-destek",
        DisplayName = "OpenRouter Destek",
        Description = "Ayni destek senaryosu, OpenRouter uzerinden calisir.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. " +
                       "Siparis sorularinda mutlaka tool kullan.",
        Model = new ModelBinding
        {
            Provider = "openrouter",
            // OpenRouter model kimlikleri saglayici onekiyle gelir; "gpt-5.4-mini"
            // degil "openai/gpt-5.4-mini". Olculdu: docs/08-SAGLAYICI-GENISLEMESI.md.
            Model = openRouter["DefaultModel"] ?? "openai/gpt-5.4-mini",
            // OpenRouter'in kredi kontrolu max_tokens'i "en kotu durum" olarak
            // hesaba katar; varsayilan (65536) dusuk bakiyeli anahtarlarda
            // HTTP 402 uretir. Olculdu: docs/08-SAGLAYICI-GENISLEMESI.md.
            MaxOutputTokens = 512,
        },
        ToolNames = ["get_order_status", "list_recent_orders", "cancel_order"],
    });
}

// Kalicilik istege baglidir. Baglanti dizesi yoksa uygulama bellek ici
// depolarla calisir; hicbir sey kirilmaz, yalnizca veri surecle birlikte biter.
var postgreSql = builder.Configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName);
var persistenceEnabled = !string.IsNullOrWhiteSpace(postgreSql["ConnectionString"]);

if (persistenceEnabled)
{
    agentPrism.UsePostgreSql(postgreSql);
}

// Cok kiracililik istege baglidir ve VARSAYILAN OLARAK KAPALIDIR. Acildiginda
// kiraci once claim'den, o yoksa (acikca izin verilmisse) baslikten cozulur.
// Baslik sahtelenebilir; asagidaki kurulum yalnizca ornek icindir ve
// yapilandirmadan acikca acilmadikca devreye girmez.
if (builder.Configuration.GetValue<bool>("AgentPrism:Tenancy:Enabled"))
{
    agentPrism.UseTenancy(options =>
    {
        options.Enabled = true;
        options.ClaimType = builder.Configuration["AgentPrism:Tenancy:ClaimType"];
        options.AllowHeaderResolution =
            builder.Configuration.GetValue<bool>("AgentPrism:Tenancy:AllowHeaderResolution");
    });
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", (IRunStore runs, ISessionStore sessions) => Results.Ok(new
{
    status = "healthy",
    phase = "15 - workflow yurutme",
    storage = new
    {
        persistent = persistenceEnabled,
        runStore = runs.GetType().Name,
        sessionStore = sessions.GetType().Name,
    },
    // API anahtari BURADA GORUNMEZ; yalnizca saglayicinin acik olup olmadigi bildirilir.
    provider = new
    {
        openAI = openAiEnabled,
        model = model.Model,
        name = model.Provider,
    },
    // Detayli, canli durum icin: GET /agentprism/api/models/health
    openRouter = openRouterEnabled,
}));

app.MapOpenApi();

// Tek giris noktasi. Yonetim API'si (/agentprism/api/*), OpenAI uyumlu
// calistirma uclari (/agentprism/v1/*) ve gomulu arayuz (/agentprism) bu tek
// cagriyla baglanir.
//
// Erisim varsayilan olarak loopback ile sinirlidir. Uretimde bir authorization
// policy baglanir:
//     options.RequireAuthorization("AgentPrismAdmin");
//
// Bearer token yalnizca sirlardan okunur; appsettings.json'a YAZILMAZ:
//     dotnet user-secrets set "AgentPrism:Ui:AuthToken" "..."
app.MapAgentPrism("/agentprism", options =>
{
    if (builder.Configuration["AgentPrism:Ui:AuthToken"] is { Length: > 0 } token)
    {
        options.AuthToken = token;
    }
});

app.Run();
