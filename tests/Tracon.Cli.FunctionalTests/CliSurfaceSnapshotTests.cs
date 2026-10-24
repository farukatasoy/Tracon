using System.Text.RegularExpressions;
using Tracon.Cli.FunctionalTests.Infrastructure;

namespace Tracon.Cli.FunctionalTests;

/// <summary>
/// Pins the commands and options of the <c>tracon</c> CLI to <c>docs/sozlesme/cli.txt</c> (Phase 238, section 238.2).
/// </summary>
/// <remarks>
/// <para>
/// A build script calls <c>tracon eval --url … --min-pass-rate …</c> in CI. Dropping or renaming a command or an
/// option fails that script, so in 1.x it is a breaking change; a new command or option is not. The words the
/// human output uses are not a contract; the names in the usage block are.
/// </para>
/// <para>
/// The names are read from <c>tracon --help</c>, the one place a consumer reads them. Two checks keep that text
/// true to the code: every <c>--option</c> literal in the CLI sources must appear in it, and every command the
/// entry point routes must appear in it. Without them a command could work and be missing from the snapshot, and
/// the snapshot would pin the help text rather than the CLI.
/// </para>
/// </remarks>
[Collection(nameof(CliTestGroup))]
public sealed class CliSurfaceSnapshotTests
{
    // The forms Program.Main answers before it routes a command. They are not in the usage block, so a test runs each.
    private static readonly string[] HelpForms = ["--help", "-h", "help"];

    private static readonly Regex OptionName = new(
        @"--[a-z][a-z-]*",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(5));

    private static readonly Regex RoutedCommand = new(
        """^\s*"(?<name>[a-z][a-z-]*)"(?:\s+when\s+[^=]+)?\s*=>""",
        RegexOptions.CultureInvariant | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    [Fact]
    public async Task The_snapshot_matches_the_commands_and_options_in_the_help_text()
    {
        ContractSnapshot.Verify(
            "docs/sozlesme/cli.txt",
            "tests/Tracon.Cli.FunctionalTests/CliSurfaceSnapshotTests.cs",
            "The commands and options of the tracon CLI, read from 'tracon --help' (an option line names its command)",
            await SurfaceAsync());
    }

    [Fact]
    public async Task Every_help_form_prints_the_usage_and_exits_zero()
    {
        foreach (var form in HelpForms)
        {
            var result = await CliRunner.RunAsync(form);

            result.ExitCode.ShouldBe(0, $"'tracon {form}' must print the usage and succeed");
            result.StandardOutput.ShouldContain("Usage:", Case.Sensitive);
        }

        // No argument at all prints the same text and fails: a script that forgot its arguments must not pass.
        (await CliRunner.RunAsync()).ExitCode.ShouldBe(1);

        // The version forms. `--version` is in the usage block; `-v` is its short form.
        foreach (var form in new[] { "--version", "-v" })
        {
            (await CliRunner.RunAsync(form)).ExitCode.ShouldBe(0, $"'tracon {form}' must succeed");
        }
    }

    [Fact]
    public async Task Every_option_the_cli_sources_read_is_listed_in_the_help_text()
    {
        var listed = (await SurfaceAsync())
            .Where(static line => line.StartsWith("option ", StringComparison.Ordinal))
            .SelectMany(static line => OptionName.Matches(line).Select(static match => match.Value))
            .ToHashSet(StringComparer.Ordinal);

        var read = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src", "Tracon.Cli"), "*.cs", SearchOption.AllDirectories))
        {
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in Regex.Matches(File.ReadAllText(path), "\"(?<option>--[a-z][a-z-]*)\"", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5)))
            {
                read.Add(match.Groups["option"].Value);
            }
        }

        // 2026-10-24: 19 literals, 18 of them in the usage block; `--help` is answered before routing.
        read.Count.ShouldBeGreaterThan(15);
        read.Where(option => !listed.Contains(option) && !string.Equals(option, "--help", StringComparison.Ordinal))
            .ShouldBeEmpty("The CLI reads an option that 'tracon --help' does not list. Add it to the usage block first.");
    }

    [Fact]
    public async Task Every_command_the_entry_point_routes_is_listed_in_the_help_text()
    {
        var listed = (await SurfaceAsync())
            .Where(static line => line.StartsWith("command ", StringComparison.Ordinal))
            .Select(static line => line["command ".Length..].Split(' ')[0])
            .ToHashSet(StringComparer.Ordinal);

        var source = File.ReadAllText(Path.Combine(RepoRoot.Path, "src", "Tracon.Cli", "Program.cs"));
        var routed = RoutedCommand.Matches(source).Select(static match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);

        routed.Count.ShouldBeGreaterThan(4);
        routed.Where(command => !listed.Contains(command)).ShouldBeEmpty("A routed command is missing from 'tracon --help'.");

        // `help` is answered before routing, like the options in HelpForms.
        listed.Where(command => !routed.Contains(command) && !string.Equals(command, "help", StringComparison.Ordinal))
            .ShouldBeEmpty("'tracon --help' lists a command the entry point does not route.");
    }

    [Theory]
    [InlineData(
        "  tracon migrate status --provider <p>\n  tracon eval --url <u>\n                  [--json]\n",
        new[] { "command migrate status", "command eval", "option migrate status --provider", "option eval --url", "option eval --json" })]
    [InlineData(
        "  tracon --version\n  tracon health --url <u> [--token <t>]\n",
        new[] { "command health", "option --version", "option health --url", "option health --token" })]
    public void The_usage_parser_attaches_each_option_to_its_command(string usage, string[] expected)
    {
        Parse($"Usage:\n{usage}\nNext paragraph.").ShouldBe(expected, ignoreOrder: true);
    }

    private static async Task<List<string>> SurfaceAsync()
    {
        var result = await CliRunner.RunAsync("--help");

        result.ExitCode.ShouldBe(0);

        return [.. Parse(result.StandardOutput).Concat(
            [
                .. HelpForms.Where(static form => form.StartsWith('-')).Select(static form => $"option {form}"),
                "option -v",
                "command help",
            ])];
    }

    /// <summary>Reads the <c>Usage:</c> block: a line starting with <c>tracon</c> opens a command, deeper lines continue it.</summary>
    private static List<string> Parse(string help)
    {
        var lines = new List<string>();
        var inUsage = false;
        var command = string.Empty;

        foreach (var raw in help.ReplaceLineEndings("\n").Split('\n'))
        {
            if (!inUsage)
            {
                inUsage = raw.StartsWith("Usage:", StringComparison.Ordinal);
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                break;
            }

            var line = raw.Trim();

            if (line.StartsWith("tracon", StringComparison.Ordinal))
            {
                var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                    .TakeWhile(static word => word[0] is not ('-' or '<' or '['));

                command = string.Join(' ', words);

                if (command.Length > 0)
                {
                    lines.Add($"command {command}");
                }
            }

            foreach (Match option in OptionName.Matches(line))
            {
                lines.Add(command.Length > 0 ? $"option {command} {option.Value}" : $"option {option.Value}");
            }
        }

        return lines;
    }
}
