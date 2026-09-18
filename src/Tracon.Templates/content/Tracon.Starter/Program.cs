// Tracon starter template — generated with `dotnet new tracon-api`.
//
// Before running, follow the `dotnet user-secrets` steps in README.md.
// Secrets are NEVER written to this file; appsettings.json carries only empty placeholders.

using Tracon;

var builder = WebApplication.CreateBuilder(args);

var tracon = builder.AddTracon()
    // Registers every method marked [TraconTool] in this assembly at build
    // time via the source generator — no reflection, no AOT warning (OrderTools).
    // Use AddToolsFrom for tools defined in another assembly.
    .AddGeneratedTools();

// Persistence is optional. If the connection string is empty, the app runs on
// in-memory stores; nothing breaks, data just ends with the process.
#if (UsePostgres)
var postgreSql = builder.Configuration.GetSection(TraconPostgreSqlOptions.SectionName);

if (!string.IsNullOrWhiteSpace(postgreSql["ConnectionString"]))
{
    tracon.UsePostgreSql(postgreSql);
}
#endif
#if (UseSqlServer)
var sqlServer = builder.Configuration.GetSection(TraconSqlServerOptions.SectionName);

if (!string.IsNullOrWhiteSpace(sqlServer["ConnectionString"]))
{
    tracon.UseSqlServer(sqlServer);
}
#endif
#if (UseSqlite)
var sqlite = builder.Configuration.GetSection(TraconSqliteOptions.SectionName);

if (!string.IsNullOrWhiteSpace(sqlite["ConnectionString"]))
{
    tracon.UseSqlite(sqlite);
}
#endif

// The provider is optional too (the "zero surprise" rule). The app still
// starts up without an API key; only runs that actually call the model
// return an error. `providerConfigured` records whether one was wired up, so
// the model-name check below can stay silent on a host that can never reach a
// provider anyway.
var providerConfigured = false;

#if (UseOpenAI)
var openAi = builder.Configuration.GetSection(OpenAIProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(openAi["ApiKey"]))
{
    tracon.UseOpenAI(openAi);
    providerConfigured = true;
}
#endif
#if (UseAnthropic)
var anthropic = builder.Configuration.GetSection(AnthropicProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(anthropic["ApiKey"]))
{
    tracon.UseAnthropic(anthropic);
    providerConfigured = true;
}
#endif
#if (UseGoogle)
var google = builder.Configuration.GetSection(GoogleProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(google["ApiKey"]))
{
    tracon.UseGoogle(google);
    providerConfigured = true;
}
#endif
#if (UseAzure)
var azureOpenAI = builder.Configuration.GetSection(AzureOpenAIProviderOptions.SectionName);

if (!string.IsNullOrWhiteSpace(azureOpenAI["Endpoint"]) && !string.IsNullOrWhiteSpace(azureOpenAI["ApiKey"]))
{
    tracon.UseAzureOpenAI(azureOpenAI);
    providerConfigured = true;
}
#endif

#if (ui)
tracon.UseUI();
#endif

// A single declarative sample agent in code. The model name is deliberately
// NOT PINNED — Tracon ships no model list, so take today's model name from
// the provider's own docs and
// replace the placeholder below, or read it from the
// `Tracon:Providers:*:DefaultModel` setting in appsettings.json.
const string ModelName = "WRITE_MODEL_NAME_HERE";

// Left in place, the placeholder is a model no provider serves, and the first
// run comes back as "The model provider request failed." — the provider's own
// 404 names the model, but that text is a foreign SDK message and is redacted
// before it reaches the client, so the first five minutes go on an error that
// names nothing. This stops at startup instead, and names the line to edit.
//
// Only when a provider is actually configured: with no API key the agent can
// never reach a model, so an unedited template still starts up and still
// answers, which is the "zero surprise" rule above and what
// TemplateRunTests measures.
if (providerConfigured && ModelName.StartsWith("WRITE_", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"Program.cs still carries the model-name placeholder ('{ModelName}'). Replace it with a " +
        "model your provider serves today — Tracon ships no model list, so the name comes from the " +
        "provider's own documentation.");
}

tracon.AddAgent(new AgentDefinition
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
        Model = ModelName,
    },
    ToolNames = ["get_order_status"],
});

var app = builder.Build();

app.MapTracon("/tracon");

app.Run();
