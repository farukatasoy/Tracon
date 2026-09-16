using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tracon.Cli.Commands;
using Tracon.Cli.FunctionalTests.Infrastructure;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// <c>tracon agent-skill</c> against a real directory tree. The command is the
/// only one that changes the caller's working tree, so every case here is
/// measured against files on disk rather than against console output alone.
/// </summary>
[Collection(nameof(CliTestGroup))]
public sealed class AgentSkillCommandTests : IDisposable
{
    /// <summary>The path the command writes, relative to the directory it is given.</summary>
    private static readonly string RelativeSkillPath =
        Path.Combine(".claude", "skills", "tracon", "SKILL.md");

    private readonly string _root = Directory.CreateTempSubdirectory("tracon-agent-skill-").FullName;

    private string SkillPath => Path.Combine(_root, RelativeSkillPath);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task The_skill_is_written_under_the_output_directory_and_carries_the_map_revision()
    {
        var result = await CliRunner.RunAsync("agent-skill", "--output", _root);

        result.ExitCode.ShouldBe(0);
        File.Exists(SkillPath).ShouldBeTrue($"{SkillPath} was not written. Output: {result.Combined}");

        var written = await File.ReadAllTextAsync(SkillPath);

        written.ShouldContain(
            GateSkillText.Marker(GateSkillText.MapRevision),
            Case.Sensitive,
            "without the stamp the build can never report the file as stale");
    }

    /// <summary>
    /// The contract this command shares with the build's <c>AGENTS.md</c>: an
    /// existing file may carry the consumer's own edits, and losing them on a
    /// routine re-run would be worse than a stale file.
    /// </summary>
    [Fact]
    public async Task Existing_file_is_never_touched()
    {
        (await CliRunner.RunAsync("agent-skill", "--output", _root)).ExitCode.ShouldBe(0);

        await File.AppendAllTextAsync(SkillPath, "\nA note the consumer added by hand.\n");
        var edited = await HashAsync(SkillPath);

        var result = await CliRunner.RunAsync("agent-skill", "--output", _root);

        result.ExitCode.ShouldBe(0, "leaving the file alone is a success, not a failure");
        (await HashAsync(SkillPath)).ShouldBe(edited);
        result.StandardOutput.ShouldContain("left untouched");
        result.StandardOutput.ShouldContain("--force", Case.Sensitive, "the message has to name the way out");
    }

    [Fact]
    public async Task Force_overwrites()
    {
        (await CliRunner.RunAsync("agent-skill", "--output", _root)).ExitCode.ShouldBe(0);
        await File.AppendAllTextAsync(SkillPath, "\nA note the consumer added by hand.\n");

        var result = await CliRunner.RunAsync("agent-skill", "--output", _root, "--force");

        result.ExitCode.ShouldBe(0);
        (await File.ReadAllTextAsync(SkillPath)).ShouldNotContain("A note the consumer added by hand.");
    }

