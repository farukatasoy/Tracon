using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// The name given with <c>-n</c> must replace "AgentPrism.Starter" in every file
/// and file name. A rename left half-done turns into a leftover that either
/// fails to compile or references the wrong location in the generated project.
/// </summary>
public sealed class TemplateRenameTests(TemplateFixture fixture)
{
    [Fact]
    public async Task No_file_in_the_generated_project_keeps_the_template_name()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("My.Agent", dir.Path);
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        File.Exists(Path.Combine(dir.Path, "My.Agent.csproj")).ShouldBeTrue();
        File.Exists(Path.Combine(dir.Path, "AgentPrism.Starter.csproj")).ShouldBeFalse();

        foreach (var file in Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories))
        {
            Path.GetFileName(file).ShouldNotContain("AgentPrism.Starter");

            var content = await File.ReadAllTextAsync(file);
            content.Contains("AgentPrism.Starter", StringComparison.Ordinal)
                .ShouldBeFalse($"'{file}' still contains 'AgentPrism.Starter'.");
        }
    }
}
