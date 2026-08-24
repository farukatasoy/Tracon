namespace AgentPrism.Package.Tests.Infrastructure;

/// <summary>
/// Writes a standalone consumer project into a directory: an ASP.NET Core
/// application that references the packed <c>AgentPrism</c> and
/// <c>AgentPrism.Testing</c> packages exactly the way an external consumer
/// would, then runs one real agent turn and prints the result to stdout.
/// </summary>
/// <remarks>
/// No template is used here (unlike <see cref="TemplateFixture"/>'s
/// <c>dotnet new</c> flow) — this project is written by hand so the test
/// exercises the meta package itself, independent of the template's own
/// content. The run assertions execute INSIDE the generated project's
/// <c>Program.cs</c>: the test assembly cannot reference
/// <c>AgentPrism.Testing</c> directly, because that package is built in the
/// same solution and a <c>PackageReference</c> back to it would form a cycle.
/// </remarks>
internal static class ConsumerProject
{
    public const string ExitedOkPrefix = "OK run=";

    /// <summary>Writes the consumer project to <paramref name="directory"/>.</summary>
    /// <param name="version">The packed version of every AgentPrism package (shared across the solution).</param>
    /// <param name="directory">Target directory; must already exist.</param>
    public static async Task WriteAsync(string version, string directory)
    {
        await TemplateFixture.WriteLocalNuGetConfigAsync(directory);

        await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="AgentPrism" Version="{version}" />
                <PackageReference Include="AgentPrism.Testing" Version="{version}" />
              </ItemGroup>

            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "OrderTools.cs"), """
            using AgentPrism;

            namespace Consumer;

            /// <summary>
            /// The tool this smoke test exercises. Marked with [AgentPrismTool] and
            /// registered at build time by the source generator through
            /// AddGeneratedTools() - this compiling at all proves the analyzer DLL
            /// travels inside the packed .nupkg's analyzers/dotnet/cs/ folder.
            /// </summary>
            internal static class OrderTools
            {
                [AgentPrismTool("order_status", "Returns the shipping status of an order.")]
                public static string OrderStatus(string orderId) => $"{orderId} shipped";
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), """
            using AgentPrism;
            using AgentPrism.Testing;
            using Microsoft.Agents.AI;
            using Consumer;

            var provider = new FakeModelProvider("fake")
                .CallsTool("order_status", new { orderId = "ORD-7" })
                .EchoesLastToolResult();

            await using var host = await AgentPrismTestHost.StartAsync(options =>
            {
                options.ModelProvider = provider;
                options.ConfigureAgentPrism = agentPrism => agentPrism
                    .AddGeneratedTools()
                    .AddAgent(new AgentDefinition
                    {
                        Name = "assistant",
                        DisplayName = "Consumer Assistant",
                        Description = "Package consumer smoke agent.",
                        Instructions = "Answer briefly.",
                        Model = new ModelBinding { Provider = "fake", Model = "fake-1" },
                        ToolNames = ["order_status"],
                    });
            });

            var run = await host.RunAsync("assistant", "Where is ORD-7?");

            run.ShouldHaveCompleted()
               .ShouldHaveCalledTool("order_status")
               .ShouldHaveOutputContaining("shipped");

            Console.WriteLine($"OK run={run.Record.Id} events={run.Events.Count} tools={run.ToolInvocations.Count}");
            """);
    }
}
