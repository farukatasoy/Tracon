using System.Globalization;
using System.Text.Json;

namespace Tracon.Cli.Commands;

/// <summary>
/// Writes the Tracon gate skill into a consumer's repository, so the coding
/// agent working there reads the capability map before it writes code Tracon
/// already ships.
/// </summary>
/// <remarks>
/// <para>
/// This is the one command that changes the caller's working tree, and it does
/// so under the same contract the build already uses for <c>AGENTS.md</c>: an
/// existing file is never touched without <c>--force</c>, because the consumer
/// may have edited it and losing those edits would be worse than a stale file.
/// Staleness is reported by the build instead, as a warning that names the two
/// steps which refresh the file.
/// </para>
/// <para>
/// The tool writes it rather than the build, for two reasons. The content is
/// the consumer's to keep and to edit, and a build target that writes a
/// directory tree cannot honour "never overwrite" the way one writing a single
/// file can.
/// </para>
/// </remarks>
internal static class AgentSkillCommand
{
    /// <summary>
    /// The formats this build actually writes. One entry, and the reason is
    /// measured rather than chosen: a skill file is loaded by the harness that
    /// defines the layout, and a file written to a layout no harness reads is
    /// dead weight that still costs the consumer their agent's context budget.
    /// Only this layout was measured to load.
    /// </summary>
    private static readonly string[] SupportedFormats = ["claude"];

    // camelCase, like `state-check --json` and the HTTP API: one PascalCase
    // document would make this command the odd one out in a `jq` pipeline.
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var requestedFormat = CliArgs.GetOption(args, "--format");
        var force = CliArgs.HasFlag(args, "--force");
        var asJson = CliArgs.HasFlag(args, "--json");

        // '--format' with nothing after it reads as absent, and defaulting there
        // would write the Claude file for someone who asked for something else.
        // This command makes the narrowing explicit, so it may not narrow quietly.
        if (requestedFormat is null && CliArgs.HasFlag(args, "--format"))
        {
            throw new CliArgumentException(
                $"'--format' needs a value. Supported: {string.Join(", ", SupportedFormats)}.");
        }

        var format = requestedFormat ?? SupportedFormats[0];

        if (!SupportedFormats.Contains(format, StringComparer.Ordinal))
        {
            throw new CliArgumentException(
                $"'--format {format}' is not a format this version writes. Supported: {string.Join(", ", SupportedFormats)}.");
        }

        var output = ResolveOutputDirectory(CliArgs.GetOption(args, "--output"));
        var target = ResolveTarget(output);

        // Read once, BEFORE the write: asking again afterwards always answers
        // "yes" and would report every write as an overwrite.
        var existed = File.Exists(target);

        if (existed && !force)
        {
            Report(asJson, format, target, "kept", revision: null);
            return 0;
        }

        try
        {
            var revision = GateSkillText.MapRevision;
            await WriteAtomicallyAsync(target, GateSkillText.ClaudeCodeSkill(revision), cancellationToken).ConfigureAwait(false);
            Report(asJson, format, target, existed ? "overwritten" : "written", revision);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled. Nothing was written.");
            return 2;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Could not write '{target}': {ex.Message}");
            return 2;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Could not write '{target}': {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// The directory the skill is written under: the repository root above the
    /// working directory, or the working directory when there is no repository.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The repository root rather than the working directory, because that is
    /// where the build looks for the file and where a coding agent's harness
    /// loads it from. Measured as a defect first: with the working directory as
    /// the default, running the command from a subdirectory after the build
    /// reported the file as stale wrote a second copy nobody reads, and the
    /// warning went quiet because the file it named was gone. Two writers
    /// looking at one location cannot drift apart.
    /// </para>
    /// <para>
    /// The probe matches the build target's: a normal clone carries a
    /// <c>.git</c> directory, while a worktree or a submodule carries a
    /// <c>.git</c> FILE.
    /// </para>
    /// <para>
    /// A directory that does not exist is an argument error rather than a
    /// created tree: the option names a repository root, and creating one from
    /// a typo would scatter a skill somewhere nobody looks.
    /// </para>
    /// </remarks>
    private static string ResolveOutputDirectory(string? requested)
    {
        if (string.IsNullOrEmpty(requested))
        {
            var working = Directory.GetCurrentDirectory();

            return RepositoryRootAbove(working) ?? working;
        }

        var output = Path.GetFullPath(requested);

        if (!Directory.Exists(output))
        {
            throw new CliArgumentException(
                $"'--output {output}' is not an existing directory. Give the root of the repository the skill belongs to.");
        }

        return output;
    }

    /// <summary>The nearest directory at or above <paramref name="start"/> that holds a <c>.git</c> entry.</summary>
    private static string? RepositoryRootAbove(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var git = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(git) || File.Exists(git))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// The file this command writes, proven to sit under the directory it was
    /// given.
    /// </summary>
    /// <remarks>
    /// The relative path is a constant today, so the check can only fire once a
    /// second format arrives or the option is fed a path that resolves
    /// elsewhere. It is written now because the failure it guards against - a
    /// file-writing command reaching outside the directory the caller named -
    /// is silent, and a silent clamp to the target would surprise the caller
    /// just as much as the escape.
    /// </remarks>
    private static string ResolveTarget(string output)
    {
        var target = Path.GetFullPath(Path.Combine(output, ".claude", "skills", GateSkillText.SkillName, "SKILL.md"));
        var root = output.EndsWith(Path.DirectorySeparatorChar) ? output : output + Path.DirectorySeparatorChar;

        if (!target.StartsWith(root, StringComparison.Ordinal))
        {
            throw new CliArgumentException(
                $"'{target}' is outside '{output}'. This command never writes outside the directory it is given.");
        }

        return target;
    }

    /// <summary>
    /// Writes through a temporary file in the same directory, then moves it
    /// into place.
    /// </summary>
    /// <remarks>
    /// A direct write leaves a half-written file behind when the command is
    /// cancelled, and a half-written skill is worse than none: the harness
    /// still loads it, and the procedure it carries stops mid-sentence. The
    /// move is the only step that makes the file visible, and it happens after
    /// the content is complete. Two runs at once are safe for the same reason -
    /// whichever moves last wins, and neither can be seen half-done.
    /// </remarks>
    private static async Task WriteAtomicallyAsync(string target, string content, CancellationToken cancellationToken)
    {
        // Before the directory, not after: a cancelled run that had already
        // created the tree would leave an empty .claude/skills/tracon/ in the
        // consumer's repository while reporting that nothing was written.
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);

        var temporary = Path.Combine(
            directory,
            string.Create(CultureInfo.InvariantCulture, $"SKILL.md.{Guid.NewGuid():n}.tmp"));

        try
        {
            await File.WriteAllTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void Report(bool asJson, string format, string path, string action, string? revision)
    {
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                new SkillWriteReport(format, path, action, revision),
                PrettyJson));
            return;
        }

        Console.WriteLine(action switch
        {
            "kept" => $"{path} already exists and was left untouched. Pass --force to overwrite it.",
            "overwritten" => $"{path} was overwritten from capability map revision {revision}.",
            _ => $"{path} was written from capability map revision {revision}.",
        });
    }

    /// <summary>What the command did, for a caller reading <c>--json</c>.</summary>
    private sealed record SkillWriteReport(string Format, string Path, string Action, string? Revision);
}
