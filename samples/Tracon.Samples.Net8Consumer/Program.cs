// A console application on .NET 8 that takes Tracon.Core from a NuGet feed and
// runs one agent end to end: generic host, configuration, a model provider, an
// agent definition, the agent catalog, and a real run through Tracon's
// recording pipeline. The model makes no network call.

using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tracon;
using Tracon.Samples.Net8Consumer;

if (Environment.Version.Major != 8)
{
    Console.Error.WriteLine($"This sample proves the net8.0 build; it is running on {RuntimeInformation.FrameworkDescription}.");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);

builder.AddTracon()
    .AddModelProvider(new ReplyModelProvider())
    .AddAgent(new AgentDefinition
    {
        Name = "net8-agent",
        Instructions = "Reply briefly.",
        Model = new ModelBinding { Provider = ReplyModelProvider.ProviderName, Model = ReplyModelProvider.ModelName },
    });

using var host = builder.Build();
await host.StartAsync();

var catalog = host.Services.GetRequiredService<IAgentCatalog>();
var agent = await catalog.ResolveAsync("net8-agent", culture: null, CancellationToken.None);

if (agent is null)
{
    Console.Error.WriteLine("The agent catalog did not resolve 'net8-agent'.");
    return 1;
}

var response = await agent.RunAsync("ping from net8");

await host.StopAsync();

if (!response.Text.Contains("ping from net8", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"Unexpected reply: '{response.Text}'.");
    return 1;
}

Console.WriteLine($"net8.0 consumer smoke passed on {RuntimeInformation.FrameworkDescription}: {response.Text}");
return 0;
