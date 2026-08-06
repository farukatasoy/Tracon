using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPrism.Templates.Tests.Infrastructure;

namespace AgentPrism.Templates.Tests;

/// <summary>
/// K-059/K-009'un sablon tarafi: uretilen hicbir dosyada gercek gorunumlu bir
/// anahtar olmamali, appsettings.json yalniz bos placeholder tasimali.
/// </summary>
public sealed class TemplateSecretTests(TemplateFixture fixture)
{
    // DoD dogrulama komutuyla ayni desen: docs/37-PROJE-SABLONU.md.
    private static readonly Regex SecretLike = new(
        "sk-[a-z0-9]{20}|api[_-]?key\"\\s*:\\s*\"[^\"]+\"",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task UretilenProjede_GercekGorunumluBirAnahtarYok()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "Sir.Deneme",
            dir.Path,
            "--persistence sqlserver --provider azure --ui true");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        foreach (var file in Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(file);
            SecretLike.IsMatch(content).ShouldBeFalse($"'{file}' gercek gorunumlu bir anahtar iceriyor.");
        }
    }

    [Fact]
    public async Task AppSettings_YalnizBosPlaceholderTasir()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "SirAyar.Deneme",
            dir.Path,
            "--persistence sqlserver --provider azure --ui true");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var appSettingsPath = Path.Combine(dir.Path, "appsettings.json");
        File.Exists(appSettingsPath).ShouldBeTrue();

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(appSettingsPath));
        var agentPrism = document.RootElement.GetProperty("AgentPrism");

        agentPrism.GetProperty("SqlServer").GetProperty("ConnectionString").GetString().ShouldBe(string.Empty);

        var azureOpenAi = agentPrism.GetProperty("Providers").GetProperty("AzureOpenAI");
        azureOpenAi.GetProperty("Endpoint").GetString().ShouldBe(string.Empty);
        azureOpenAi.GetProperty("ApiKey").GetString().ShouldBe(string.Empty);
    }
}
