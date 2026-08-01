// AgentPrism ornek barindirici — Faz 1.
//
// Bu proje AgentPrism'in su anki yeteneklerini gercek bir ASP.NET Core
// uygulamasinda gosterir: tool kaydi, kod agent'i, katalog, calistirma kaydi.
//
// Faz 1'de veritabani ve HTTP katmani henuz yok. Depolama bellek icindedir
// (AgentPrism'in "sifir surpriz" kurali) ve asagidaki uclar gecicidir —
// Faz 4'te tek bir `app.MapAgentPrism("/agentprism")` cagrisi bunlarin
// yerini alir. Bkz. docs/04-HTTP-API.md
//
// Calistirmadan once sirlari ayarlayin (Faz 2 ve 3'te gerekli olacak):
//   dotnet user-secrets set "AgentPrism:ConnectionString" "Host=...;Database=AgentPrism;..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."

using AgentPrism;
using AgentPrism.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

builder.AddAgentPrism()
    // Tool'lar YALNIZCA kodda tanimlanir. Arayuz (Faz 5) bu listeden secim
    // yaptirir; tool kodu yazdirmaz. Bu bir guvenlik sinirdir.
    .AddTool(OrderTools.GetOrderStatus, name: "get_order_status", description: "Bir siparisin kargo durumunu dondurur.")
    .AddTool(OrderTools.ListRecentOrders, name: "list_recent_orders", description: "Musterinin son siparislerini listeler.")

    // Faz 3'te bunun yerini `UseOpenAI(apiKey)` alacak.
    .AddModelProvider(new EchoModelProvider())

    // Kodda bildirimsel agent. Katalogda "code" kaynagi ile gorunur ve
    // ayni ada sahip bir veritabani tanimina karsi oncelik kazanir.
    .AddAgent(new AgentDefinition
    {
        Name = "support",
        DisplayName = "Destek Asistani",
        Description = "Siparis ve kargo sorularini yanitlar.",
        Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver.",
        Model = new ModelBinding { Provider = "echo", Model = "echo-1" },
        ToolNames = ["get_order_status", "list_recent_orders"],
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", (IConfiguration configuration) => Results.Ok(new
{
    status = "healthy",
    phase = "1 - cekirdek soyutlamalar",
    configuration = new
    {
        connectionStringConfigured = !string.IsNullOrWhiteSpace(configuration["AgentPrism:ConnectionString"]),
        openAiApiKeyConfigured = !string.IsNullOrWhiteSpace(configuration["AgentPrism:Providers:OpenAI:ApiKey"]),
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
    CancellationToken cancellationToken) =>
{
    var agent = await catalog.ResolveAsync(name, cancellationToken);

    if (agent is null)
    {
        return Results.NotFound(new { message = $"'{name}' adinda bir agent bulunamadi." });
    }

    var response = await agent.RunAsync(request.Message, cancellationToken: cancellationToken);
    return Results.Ok(new { text = response.Text });
});

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
