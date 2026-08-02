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
// HTTP katmani hala gecici: Faz 4'te tek bir `app.MapAgentPrism("/agentprism")`
// cagrisi asagidaki uclarin yerini alir. Bkz. docs/04-HTTP-API.md
//
// Calistirmadan once sirlari ayarlayin:
//   dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "Host=localhost;Database=AgentPrism;Username=...;Password=..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."

using AgentPrism;
using AgentPrism.Api;
using Microsoft.Agents.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

var agentPrism = builder.AddAgentPrism()
    // Tool'lar YALNIZCA kodda tanimlanir. Arayuz (Faz 5) bu listeden secim
    // yaptirir; tool kodu yazdirmaz. Bu bir guvenlik sinirdir.
    // [AgentPrismTool] ile isaretlenmemis metotlar taranmaz.
    .AddToolsFrom(typeof(OrderTools));

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
    phase = "3 - saglayici ve derleyici",
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

// --- Gecici tanitim uclari. Faz 4'te app.MapAgentPrism() bunlarin yerini alir. ---

app.MapGet("/agents", async (IAgentCatalog catalog, CancellationToken cancellationToken)
    => Results.Ok(await catalog.ListAsync(cancellationToken)));

app.MapGet("/tools", (IToolRegistry tools) => Results.Ok(tools.List()));

app.MapGet("/models", (IModelProviderRegistry models) => Results.Ok(models.List()));

app.MapPost("/agents/{name}/run", async (
    string name,
    RunRequest request,
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    CancellationToken cancellationToken) =>
{
    var agent = await catalog.ResolveAsync(name, cancellationToken);

    if (agent is null)
    {
        return Results.NotFound(new { message = $"'{name}' adinda bir agent bulunamadi." });
    }

    // Oturum kimligi verilmemisse oturumsuz calis: gecmis tasinmaz.
    if (string.IsNullOrWhiteSpace(request.SessionId))
    {
        var single = await agent.RunAsync(request.Message, cancellationToken: cancellationToken);
        return Results.Ok(new { text = single.Text, sessionId = (string?)null });
    }

    AgentSession session = await sessions.GetOrCreateSessionAsync(agent, request.SessionId, cancellationToken);
    var response = await agent.RunAsync(request.Message, session, cancellationToken: cancellationToken);

    await sessions.SaveSessionAsync(agent, session, cancellationToken);

    return Results.Ok(new { text = response.Text, sessionId = request.SessionId });
});

app.MapGet("/sessions", async (AgentSessionManager sessions, CancellationToken cancellationToken)
    => Results.Ok(await sessions.QuerySessionsAsync(new SessionQuery(), cancellationToken)));

app.MapDelete("/sessions/{sessionId}", async (
    string sessionId,
    AgentSessionManager sessions,
    CancellationToken cancellationToken)
    => await sessions.DeleteSessionAsync(sessionId, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound());

app.MapGet("/runs", async (IRunStore runs, CancellationToken cancellationToken)
    => Results.Ok(await runs.QueryRunsAsync(new RunQuery(), cancellationToken)));

app.MapGet("/runs/{runId:guid}/events", (Guid runId, IRunStore runs, CancellationToken cancellationToken)
    => runs.ReadEventsAsync(runId, cancellationToken: cancellationToken));

app.Run();

/// <summary>
/// Fonksiyonel testlerin <c>WebApplicationFactory&lt;Program&gt;</c> ile bu barindiriciyi
/// ayaga kaldirabilmesi icin gereken acik giris noktasi tipi.
/// </summary>
public partial class Program;
