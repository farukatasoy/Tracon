// AgentPrism starter template — generated with `dotnet new agentprism-api`.
//
// Before running, follow the `dotnet user-secrets` steps in README.md.
// Secrets are NEVER written to this file; appsettings.json carries only empty placeholders.

using AgentPrism;

var builder = WebApplication.CreateBuilder(args);

var agentPrism = builder.AddAgentPrism()
    // Registers every method marked [AgentPrismTool] in this assembly at build
    // time via the source generator — no reflection, no AOT warning (OrderTools).
    // Use AddToolsFrom for tools defined in another assembly.
    .AddGeneratedTools();

// Persistence is optional. If the connection string is empty, the app runs on
// in-memory stores; nothing breaks, data just ends with the process.
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

// The provider is optional too (the "zero surprise" rule). The app still
// starts up without an API key; only runs that actually call the model
// return an error.
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

// A single declarative sample agent in code. The model name is deliberately
// NOT PINNED — AgentPrism ships no model list, so take today's model name from
// the provider's own docs and
// replace the placeholder below, or read it from the
// `AgentPrism:Providers:*:DefaultModel` setting in appsettings.json.
agentPrism.AddAgent(new AgentDefinition
{
    Name = "support",
    DisplayName = "Support Assistant",
    Description = "Answers order and shipping questions.",
    Instructions = "You are a support assistant. Answer briefly and clearly. Always use a tool for order questions.",
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
        Model = "WRITE_MODEL_NAME_HERE",
    },
    ToolNames = ["get_order_status"],
});

var app = builder.Build();

app.MapAgentPrism("/agentprism");

app.Run();
