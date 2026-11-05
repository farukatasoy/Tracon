using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// The secrets a model provider reports it was set up with are read in one place: the registry that builds the mask of
/// a client.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IModelProvider.SetupApiKeys"/> returns real keys. A second reader (a diagnostics page, a health check, a
/// serializer, a log line) is a second way for those values to leave the process, and nothing about a key tells a
/// compiler that it must not be written. This gate makes a NEW reader fail until someone either removes it or adds it
/// to this list with the reason, where a reviewer sees it.
/// </para>
/// <para>
/// The text of comments is not read: documentation names the member and does not read it.
/// </para>
/// </remarks>
public sealed partial class SetupApiKeysReaderTests
{
    /// <summary>The one place that reads the values.</summary>
    private const string Reader = "src/Tracon.Core/Models/ModelProviderRegistry.cs";

    /// <summary>The files that declare or implement the member. They return the values and read none.</summary>
    private static readonly string[] Declarations =
    [
        "src/Tracon.Abstractions/Models/IModelProvider.cs",
        "src/Tracon.Anthropic/AnthropicModelProvider.cs",
        "src/Tracon.Azure/AzureOpenAIModelProvider.cs",
        "src/Tracon.Google/GoogleModelProvider.cs",
        "src/Tracon.OpenAI/OpenAIModelProvider.cs",
    ];

    [Fact]
    public void Only_the_registry_reads_the_keys_a_provider_was_set_up_with()
    {
        var root = FindRepositoryRoot();

        var files = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(path => (Relative: Path.GetRelativePath(root, path).Replace('\\', '/'), Code: WithoutComments(File.ReadAllText(path))))
            .ToList();

        var readers = files
            .Where(file => MemberRead().IsMatch(file.Code))
            .Select(static file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        var mentions = files
            .Where(file => file.Code.Contains("SetupApiKeys", StringComparison.Ordinal))
            .Select(static file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToList();

        readers.ShouldBe(
            [Reader],
            "A place other than the registry reads IModelProvider.SetupApiKeys. The values are real keys: do not read them there.");
        mentions.ShouldBe(
            [.. Declarations.Append(Reader).Order(StringComparer.Ordinal)],
            "A file mentions SetupApiKeys that does not declare it and is not the reader. Add it to Declarations only if it implements the member.");
    }

    // `.SetupApiKeys` as a member access; a declaration is `SetupApiKeys =>` or `SetupApiKeys {`, with no dot before it.
    [GeneratedRegex(@"\.\s*SetupApiKeys\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MemberRead();

    private static string WithoutComments(string source)
        => string.Join('\n', source.Split('\n').Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Repository root not found. Searched upwards from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
