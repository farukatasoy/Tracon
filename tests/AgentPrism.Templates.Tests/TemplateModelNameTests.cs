using AgentPrism.Templates.Tests.Infrastructure;

namespace AgentPrism.Templates.Tests;

/// <summary>
/// K-032: the model catalog comes from configuration, never a list built into
/// the code. The generated Program.cs must NOT HARD-CODE a model name — it
/// must only carry an explicit placeholder.
/// </summary>
public sealed class TemplateModelNameTests(TemplateFixture fixture)
{
    // Known provider model name prefixes. The template must not write ANY of
    // these as a real value in the generated code.
    private static readonly string[] KnownModelPrefixes =
    [
        "gpt-", "claude-", "gemini-", "o1-", "o3-", "text-embedding-",
    ];

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("google")]
    [InlineData("azure")]
    public async Task Generated_ProgramCs_carries_no_hardcoded_model_name(string provider)
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync($"Model.{provider}", dir.Path, $"--provider {provider}");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var programCs = await File.ReadAllTextAsync(Path.Combine(dir.Path, "Program.cs"));

        programCs.ShouldContain("WRITE_MODEL_NAME_HERE");

        var lowered = programCs.ToLowerInvariant();

        foreach (var prefix in KnownModelPrefixes)
        {
            lowered.ShouldNotContain(prefix);
        }
    }
}
