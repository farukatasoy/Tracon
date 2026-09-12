namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// Writes a standalone consumer project - the same shape as
/// <see cref="ConsumerProject"/> - whose one tool carries a 130.1 constraint
/// attribute (<c>[Range]</c>). Proves both halves of 130's DoD from a real
/// <c>PackageReference</c> consumer: the constraint reaches the packed
/// analyzer's generated schema, and a real run still completes with the
/// constrained argument bound correctly.
/// </summary>
internal static class ConstrainedToolConsumerProject
{
    public const string ExitedOkPrefix = "OK run=";

    /// <param name="version">The packed version of every Tracon package (shared across the solution).</param>
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
                <PackageReference Include="Tracon" Version="{version}" />
                <PackageReference Include="Tracon.Testing" Version="{version}" />
              </ItemGroup>

            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "OrderTools.cs"), """
            using System.ComponentModel;
            using System.ComponentModel.DataAnnotations;
            using Tracon;

            namespace Consumer;

            /// <summary>
            /// The tool this smoke test exercises. [Range(1, 10)] on the second
            /// parameter proves 130.1 travels through the packed analyzer DLL into a
            /// real PackageReference consumer's generated schema, not just a
            /// ProjectReference/in-solution build.
            /// </summary>
            internal static class OrderTools
            {
                [TraconTool("set_priority", "Sets an order's priority.")]
                public static string SetPriority(
                    [Description("The order number.")] string orderId,
                    [Description("The priority, 1 (lowest) to 10 (highest).")] [Range(1, 10)] int priority)
                    => $"{orderId} priority set to {priority}";
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), """
            using Tracon;
            using Tracon.Testing;
            using Microsoft.Agents.AI;
            using Consumer;

            var registration = Tracon.Generated.TraconGeneratedTools.Create()
                .Single(r => r.Function.Name == "set_priority");

            var schemaText = registration.Function.JsonSchema.GetRawText();

            if (!schemaText.Contains("\"minimum\":1", StringComparison.Ordinal) ||
                !schemaText.Contains("\"maximum\":10", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Generated schema is missing the [Range(1, 10)] constraint: {schemaText}");
            }

            var provider = new FakeModelProvider("fake")
                .CallsTool("set_priority", new { orderId = "ORD-7", priority = 5 })
                .EchoesLastToolResult();

            await using var host = await TraconTestHost.StartAsync(options =>
            {
                options.ModelProvider = provider;
                options.ConfigureTracon = tracon => tracon
                    .AddGeneratedTools()
                    .AddAgent(new AgentDefinition
                    {
                        Name = "assistant",
                        DisplayName = "Consumer Assistant",
                        Description = "Package consumer constrained-tool smoke agent.",
                        Instructions = "Answer briefly.",
                        Model = new ModelBinding { Provider = "fake", Model = "fake-1" },
                        ToolNames = ["set_priority"],
                    });
            });

            var run = await host.RunAsync("assistant", "Set priority for ORD-7 to 5.");

            run.ShouldHaveCompleted()
               .ShouldHaveCalledTool("set_priority")
               .ShouldHaveOutputContaining("priority");

            Console.WriteLine($"OK run={run.Record.Id} events={run.Events.Count} tools={run.ToolInvocations.Count}");
            Console.WriteLine($"SCHEMA {schemaText}");
            """);
    }
}
