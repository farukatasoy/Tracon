using Microsoft.Agents.AI;
using Microsoft.Extensions.VectorData;
using Tracon;
using Tracon.Samples.CustomAgentSource;
using Tracon.Samples.CustomModelProvider;
using Tracon.Samples.CustomTool;
using Tracon.Samples.ExtensionAotSmoke;
using Tracon.Tests.Common;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var directory = Directory.CreateTempSubdirectory("tracon-extension-aot-");
await File.WriteAllTextAsync(
    Path.Combine(directory.FullName, "aot-agent.json"),
    """
    {
      "name": "aot-agent",
      "instructions": "Reply briefly.",
      "model": { "provider": "contoso", "model": "contoso-large" }
    }
    """);
await File.WriteAllTextAsync(
    Path.Combine(directory.FullName, "memory-agent.json"),
    """
    {
      "name": "memory-agent",
      "instructions": "Reply briefly.",
      "model": { "provider": "contoso", "model": "contoso-large" },
      "memory": { "enableConversationMemory": true }
    }
    """);

// A real generic host, started and stopped: every hosted service Tracon
// registers - the package family alignment check among them - runs under
// Native AOT exactly as it does in a deployed application. A check that
// misreads the trimmed image and refuses an aligned graph fails this smoke.
var builder = Host.CreateApplicationBuilder(args);
var tracon = builder.AddTracon()
    .AddModelProvider(new ContosoModelProvider("aot-setup-key"))
    .AddOrderPreviewTools()
    .UseConversationMemory(static options => options.VectorDimensions = 8);

// Conversation memory (phase 233): the host's own vector store and the user a run belongs to.
builder.Services.AddSingleton<VectorStore>(new InMemoryVectorStore());
builder.Services.AddSingleton<IRunAttributionContext>(new FixedUser("aot-user"));
builder.Services.AddSingleton(new JsonFileAgentSourceOptions { Directory = directory.FullName });
tracon.AddAgentSource<JsonFileAgentSource>();

using var host = builder.Build();
await host.StartAsync();

var source = host.Services.GetServices<IAgentSource>().OfType<JsonFileAgentSource>().Single();
var descriptors = await source.ListAsync();
var tool = host.Services.GetServices<TraconToolRegistration>()
    .Select(static registration => registration.Function)
    .OfType<AIFunction>()
    .Single(static function => string.Equals(function.Name, "preview_order", StringComparison.Ordinal));
var result = await tool.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)
{
    ["orderId"] = "AOT-42",
});

// Conversation memory under Native AOT: what the user says in one session reaches the model in another,
// and the Contoso model echoes the last message it is sent, which is the memory.
var catalog = host.Services.GetRequiredService<IAgentCatalog>();
var sessions = host.Services.GetRequiredService<AgentSessionManager>();
var memoryAgent = await catalog.ResolveAsync("memory-agent", culture: null, CancellationToken.None)
    ?? throw new InvalidOperationException("The memory agent was not found.");

var first = await sessions.GetOrCreateSessionAsync(memoryAgent, "aot-session-1");
_ = await memoryAgent.RunAsync("My favourite colour is teal.", first);
await sessions.SaveSessionAsync(memoryAgent, first);

var second = await sessions.GetOrCreateSessionAsync(memoryAgent, "aot-session-2");
var recalled = await memoryAgent.RunAsync("What is my favourite colour?", second);

await host.StopAsync();

if (descriptors.Count != 2 || !string.Equals(descriptors[0].Name, "aot-agent", StringComparison.Ordinal)
    || result?.ToString()?.Contains("AOT-42", StringComparison.Ordinal) is not true)
{
    return 1;
}

if (!recalled.Text.Contains("teal", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine($"conversation memory did not reach the model under Native AOT: '{recalled.Text}'");

    return 1;
}

Console.WriteLine("provider/source/generated-tool/conversation-memory AOT host smoke passed");
return 0;
