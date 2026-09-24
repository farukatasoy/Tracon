namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// Writes throwaway consumer projects whose Tracon graph mixes two releases, the
/// shapes a consumer produces by upgrading one Tracon package and not the rest.
/// </summary>
/// <remarks>
/// <para>
/// The local feed already holds two stamps of the same source:
/// <see cref="ReleaseArtifactFixture.Version"/> (<c>1.0.0-preview.1</c>) and the
/// MinVer height <see cref="TemplateFixture"/> packs. No third <c>dotnet pack</c>
/// is added for these shapes.
/// </para>
/// <para>
/// 🚨 nuget.org serves a DIFFERENT <c>1.0.0-preview.1</c> of the same package
/// ids. Every restore that resolves the local preview.1 runs against an isolated
/// <c>NUGET_PACKAGES</c> directory (<see cref="IsolatedPackageCache"/>) and a
/// <c>packageSourceMapping</c> that binds <c>Tracon*</c> to the local feed only;
/// without both, the global package folder keeps whichever preview.1 it saw
/// first and every later consumer on this machine reads it.
/// </para>
/// </remarks>
internal static class MixedVersionConsumerProject
{
    /// <summary>The line the web host prints after its <c>StartAsync</c> returns.</summary>
    public const string StartedLine = "MIXED-GRAPH-HOST-STARTED";

    /// <summary>
    /// Writes a <c>NuGet.config</c> that serves <c>Tracon*</c> from the local feed
    /// only and everything else from nuget.org.
    /// </summary>
    /// <param name="directory">Target directory; must already exist.</param>
    /// <param name="nugetOrgPackageIds">
    /// Tracon package ids served from nuget.org instead (a published release the
    /// local feed does not carry). An exact id outranks the <c>Tracon*</c>
    /// pattern.
    /// </param>
    public static async Task WriteNuGetConfigAsync(string directory, params string[] nugetOrgPackageIds)
    {
        var nugetOrgPatterns = string.Concat(nugetOrgPackageIds.Select(id => $"""

                  <package pattern="{id}" />
            """));

        var content = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
                <add key="tracon-local" value="{RepoPaths.PackageReleaseDirectory}" />
              </packageSources>
              <packageSourceMapping>
                <clear />
                <packageSource key="nuget.org">
                  <package pattern="*" />{nugetOrgPatterns}
                </packageSource>
                <packageSource key="tracon-local">
                  <package pattern="Tracon*" />
                </packageSource>
              </packageSourceMapping>
            </configuration>
            """;

        await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.config"), content);
    }

    /// <summary>Writes a class library that only restores the given references.</summary>
    /// <param name="directory">Target directory; must already exist.</param>
    /// <param name="references">Package id and exact version pairs.</param>
    public static Task WriteClassLibraryAsync(string directory, params (string Id, string Version)[] references)
        => File.WriteAllTextAsync(Path.Combine(directory, "MixedLibrary.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
            {PackageReferences(references)}
              </ItemGroup>

            </Project>
            """);

    /// <summary>
    /// Writes the smallest Tracon web host (<c>AddTracon</c> + <c>MapTracon</c>)
    /// that starts, prints <see cref="StartedLine"/>, and stops again, so a host
    /// that does start cannot hang the test.
    /// </summary>
    /// <param name="directory">Target directory; must already exist.</param>
    /// <param name="references">Package id and exact version pairs.</param>
    public static async Task WriteWebHostAsync(string directory, params (string Id, string Version)[] references)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "MixedHost.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk.Web">

              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
                <InvariantGlobalization>true</InvariantGlobalization>
              </PropertyGroup>

              <ItemGroup>
            {PackageReferences(references)}
              </ItemGroup>

            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), $$"""
            using Tracon;

            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.AddTracon();

            var app = builder.Build();
            app.MapTracon();

            await app.StartAsync();
            Console.WriteLine("{{StartedLine}}");
            await app.StopAsync();
            """);
    }

    private static string PackageReferences(IEnumerable<(string Id, string Version)> references)
        => string.Join(
            Environment.NewLine,
            references.Select(reference => $"""    <PackageReference Include="{reference.Id}" Version="{reference.Version}" />"""));
}

/// <summary>
/// A <c>NUGET_PACKAGES</c> directory for one test class, so a restore of the
/// local <c>1.0.0-preview.1</c> never reaches the global package folder (K-622).
/// </summary>
/// <remarks>
/// Shared by the facts of one class: the third-party packages every shape needs
/// are extracted once, and the directory is removed when the class finishes.
/// </remarks>
public sealed class IsolatedPackageCache : IDisposable
{
    private readonly TempDirectory _directory = new();

    /// <summary>The isolated package folder.</summary>
    public string Path => _directory.Path;

    /// <summary>The environment every <c>dotnet</c> call of the class runs with.</summary>
    public IReadOnlyDictionary<string, string> Environment
        => new Dictionary<string, string>(StringComparer.Ordinal) { ["NUGET_PACKAGES"] = Path };

    /// <inheritdoc />
    public void Dispose() => _directory.Dispose();
}
