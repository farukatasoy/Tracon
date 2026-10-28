using System.Text.Json;
using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The template side of K-059/K-009: no generated file should contain a
/// realistic-looking key, and appsettings.json should carry only empty placeholders.
/// </summary>
public sealed class TemplateSecretTests(TemplateFixture fixture)
{
    // Same pattern as the DoD verification command: Phase 37.
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

    // The guide and the template README say there is no `dotnet user-secrets init` step: the project
    // file already carries the id, and the template engine gives every generated project its own.
    [Fact]
    public async Task Generated_project_carries_its_own_UserSecretsId()
    {
        using var first = new TempDirectory();
        using var second = new TempDirectory();

        (await fixture.NewAsync("Secrets.First", first.Path)).ExitCode.ShouldBe(0);
        (await fixture.NewAsync("Secrets.Second", second.Path)).ExitCode.ShouldBe(0);

        var firstId = ReadUserSecretsId(Path.Combine(first.Path, "Secrets.First.csproj"));
        var secondId = ReadUserSecretsId(Path.Combine(second.Path, "Secrets.Second.csproj"));

        firstId.ShouldNotBeNullOrWhiteSpace("Without a UserSecretsId, `dotnet user-secrets set` fails on the first step the guide gives.");
        firstId.ShouldNotContain("10000000-0000-0000-0000-000000000001", Case.Sensitive, "The template's own GUID must be replaced on every generation.");
        string.Equals(firstId, secondId, StringComparison.Ordinal).ShouldBeFalse("Two generated projects share one user-secrets store.");
    }

    private static string ReadUserSecretsId(string projectPath)
        => System.Xml.Linq.XDocument.Load(projectPath).Descendants("UserSecretsId").Single().Value;

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
        var tracon = document.RootElement.GetProperty("Tracon");

        tracon.GetProperty("SqlServer").GetProperty("ConnectionString").GetString().ShouldBe(string.Empty);

        var azureOpenAi = tracon.GetProperty("Providers").GetProperty("AzureOpenAI");
        azureOpenAi.GetProperty("Endpoint").GetString().ShouldBe(string.Empty);
        azureOpenAi.GetProperty("ApiKey").GetString().ShouldBe(string.Empty);

        // The model name is configuration but not a secret: with no --model it is an empty
        // placeholder too, and nothing else sits next to the secrets.
        azureOpenAi.GetProperty("DefaultDeployment").GetString().ShouldBe(string.Empty);
        azureOpenAi.EnumerateObject().Select(property => property.Name)
            .ShouldBe(["Endpoint", "ApiKey", "DefaultDeployment"], ignoreOrder: true);
    }
}