    /// <summary>
    /// Everything the command creates has to sit under the directory it was
    /// given. Measured over the whole tree rather than over the one path the
    /// command reports, because a stray temporary file is exactly what this
    /// would miss.
    /// </summary>
    [Fact]
    public async Task Output_stays_within_the_target_directory()
    {
        var sibling = Directory.CreateTempSubdirectory("tracon-agent-skill-sibling-").FullName;

        try
        {
            var before = Directory.GetFileSystemEntries(sibling, "*", SearchOption.AllDirectories);

            (await CliRunner.RunAsync("agent-skill", "--output", _root)).ExitCode.ShouldBe(0);

            Directory.GetFileSystemEntries(sibling, "*", SearchOption.AllDirectories).ShouldBe(before);

            Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
                .ShouldBe([SkillPath], "the command writes exactly one file and leaves no temporary behind");
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_directory_is_reported_clearly()
    {
        var missing = Path.Combine(_root, "no-such-directory");

        var result = await CliRunner.RunAsync("agent-skill", "--output", missing);

        result.ExitCode.ShouldBe(1);
        result.StandardError.ShouldContain("is not an existing directory");
        result.StandardError.ShouldContain(missing);
        Directory.Exists(missing).ShouldBeFalse("a typo must not create a tree nobody looks in");
    }

    [Fact]
    public async Task An_unwritable_format_is_an_argument_error_rather_than_a_silent_claude_file()
    {
        var result = await CliRunner.RunAsync("agent-skill", "--output", _root, "--format", "cursor");

        result.ExitCode.ShouldBe(1);
        result.StandardError.ShouldContain("is not a format this version writes");
        result.StandardError.ShouldContain("claude", Case.Sensitive, "the message has to name what IS supported");
        File.Exists(SkillPath).ShouldBeFalse("a rejected format writes nothing");
    }

    /// <summary>
    /// A cancelled run creates nothing and says so.
    /// </summary>
    /// <remarks>
    /// This covers cancellation BEFORE the write only, which is the case a test
    /// can force: the framework refuses an already-cancelled write before it
    /// opens the file. Cancellation part-way through a write is handled by
    /// construction instead - the content goes to a temporary file and a move
    /// is what makes it visible - and
    /// <see cref="A_failed_write_leaves_no_temporary_file_behind"/> is what
    /// holds that machinery in place. Measured while writing these: removing
    /// the temporary file entirely left this test green, so it does not claim
    /// to cover it.
    /// </remarks>
    [Fact]
    public async Task Cancellation_before_the_write_creates_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var exitCode = await AgentSkillCommand.RunAsync(["--output", _root], cancelled.Token);

        exitCode.ShouldBe(2);
        File.Exists(SkillPath).ShouldBeFalse();
        Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty("not even the empty directory tree is left behind");
    }

    /// <summary>
    /// A write that fails at the last step reports the failure and leaves the
    /// directory as it found it.
    /// </summary>
    /// <remarks>
    /// The failure is forced the only way a test can force one deterministically:
    /// the target path is occupied by a directory, so the content reaches the
    /// temporary file and the move is what fails. Without the cleanup, the
    /// temporary file stays in the consumer's repository - the one thing a
    /// command that writes into someone else's tree must never leave behind.
    /// </remarks>
    [Fact]
    public async Task A_failed_write_leaves_no_temporary_file_behind()
    {
        Directory.CreateDirectory(SkillPath);

        var result = await CliRunner.RunAsync("agent-skill", "--output", _root);

        result.ExitCode.ShouldBe(2);
        result.StandardError.ShouldContain("Could not write");

        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .ShouldBeEmpty("a failed run leaves no temporary file in the consumer's repository");
    }

    /// <summary>
    /// Two runs at once. The move is the only step that makes a file visible,
    /// so the loser of the race cannot leave a partial file behind either.
    /// </summary>
    [Fact]
    public async Task Two_runs_at_once_leave_one_complete_file()
    {
        var first = AgentSkillCommand.RunAsync(["--output", _root, "--force"], CancellationToken.None);
        var second = AgentSkillCommand.RunAsync(["--output", _root, "--force"], CancellationToken.None);

        (await Task.WhenAll(first, second)).ShouldAllBe(code => code == 0);

        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ShouldBe([SkillPath]);
        (await File.ReadAllTextAsync(SkillPath)).ShouldBe(GateSkillText.ClaudeCodeSkill(GateSkillText.MapRevision));
    }

    [Fact]
    public async Task The_written_file_stays_under_the_byte_budget()
    {
        (await CliRunner.RunAsync("agent-skill", "--output", _root)).ExitCode.ShouldBe(0);

        new FileInfo(SkillPath).Length.ShouldBeLessThanOrEqualTo(
            GateSkillText.MaximumBytes,
            "every byte is spent out of the context budget of the agent that loads this on every Tracon task");
    }

    /// <summary>
    /// The shell carries the shared procedure verbatim. With one shell today
    /// this pins the split itself: a second harness has to wrap this same text
    /// rather than fork a copy of it that drifts.
    /// </summary>
    [Fact]
    public void The_shell_carries_the_canonical_text_verbatim()
    {
        GateSkillText.ClaudeCodeSkill("deadbeef").ShouldContain(GateSkillText.Body, Case.Sensitive);
        GateSkillText.Body.ShouldNotContain("---", Case.Sensitive, "front matter belongs to the shell, not the procedure");
    }

    /// <summary>
    /// This file ships to a consumer, and no other gate covers it: the shipped
    /// documentation gate reads XML doc comments, and this text is a string
    /// literal. It may only point at things the consumer holds.
    /// </summary>
    [Fact]
    public void The_written_text_points_at_nothing_from_the_development_record()
    {
        var pattern = new Regex(
            File.ReadAllText(Path.Combine(RepositoryRoot(), "docs-site", "scripts", "internal-history.pattern")).Trim(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        var offending = pattern.Matches(GateSkillText.ClaudeCodeSkill(GateSkillText.MapRevision))
            .Select(match => match.Value)
            .ToList();

        offending.ShouldBeEmpty(
            $"the gate skill names something the consumer never receives: {string.Join(", ", offending)}");
    }

    [Fact]
    public async Task Json_reports_what_the_command_did()
    {
        var written = await CliRunner.RunAsync("agent-skill", "--output", _root, "--json");

        using (var document = JsonDocument.Parse(written.StandardOutput))
        {
            document.RootElement.GetProperty("action").GetString().ShouldBe("written");
            document.RootElement.GetProperty("format").GetString().ShouldBe("claude");
            document.RootElement.GetProperty("path").GetString().ShouldBe(SkillPath);
            document.RootElement.GetProperty("revision").GetString().ShouldBe(GateSkillText.MapRevision);
        }

        var kept = await CliRunner.RunAsync("agent-skill", "--output", _root, "--json");

        using var second = JsonDocument.Parse(kept.StandardOutput);
        second.RootElement.GetProperty("action").GetString().ShouldBe("kept");
    }

    /// <summary>
    /// The stamp the command writes is the revision of the map this build of
    /// the tool carries. A tool built against another map would stamp a value
    /// the consumer's build immediately reports as stale.
    /// </summary>
    [Fact]
    public async Task The_stamped_revision_is_the_one_the_repository_ships()
    {
        var map = await File.ReadAllLinesAsync(
            Path.Combine(RepositoryRoot(), "src", "Tracon.Core", "buildTransitive", "Tracon.AgentMap.md"));

        map[0].ShouldContain($"revision: {GateSkillText.MapRevision} ", Case.Sensitive);
    }

    /// <summary>
    /// The stamp the command writes and the stamp the analyzer reads are two
    /// literals in two assemblies, and nothing else compares them.
    /// </summary>
    /// <remarks>
    /// The analyzer targets netstandard2.0 and ships as an analyzer reference,
    /// so its constant cannot be referenced from here; the source is read
    /// instead, which is what makes the comparison real. Without this, changing
    /// either literal leaves every test in both suites green and TRC0403 silent
    /// forever - each side would still agree with itself.
    /// </remarks>
    [Fact]
    public void The_marker_the_command_writes_is_the_one_the_analyzer_looks_for()
    {
        var analyzer = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Tracon.Generators", "TraconUsageAnalyzer.cs"));

        var declaration = Regex.Match(
            analyzer,
            @"GateSkillMarkerOpening\s*=\s*""(?<marker>[^""]*)""",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(5));

        declaration.Success.ShouldBeTrue("TraconUsageAnalyzer no longer declares GateSkillMarkerOpening.");

        // The source carries the separator as an escape; compare the decoded value.
        var expected = Regex.Unescape(declaration.Groups["marker"].Value);

        GateSkillText.MarkerOpening.ShouldBe(expected);
    }

    /// <summary>
    /// With no <c>--output</c>, the file lands at the repository root rather
    /// than wherever the command happened to be run from.
    /// </summary>
    /// <remarks>
    /// The build reads the file from the repository root, so a default of the
    /// working directory let a consumer "fix" a stale-skill warning by writing
    /// a second copy into a subdirectory: the warning went quiet because the
    /// file it named had been deleted, and the new one reached nothing.
    /// </remarks>
    [Fact]
    public async Task Without_an_output_the_file_lands_at_the_repository_root()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".git"));
        var nested = Directory.CreateDirectory(Path.Combine(_root, "src", "Consumer")).FullName;

        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(nested);

        try
        {
            var result = await CliRunner.RunAsync("agent-skill");

            result.ExitCode.ShouldBe(0, result.Combined);
            File.Exists(SkillPath).ShouldBeTrue(result.Combined);
            File.Exists(Path.Combine(nested, RelativeSkillPath))
                .ShouldBeFalse("the working directory is not where the build looks");
            result.StandardOutput.ShouldContain(SkillPath);
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    /// <summary>
    /// Without a repository above it, the working directory is the answer -
    /// there is nowhere better, and refusing would be worse than writing.
    /// </summary>
    [Fact]
    public async Task Without_a_repository_the_working_directory_is_used()
    {
        var previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_root);

        try
        {
            (await CliRunner.RunAsync("agent-skill")).ExitCode.ShouldBe(0);

            File.Exists(SkillPath).ShouldBeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }

    [Fact]
    public async Task A_format_flag_with_no_value_is_an_argument_error()
    {
        var result = await CliRunner.RunAsync("agent-skill", "--output", _root, "--format");

        result.ExitCode.ShouldBe(1);
        result.StandardError.ShouldContain("needs a value");
        File.Exists(SkillPath).ShouldBeFalse("a rejected argument writes nothing");
    }

    private static async Task<string> HashAsync(string path) =>
        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("No directory above the test binary carries Tracon.slnx.");
    }
}
