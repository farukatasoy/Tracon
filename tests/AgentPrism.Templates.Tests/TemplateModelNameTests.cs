using AgentPrism.Templates.Tests.Infrastructure;

namespace AgentPrism.Templates.Tests;

/// <summary>
/// K-032: model kataloğu yapılandırmadan gelir, kodda yerleşik liste olmaz.
/// Üretilen Program.cs bir model adı SABİTLEMEMELİDİR — yalnız açık bir
/// placeholder taşımalıdır.
/// </summary>
public sealed class TemplateModelNameTests(TemplateFixture fixture)
{
    // Bilinen saglayici model adi onekleri. Sablon bunlardan HICBIRINI
    // uretilen kodda gercek bir deger olarak yazmamali.
    private static readonly string[] KnownModelPrefixes =
    [
        "gpt-", "claude-", "gemini-", "o1-", "o3-", "text-embedding-",
    ];

    [Theory]
    [InlineData("openai")]
    [InlineData("anthropic")]
    [InlineData("google")]
    [InlineData("azure")]
    public async Task UretilenProgramCs_SabitlenmisModelAdiTasimaz(string provider)
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync($"Model.{provider}", dir.Path, $"--provider {provider}");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var programCs = await File.ReadAllTextAsync(Path.Combine(dir.Path, "Program.cs"));

        programCs.ShouldContain("MODEL_ADINI_BURAYA_YAZIN");

        var lowered = programCs.ToLowerInvariant();

        foreach (var prefix in KnownModelPrefixes)
        {
            lowered.ShouldNotContain(prefix);
        }
    }
}
