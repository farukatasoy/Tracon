using System.Text.RegularExpressions;

namespace Tracon.Package.Tests.Infrastructure;

/// <summary>What one step of a guide asks the reader to do.</summary>
internal enum GuideStepKind
{
    /// <summary>A command line in a <c>bash</c> block.</summary>
    Command,

    /// <summary>A file the reader edits: a fenced block that is not <c>bash</c> and carries a <c>title=</c>.</summary>
    FileEdit,
}

/// <summary>One step of a guide.</summary>
/// <param name="Kind">What the reader does.</param>
/// <param name="Text">The command line, or the title of the file the reader edits.</param>
internal sealed record GuideStep(GuideStepKind Kind, string Text);

/// <summary>Reads the steps a guide section asks of the reader, in order.</summary>
/// <remarks>
/// <para>
/// A step is a command line in a <c>bash</c> block, or a file the reader is shown
/// WITH A TITLE, which is how the site marks a listing to edit
/// (<c>```csharp title="Program.cs"</c>). A fenced block without a title is an
/// illustration and not a step. The second kind is counted because the count is
/// the measure: a guide that asks for a code edit is longer than the commands show.
/// </para>
/// <para>
/// The limit is stated, not hidden: an edit asked for in prose only, with no
/// listing, is invisible here. That one is a review finding, not a gate.
/// </para>
/// </remarks>
internal static class GuideSteps
{
    private static readonly Regex Fence = new(
        @"^```(?<language>\w*)(?<info>[^\n]*)\n(?<body>.*?)^```",
        RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex Title = new(
        @"\btitle=""(?<title>[^""]*)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    /// <summary>Reads the steps between two <c>##</c> headings.</summary>
    /// <param name="markdown">The guide's Markdown.</param>
    /// <param name="fromHeading">The heading text (without <c>##</c>) the steps start after.</param>
    /// <param name="toHeading">The heading text the steps end before.</param>
    /// <returns>The steps, in the order the guide gives them.</returns>
    public static IReadOnlyList<GuideStep> Read(string markdown, string fromHeading, string toHeading)
    {
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = IndexOfHeading(text, fromHeading, 0);
        var end = IndexOfHeading(text, toHeading, start + 1);
        var steps = new List<GuideStep>();

        foreach (Match fence in Fence.Matches(text[start..end]))
        {
            if (string.Equals(fence.Groups["language"].Value, "bash", StringComparison.Ordinal))
            {
                steps.AddRange(Commands(fence.Groups["body"].Value).Select(line => new GuideStep(GuideStepKind.Command, line)));
            }
            else if (Title.Match(fence.Groups["info"].Value) is { Success: true } title)
            {
                steps.Add(new GuideStep(GuideStepKind.FileEdit, title.Groups["title"].Value));
            }
        }

        return steps;
    }

    /// <summary>Splits a command line into words. Single or double quotes group a word and are dropped.</summary>
    /// <param name="commandLine">The command line.</param>
    /// <returns>The words.</returns>
    public static string[] Words(string commandLine)
    {
        var words = new List<string>();
        var current = new System.Text.StringBuilder();
        var quote = '\0';
        var started = false;

        foreach (var character in commandLine)
        {
            if (quote == '\0' && character is '"' or '\'')
            {
                quote = character;
                started = true;
            }
            else if (character == quote)
            {
                quote = '\0';
            }
            else if (char.IsWhiteSpace(character) && quote == '\0')
            {
                if (started)
                {
                    words.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
            }
            else
            {
                current.Append(character);
                started = true;
            }
        }

        if (started)
        {
            words.Add(current.ToString());
        }

        return [.. words];
    }

    private static int IndexOfHeading(string text, string heading, int from)
    {
        var marker = $"\n## {heading}\n";
        var index = text.IndexOf(marker, from, StringComparison.Ordinal);

        return index >= 0
            ? index
            : throw new InvalidOperationException(
                $"The guide has no '## {heading}' heading after offset {from}. The heading was renamed: update the section the path test reads.");
    }

    /// <summary>The command lines of one <c>bash</c> block: a trailing <c>\</c> joins the next line, a <c>#</c> line is a comment.</summary>
    private static IEnumerable<string> Commands(string body)
    {
        var pending = string.Empty;

        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();

            if (pending.Length == 0 && (line.Length == 0 || line[0] == '#'))
            {
                continue;
            }

            if (line.EndsWith('\\'))
            {
                pending += line[..^1].TrimEnd() + " ";
                continue;
            }

            yield return (pending + line).Trim();
            pending = string.Empty;
        }
    }
}
