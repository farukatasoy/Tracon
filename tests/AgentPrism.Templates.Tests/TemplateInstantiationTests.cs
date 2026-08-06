using System.Text.RegularExpressions;
using AgentPrism.Templates.Tests.Infrastructure;

namespace AgentPrism.Templates.Tests;

/// <summary>
/// Sablonun en az iki uc noktada gercekten derlendigini dogrular: en yalin
/// (bellek ici, arayuzsuz) ve en dolu (SQL Server + Azure OpenAI + arayuz)
/// birlesim. Kutuphane degistiginde sablon sessizce kirilabilir (37.3); bu
/// testler o kirilmayi yakalayan tek gercek kapidir.
/// </summary>
public sealed class TemplateInstantiationTests(TemplateFixture fixture)
{
    private static readonly Regex WarningLine = new(
        @"^.*: warning [A-Z]+\d+:",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task EnYalinBirlesim_SifirUyariylaDerlenir()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("Yalin.Deneme", dir.Path, "--persistence memory --provider openai --ui false");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
        WarningLine.IsMatch(buildResult.Combined).ShouldBeFalse(buildResult.Combined);
    }

    [Fact]
    public async Task EnDoluBirlesim_SifirUyariylaDerlenir()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "Dolu.Deneme",
            dir.Path,
            "--persistence sqlserver --provider azure --ui true");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
        WarningLine.IsMatch(buildResult.Combined).ShouldBeFalse(buildResult.Combined);
    }
}
