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
// Bkz. docs/04-HTTP-API.md, docs/05-AGENTPRISM-UI.md
//
// Calistirmadan once sirlari ayarlayin:
//   dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Database=AgentPrism;Username=...;Password=..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."

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
        ToolNames = ["get_order_status", "list_recent_orders"],
    })

    // Harness ayarli agent: baglam sikistirma ve todo takibi devrede.
    // Shell erisimi ve arka plan agent'lari HarnessSettings icinde bilerek yoktur.
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

// Kalicilik istege baglidir. Baglanti dizesi yoksa uygulama bellek ici
// depolarla calisir; hicbir sey kirilmaz, yalnizca veri surecle birlikte biter.
var postgreSql = builder.Configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName);
var persistenceEnabled = !string.IsNullOrWhiteSpace(postgreSql["ConnectionString"]);

if (persistenceEnabled)
{
    agentPrism.UsePostgreSql(postgreSql);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", (IRunStore runs, ISessionStore sessions) => Results.Ok(new
{
    status = "healthy",
    phase = "5 - agentprism ui",
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
