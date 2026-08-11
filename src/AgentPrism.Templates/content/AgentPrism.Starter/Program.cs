// AgentPrism baslangic sablonu — `dotnet new agentprism-api` ile uretildi.
//
// Calistirmadan once README.md'deki `dotnet user-secrets` adimlarini uygulayin.
// Sirlar bu dosyaya ASLA yazilmaz; appsettings.json yalnizca bos placeholder tasir.

using AgentPrism;

var builder = WebApplication.CreateBuilder(args);

var agentPrism = builder.AddAgentPrism()
    // Bu derlemedeki [AgentPrismTool] isaretli tum metotlari derleme aninda
    // kaynak ureteciyle kaydeder — yansima yok, AOT uyarisi yok (OrderTools).
    // Baska bir derlemedeki tool'lar icin AddToolsFrom kullanilir.
    .AddGeneratedTools();

// Kalicilik istege baglidir. Baglanti dizesi bos ise uygulama bellek ici
// depolarla calisir; hicbir sey kirilmaz, yalnizca veri surecle birlikte biter.
#if (UsePostgres)
var postgreSql = builder.Configuration.GetSection(AgentPrismPostgreSqlOptions.SectionName);

if (!string.IsNullOrWhiteSpace(postgreSql["ConnectionString"]))
{
    agentPrism.UsePostgreSql(postgreSql);
}
#endif
#if (UseSqlServer)
var sqlServer = builder.Configuration.GetSection(AgentPrismSqlServerOptions.SectionName);

if (!string.IsNullOrWhiteSpace(sqlServer["ConnectionString"]))
{
    agentPrism.UseSqlServer(sqlServer);
}
#endif
#if (UseSqlite)
var sqlite = builder.Configuration.GetSection(AgentPrismSqliteOptions.SectionName);

if (!string.IsNullOrWhiteSpace(sqlite["ConnectionString"]))
{
    agentPrism.UseSqlite(sqlite);
}
#endif

// Saglayici da istege baglidir ("sifir surpriz" kurali). API anahtari
// tanimlanmadan uygulama yine ayaga kalkar; yalnizca modeli gercekten
// cagiran calistirmalar hata doner.
#if (UseOpenAI)
var openAi = builder.Configuration.GetSection(OpenAIProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(openAi["ApiKey"]))
{
    agentPrism.UseOpenAI(openAi);
}
#endif
#if (UseAnthropic)
var anthropic = builder.Configuration.GetSection(AnthropicProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(anthropic["ApiKey"]))
{
    agentPrism.UseAnthropic(anthropic);
}
#endif
#if (UseGoogle)
var google = builder.Configuration.GetSection(GoogleProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(google["ApiKey"]))
{
    agentPrism.UseGoogle(google);
}
#endif
#if (UseAzure)
var azureOpenAI = builder.Configuration.GetSection(AzureOpenAIProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(azureOpenAI["Endpoint"]) && !string.IsNullOrWhiteSpace(azureOpenAI["ApiKey"]))
{
    agentPrism.UseAzureOpenAI(azureOpenAI);
}
#endif

#if (ui)
agentPrism.UseUI();
#endif

// Kodda bildirimsel tek bir ornek agent. Model adi kasitli olarak
// SABITLENMEDI (K-032) — bugunku model adini saglayicinin belgesinden alip
// asagidaki placeholder'i degistirin veya appsettings.json'daki
// `AgentPrism:Providers:*:DefaultModel` ayarindan okuyun.
agentPrism.AddAgent(new AgentDefinition
{
    Name = "support",
    DisplayName = "Destek Asistani",
    Description = "Siparis ve kargo sorularini yanitlar.",
    Instructions = "Sen bir destek asistanisin. Kisa ve net yanit ver. Siparis sorularinda mutlaka tool kullan.",
    Model = new ModelBinding
    {
#if (UseOpenAI)
        Provider = OpenAIProviderNames.ChatCompletions,
#endif
#if (UseAnthropic)
        Provider = AnthropicProviderNames.Anthropic,
#endif
#if (UseGoogle)
        Provider = GoogleProviderNames.Google,
#endif
#if (UseAzure)
        Provider = AzureOpenAIProviderNames.AzureOpenAI,
#endif
        Model = "MODEL_ADINI_BURAYA_YAZIN",
    },
    ToolNames = ["get_order_status"],
});

var app = builder.Build();

app.MapAgentPrism("/agentprism");

app.Run();
