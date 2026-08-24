using System.Text.Json;
using System.Text.RegularExpressions;
using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// The template side of K-059/K-009: no generated file should contain a
/// realistic-looking key, and appsettings.json should carry only empty placeholders.
/// </summary>
public sealed class TemplateSecretTests(TemplateFixture fixture)
{
    // Same pattern as the DoD verification command: docs/arsiv/fazlar/37-PROJE-SABLONU.md.
    private static readonly Regex SecretLike = new(
        "sk-[a-z0-9]{20}|api[_-]?key\"\\s*:\\s*\"[^\"]+\"",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Generated_project_has_no_realistic_looking_key()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "Secret.Sample",
            dir.Path,
            "--persistence sqlserver --provider azure --ui true");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        foreach (var file in Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories))
        {
            var content = await File.ReadAllTextAsync(file);
            SecretLike.IsMatch(content).ShouldBeFalse($"'{file}' contains a realistic-looking key.");
        }
    }

    [Fact]
    public async Task AppSettings_carries_only_empty_placeholders()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "SecretSettings.Sample",
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
