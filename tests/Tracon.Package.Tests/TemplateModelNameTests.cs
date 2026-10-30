using System.Text.Json;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The model catalog comes from configuration, never a list built into
/// the code. <c>dotnet new tracon-api --model &lt;name&gt;</c> writes the name to
/// <c>appsettings.json</c> as the provider's default model, and the generated
/// <c>Program.cs</c> carries no model name at all.
/// </summary>
public sealed class TemplateModelNameTests(TemplateFixture fixture)
{
    // Known provider model name prefixes. The template must not write ANY of
    // these as a real value in the generated code.
    private static readonly string[] KnownModelPrefixes =
    [
        "gpt-", "claude-", "gemini-", "o1-", "o3-", "text-embedding-",
    ];

    // Azure OpenAI names a deployment, not a model, so its setting has another name.
    [Theory]
    [InlineData("openai", "OpenAI", "DefaultModel")]
    [InlineData("anthropic", "Anthropic", "DefaultModel")]
    [InlineData("google", "Google", "DefaultModel")]
    [InlineData("azure", "AzureOpenAI", "DefaultDeployment")]
    public async Task Model_parameter_lands_in_the_providers_default_model_setting(string provider, string section, string setting)
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync($"Model.{provider}", dir.Path, $"--provider {provider} --model m-1");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        ReadSetting(dir.Path, section, setting).ShouldBe("m-1");

        var programCs = await File.ReadAllTextAsync(Path.Combine(dir.Path, "Program.cs"));

        programCs.ShouldNotContain("WRITE_", Case.Sensitive);
        programCs.ShouldNotContain("m-1", Case.Sensitive);

        var lowered = programCs.ToLowerInvariant();

        foreach (var prefix in KnownModelPrefixes)
        {
            lowered.ShouldNotContain(prefix);
        }
    }

    [Theory]
    [InlineData("openai", "OpenAI", "DefaultModel")]
    [InlineData("azure", "AzureOpenAI", "DefaultDeployment")]
    public async Task Without_the_model_parameter_the_setting_is_empty(string provider, string section, string setting)
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync($"NoModel.{provider}", dir.Path, $"--provider {provider}");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        ReadSetting(dir.Path, section, setting).ShouldBe(string.Empty);
    }

    // The value is written into a JSON file by the template engine. A name that carries a quote or
    // a backslash must still leave a valid file, and must read back unchanged.
    [Theory]
    [InlineData("\"\"", "")]
    [InlineData("\"two words\"", "two words")]
    [InlineData("\"a\\\"b\\c\"", "a\"b\\c")]
    public async Task Model_value_stays_a_valid_JSON_string(string argument, string expected)
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("Model.Json", dir.Path, $"--model {argument}");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        ReadSetting(dir.Path, "OpenAI", "DefaultModel").ShouldBe(expected);
    }

    private static string ReadSetting(string directory, string section, string setting)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "appsettings.json")));

        return document.RootElement
            .GetProperty("Tracon")
            .GetProperty("Providers")
            .GetProperty(section)
            .GetProperty(setting)
            .GetString()
            ?? throw new InvalidOperationException($"'{section}:{setting}' is null.");
    }
}
