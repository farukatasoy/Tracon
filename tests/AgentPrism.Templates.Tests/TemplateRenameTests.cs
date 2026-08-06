using AgentPrism.Templates.Tests.Infrastructure;

namespace AgentPrism.Templates.Tests;

/// <summary>
/// <c>-n</c> ile verilen ad her dosyada ve dosya adinda "AgentPrism.Starter"in
/// yerini almalidir. Yarim kalan bir yeniden adlandirma, uretilen projede
/// derlenmeyen veya yanlis yerde referans veren bir kalintiya doner.
/// </summary>
public sealed class TemplateRenameTests(TemplateFixture fixture)
{
    [Fact]
    public async Task UretilenProjede_SablonAdiHicbirDosyadaKalmaz()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("Benim.Agent", dir.Path);
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        File.Exists(Path.Combine(dir.Path, "Benim.Agent.csproj")).ShouldBeTrue();
        File.Exists(Path.Combine(dir.Path, "AgentPrism.Starter.csproj")).ShouldBeFalse();

        foreach (var file in Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories))
        {
            Path.GetFileName(file).ShouldNotContain("AgentPrism.Starter");

            var content = await File.ReadAllTextAsync(file);
            content.Contains("AgentPrism.Starter", StringComparison.Ordinal)
                .ShouldBeFalse($"'{file}' hala 'AgentPrism.Starter' iceriyor.");
        }
    }
}
