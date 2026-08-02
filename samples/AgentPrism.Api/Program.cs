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
// Bkz. docs/04-HTTP-API.md, docs/05-AGENTPRISM-UI.md, docs/06-GOZLEMLENEBILIRLIK.md,
//      docs/08-SAGLAYICI-GENISLEMESI.md
//
// Calistirmadan once sirlari ayarlayin:
//   dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Database=AgentPrism;Username=...;Password=..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAICompatible:openrouter:ApiKey" "sk-or-..."

using AgentPrism;
using AgentPrism.Api;
using Microsoft.Agents.AI;

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
    // Dosya erisimi ve arka plan agent'lari HarnessSettings icinde bilerek
    // yoktur; ikisi de MAF'ta yalnizca deger atandiginda etkinlesir ve
    // AgentPrism o degerleri hic atamaz (karar K-062).
    .AddAgent(new AgentDefinition
    {
        Name = "arastirmaci",
        DisplayName = "Arastirmaci",
        Description = "Uzun konusmalarda baglami sikistirarak calisir.",
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
    });

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
    phase = "8 - saglayici genislemesi ve saglik denetimi",
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
