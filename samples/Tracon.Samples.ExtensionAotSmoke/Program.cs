using Tracon;
using Tracon.Samples.CustomAgentSource;
using Tracon.Samples.CustomModelProvider;
using Tracon.Samples.CustomTool;
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

// A real generic host, started and stopped: every hosted service Tracon
// registers - the package family alignment check among them - runs under
// Native AOT exactly as it does in a deployed application. A check that
// misreads the trimmed image and refuses an aligned graph fails this smoke.
var builder = Host.CreateApplicationBuilder(args);
var tracon = builder.AddTracon()
    .AddModelProvider(new ContosoModelProvider("aot-setup-key"))
    .AddOrderPreviewTools();
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

await host.StopAsync();

if (descriptors.Count != 1 || !string.Equals(descriptors[0].Name, "aot-agent", StringComparison.Ordinal)
    || result?.ToString()?.Contains("AOT-42", StringComparison.Ordinal) is not true)
{
    return 1;
}

Console.WriteLine("provider/source/generated-tool AOT host smoke passed");
return 0;
