using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// A consumer on a framework the packages no longer ship is refused at restore,
/// and the refusal names the framework that is supported.
/// </summary>
/// <remarks>
/// <c>net8.0</c> and <c>net9.0</c> were dropped from every package. A consumer still
/// on one of them must learn that from NuGet's own <c>NU1202</c> at restore - the
/// error the compatibility page and the troubleshooting page point to. The three
/// packages below restored WITHOUT an error on <c>net8.0</c> when this was first
/// measured: the meta package carried no framework-specific asset, and the other
/// two carried their <c>.targets</c> at the <c>buildTransitive/</c> root. The
/// consumer then failed only at compile time, with no Tracon type in scope.
/// </remarks>
public sealed class RetiredFrameworkConsumerTests(TemplateFixture fixture)
{
    [Theory]
    [InlineData("Tracon", "net8.0")]
    [InlineData("Tracon", "net9.0")]
    [InlineData("Tracon.Core", "net8.0")]
    [InlineData("Tracon.Core", "net9.0")]
    [InlineData("Tracon.AspNetCore", "net8.0")]
    [InlineData("Tracon.AspNetCore", "net9.0")]
    public async Task A_consumer_on_a_dropped_framework_is_refused_at_restore(string packageId, string framework)
    {
        using var dir = new TempDirectory();

        await TemplateFixture.WriteLocalNuGetConfigAsync(dir.Path);
        await File.WriteAllTextAsync(Path.Combine(dir.Path, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">

              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>{framework}</TargetFramework>
              </PropertyGroup>

              <ItemGroup>
                <PackageReference Include="{packageId}" Version="{fixture.Version}" />
              </ItemGroup>

            </Project>
            """);

        var restore = await ProcessRunner.RunAsync("dotnet", "restore", dir.Path, TimeSpan.FromMinutes(5));

        restore.ExitCode.ShouldNotBe(0, restore.Combined);
        restore.Combined.ShouldContain("NU1202", customMessage: restore.Combined);
        restore.Combined.ShouldContain("net10.0", customMessage: restore.Combined);
    }
}
