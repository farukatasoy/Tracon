using Tracon;
using Tracon.Samples.CustomAgentSource;
using Tracon.Samples.CustomModelProvider;
using Tracon.Samples.CustomTool;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

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

var services = new ServiceCollection();
var builder = services
    .AddTracon()
    .AddModelProvider(new ContosoModelProvider("aot-setup-key"))
    .AddOrderPreviewTools();
builder.Services.AddSingleton(new JsonFileAgentSourceOptions { Directory = directory.FullName });
builder.AddAgentSource<JsonFileAgentSource>();

await using var provider = services.BuildServiceProvider();
var source = provider.GetServices<IAgentSource>().OfType<JsonFileAgentSource>().Single();
var descriptors = await source.ListAsync();
var tool = provider.GetServices<TraconToolRegistration>()
    .Select(static registration => registration.Function)
    .OfType<AIFunction>()
    .Single(static function => string.Equals(function.Name, "preview_order", StringComparison.Ordinal));
var result = await tool.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)
{
    ["orderId"] = "AOT-42",
});

if (descriptors.Count != 1 || !string.Equals(descriptors[0].Name, "aot-agent", StringComparison.Ordinal)
    || result?.ToString()?.Contains("AOT-42", StringComparison.Ordinal) is not true)
{
    return 1;
}

Console.WriteLine("provider/source/generated-tool AOT smoke passed");
return 0;
