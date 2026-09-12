namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// Writes a minimal console project that references only the packed
/// <c>Tracon</c> package, for tests that check whether a single type or
/// interface is accessible from a real <c>PackageReference</c> consumer.
/// </summary>
/// <remarks>
/// Unlike <see cref="ConsumerProject"/>, this project never runs - it only
/// needs to compile (or fail to compile). Keeping it separate from
/// <see cref="ConsumerProject"/> means a probe that is expected to fail
/// compilation cannot be confused with the one project that must always
/// build and run cleanly.
/// </remarks>
internal static class SurfaceProbeProject
{
    /// <param name="version">The packed version of every Tracon package (shared across the solution).</param>
    /// <param name="directory">Target directory; must already exist.</param>
    /// <param name="programBody">The full body of <c>Program.cs</c> for this probe.</param>
    public static async Task WriteAsync(string version, string directory, string programBody)
    {
        await TemplateFixture.WriteLocalNuGetConfigAsync(directory);

        await File.WriteAllTextAsync(Path.Combine(directory, "Probe.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="Tracon" Version="{version}" />
              </ItemGroup>

            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), programBody);
    }
}
