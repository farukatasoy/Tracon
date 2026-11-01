using Microsoft.Agents.AI;

namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// A script or resource of a skill on disk that is a link out of the skill's directory is
/// neither offered to the model nor readable.
/// </summary>
/// <remarks>
/// <para>
/// The runner follows a link to its final target when it checks a file script's size, so the
/// containment is the file source's: Microsoft Agent Framework scans the skill directory and
/// leaves a link out (measured on 1.20.0). The guard lives in a dependency, so these cases pin
/// it and fail on an upgrade that stops enforcing it.
/// </para>
/// <para>
/// A link needs the operating system to allow creating one; where it does not, the case ends
/// without a verdict rather than failing.
/// </para>
/// </remarks>
public sealed class FileSkillLinkTests : IDisposable
{
    private readonly DirectoryInfo _scratch = Directory.CreateTempSubdirectory("file-skill-link-");

    [Fact]
    public async Task A_script_that_is_a_link_out_of_the_skill_directory_is_not_offered()
    {
        var (skill, content) = await ScanAsync();

        (await skill.GetScriptAsync("scripts/inside.sh", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await skill.GetScriptAsync("scripts/link.sh", TestContext.Current.CancellationToken)).ShouldBeNull();
        content.ShouldContain("scripts/inside.sh", Case.Sensitive);
        content.ShouldNotContain("scripts/link.sh", Case.Sensitive);
    }

    [Fact]
    public async Task A_resource_that_is_a_link_out_of_the_skill_directory_is_not_offered()
    {
        var (skill, content) = await ScanAsync();

        (await skill.GetResourceAsync("references/inside.md", TestContext.Current.CancellationToken)).ShouldNotBeNull();
        (await skill.GetResourceAsync("references/leak.md", TestContext.Current.CancellationToken)).ShouldBeNull();
        content.ShouldNotContain("references/leak.md", Case.Sensitive);
    }

    public void Dispose() => _scratch.Delete(recursive: true);

    private async Task<(AgentFileSkill Skill, string Content)> ScanAsync()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_scratch.FullName, "outside"));
        File.WriteAllText(Path.Combine(outside.FullName, "secret.sh"), "echo outside\n");
        File.WriteAllText(Path.Combine(outside.FullName, "secret.md"), "outside");

        var root = Path.Combine(_scratch.FullName, "skills");
        var skillDirectory = Path.Combine(root, "linked");
        Directory.CreateDirectory(Path.Combine(skillDirectory, "scripts"));
        Directory.CreateDirectory(Path.Combine(skillDirectory, "references"));
        File.WriteAllText(
            Path.Combine(skillDirectory, "SKILL.md"),
            "---\nname: linked\ndescription: A skill with links that leave its directory.\n---\nBody.\n");
        File.WriteAllText(Path.Combine(skillDirectory, "scripts", "inside.sh"), "echo inside\n");
        File.WriteAllText(Path.Combine(skillDirectory, "references", "inside.md"), "inside");

        try
        {
            File.CreateSymbolicLink(Path.Combine(skillDirectory, "scripts", "link.sh"), Path.Combine(outside.FullName, "secret.sh"));
            File.CreateSymbolicLink(Path.Combine(skillDirectory, "references", "leak.md"), Path.Combine(outside.FullName, "secret.md"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("This system does not allow creating a symbolic link.");
        }

        using var source = new AgentFileSkillsSource(
            [root],
            static (_, _, _, _, _) => Task.FromResult<object?>(null),
            new AgentFileSkillsSourceOptions
            {
                AllowedScriptExtensions = [".sh"],
                AllowedResourceExtensions = [".md"],
            },
            loggerFactory: null);

        var skills = await source.GetSkillsAsync(null!, TestContext.Current.CancellationToken);
        var skill = skills.ShouldHaveSingleItem().ShouldBeOfType<AgentFileSkill>();

        return (skill, await skill.GetContentAsync(TestContext.Current.CancellationToken));
    }
}
