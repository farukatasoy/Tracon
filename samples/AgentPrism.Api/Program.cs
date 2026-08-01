// AgentPrism ornek barindirici.
//
// Bu proje AgentPrism paketini gercek bir ASP.NET Core uygulamasinda gosterir.
// Faz 0'da yalnizca barindirici ve yapilandirma okumasi kuruludur; AgentPrism
// kayit cagrilari Faz 1'den itibaren eklenir (bkz. docs/01-CEKIRDEK-SOYUTLAMALAR.md).
//
// Calistirmadan once sirlari ayarlayin:
//   dotnet user-secrets set "AgentPrism:ConnectionString" "Host=...;Database=AgentPrism;..."
//   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "sk-..."

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Barindiricinin ayakta oldugunu ve sirlarin okunabildigini gosteren saglik ucu.
// Sir degerleri hicbir zaman dondurulmez; yalnizca var/yok bilgisi verilir.
app.MapGet("/health", (IConfiguration configuration) => Results.Ok(new
{
    status = "healthy",
    phase = "0 - altyapi",
    configuration = new
    {
        connectionStringConfigured = !string.IsNullOrWhiteSpace(configuration["AgentPrism:ConnectionString"]),
        openAiApiKeyConfigured = !string.IsNullOrWhiteSpace(configuration["AgentPrism:Providers:OpenAI:ApiKey"]),
    },
}));

app.Run();

/// <summary>
/// Fonksiyonel testlerin <c>WebApplicationFactory&lt;Program&gt;</c> ile bu barindiriciyi
/// ayaga kaldirabilmesi icin gereken acik giris noktasi tipi.
/// </summary>
public partial class Program;
