using Tracon.Core.UnitTests.Configuration;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// The history of a session is read and appended in one place, <c>ChatHistoryReader</c>.
/// </summary>
/// <remarks>
/// The framework's way into a chat history provider needs a constructor that is marked for evaluation only, and
/// the warning is suppressed where it is called. A second copy of that call is a second place to find when the
/// framework changes the type, and it had already been written twice before this gate. A new read or write goes
/// through <c>ChatHistoryReader</c>.
/// </remarks>
public sealed class ChatHistoryAccessSiteTests
{
    private const string AllowedFile = "src/Tracon.Core/Sessions/ChatHistoryReader.cs";

    private static readonly string[] Constructions =
    [
        "ChatHistoryProvider.InvokingContext(",
        "ChatHistoryProvider.InvokedContext(",
    ];

    [Fact]
    public void Only_the_chat_history_reader_builds_the_contexts_of_a_chat_history_provider()
    {
        var root = OptionsSectionCoverage.FindRepositoryRoot();

        var offenders = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(relative => !string.Equals(relative, AllowedFile, StringComparison.Ordinal))
            .Where(relative =>
            {
                var text = File.ReadAllText(Path.Combine(root, relative));

                return Constructions.Any(construction => text.Contains(construction, StringComparison.Ordinal));
            })
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "a chat history is read and appended through ChatHistoryReader, so the evaluation-only constructor is called in one file");
    }

    [Fact]
    public void The_chat_history_reader_still_holds_both_calls()
    {
        var root = OptionsSectionCoverage.FindRepositoryRoot();
        var text = File.ReadAllText(Path.Combine(root, AllowedFile));

        Constructions.ShouldAllBe(construction => text.Contains(construction, StringComparison.Ordinal));
    }
}
