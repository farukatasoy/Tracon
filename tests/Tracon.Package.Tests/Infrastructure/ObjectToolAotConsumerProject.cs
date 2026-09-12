namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// Writes a standalone, Native-AOT-published consumer project whose one tool
/// takes an object parameter (135.1) - proves the row of 135's DoD that a
/// generator unit test cannot: <c>System.Text.Json.JsonSerializer.Deserialize(
/// JsonElement, JsonTypeInfo)</c>, the call 135.5's binding code emits, has the
/// metadata it needs to run WITHOUT reflection once trimmed, from a real
/// <c>PackageReference</c> consumer - not just a <c>ProjectReference</c>
/// in-solution build, which never crosses the packed <c>analyzers/dotnet/cs/</c>
/// boundary the way <see cref="ConsumerRunTests"/> and
/// <see cref="ConstrainedToolPackageTests"/> already do for a scalar tool.
/// </summary>
internal static class ObjectToolAotConsumerProject
{
    public const string ExitedOkPrefix = "OK ";

    /// <param name="version">The packed version of every Tracon package (shared across the solution).</param>
    /// <param name="directory">Target directory; must already exist.</param>
    public static async Task WriteAsync(string version, string directory)
    {
        await TemplateFixture.WriteLocalNuGetConfigAsync(directory);

        await File.WriteAllTextAsync(Path.Combine(directory, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
                <PublishAot>true</PublishAot>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Tracon" Version="{version}" />
              </ItemGroup>

            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "RubricTools.cs"), """
            using System.ComponentModel;
            using System.Text.Json.Serialization;
            using Tracon;

            namespace Consumer;

            /// <summary>
            /// The object parameter this smoke test exercises. Every type in its graph
            /// must be declared with [JsonSerializable] on the SAME context the tool
            /// points at (APG0011) - this compiling and running at all, trimmed under
            /// Native AOT, is the proof that binding never falls back to reflection.
            /// </summary>
            public sealed record Rubric(string Name, int Weight);

            [JsonSerializable(typeof(Rubric))]
            internal sealed partial class RubricJsonContext : JsonSerializerContext;

            internal static class RubricTools
            {
                [TraconTool("score_rubric", "Scores a rubric.", JsonSerializerContext = typeof(RubricJsonContext))]
                public static string ScoreRubric([Description("The rubric.")] Rubric rubric)
                    => $"{rubric.Name}:{rubric.Weight}";
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), """
            using System.Text.Json;
            using Consumer;
            using Microsoft.Extensions.AI;

            var registration = Tracon.Generated.TraconGeneratedTools.Create()
                .Single(r => r.Function.Name == "score_rubric");

            var schemaText = registration.Function.JsonSchema.GetRawText();

            if (!schemaText.Contains("\"type\":\"object\",\"properties\":{\"Name\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Generated schema is missing the nested object node: {schemaText}");
            }

            var tool = (AIFunction)registration.Function;
            var argument = JsonDocument.Parse("{\"Name\":\"Clarity\",\"Weight\":5}").RootElement;

            var result = await tool.InvokeAsync(new AIFunctionArguments(StringComparer.Ordinal)
            {
                ["rubric"] = argument,
            });

            if (!string.Equals(result?.ToString(), "Clarity:5", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected tool result: {result}");
            }

            Console.WriteLine($"OK {result}");
            """);
    }
}
