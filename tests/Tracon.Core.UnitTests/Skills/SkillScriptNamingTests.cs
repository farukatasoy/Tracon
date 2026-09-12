namespace Tracon.Core.UnitTests.Skills;

/// <summary>
/// A stored script's name becomes a file name under the run's scratch directory.
/// </summary>
/// <remarks>
/// 🚨 These tests exist because of a measured defect. The name went from
/// <c>PUT /api/skills/{name}</c> into <c>Path.Combine</c> unchecked.
/// <c>Path.Combine</c> returns the SECOND part unchanged when that part is
/// rooted and it never resolves <c>..</c>, so a script named
/// <c>/etc/cron.d/tracon</c> was written outside the scratch directory,
/// executed from there, and survived cleanup - the cleanup only deletes the
/// scratch directory itself. K-087 kept the skill ROOT out of the interface's
/// reach; this closes the half where the interface supplied the target PATH.
/// </remarks>
public sealed class SkillScriptNamingTests
{
    [Theory]
    [InlineData("/etc/cron.d/tracon")]
    [InlineData("../../escape")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("nested/name")]
    [InlineData("nested\\name")]
    [InlineData("C:windows")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_name_that_carries_a_path_is_rejected(string scriptName)
    {
        SkillScriptNaming.IsSafeFileName(scriptName).ShouldBeFalse();

        Should.Throw<TraconException>(() => SkillScriptNaming.RequireSafeFileName(scriptName));
    }

    [Theory]
    [InlineData("build")]
    [InlineData("build-release")]
    [InlineData("build_release")]
    [InlineData("build.step2")]
    public void A_plain_file_name_is_accepted(string scriptName)
    {
        SkillScriptNaming.IsSafeFileName(scriptName).ShouldBeTrue();

        SkillScriptNaming.RequireSafeFileName(scriptName).ShouldBe(scriptName);
    }

    [Fact]
    public void A_rejected_name_never_leaves_the_directory_it_is_combined_with()
    {
        // The property the guard actually protects, stated directly.
        var scratch = Path.Combine(Path.GetTempPath(), "tracon-scratch");

        foreach (var candidate in new[] { "/etc/passwd", "../../etc/passwd" })
        {
            var combined = Path.GetFullPath(Path.Combine(scratch, candidate));

            combined.StartsWith(scratch, StringComparison.Ordinal).ShouldBeFalse(
                $"'{candidate}' escapes the scratch directory, which is why the name is rejected before it reaches Path.Combine.");

            SkillScriptNaming.IsSafeFileName(candidate).ShouldBeFalse();
        }
    }
}
