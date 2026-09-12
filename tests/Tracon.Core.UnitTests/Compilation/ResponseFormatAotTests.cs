namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The <c>ChatResponseFormat.ForJsonSchema(Type, ...)</c> and
/// <c>ForJsonSchema(JsonSerializerOptions, ...)</c> overloads rely on reflection
/// (<c>AIJsonUtilities.CreateJsonSchema</c>) and break the AOT stance of
/// <c>Tracon.Abstractions</c> and <c>.Core</c>. This test scans the source
/// tree to verify these overloads are never called. See docs/arsiv/fazlar/38-YAPILANDIRILMIS-CIKTI.md, 38.4.
/// </summary>
public sealed class ResponseFormatAotTests
{
    [Fact]
    public void ForJsonSchema_Type_overload_is_absent_from_source_tree()
        => AssertNoMatch("ForJsonSchema(typeof(", "ForJsonSchema<");

    [Fact]
    public void ForJsonSchema_JsonSerializerOptions_overload_is_absent()
        => AssertNoMatch("ForJsonSchema(serializerOptions", "ForJsonSchema(options", "ForJsonSchema(jsonOptions");

    private static void AssertNoMatch(params string[] forbiddenSnippets)
    {
        var srcRoot = Path.Combine(RepositoryRoot, "src");
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var content = File.ReadAllText(file);

            foreach (var snippet in forbiddenSnippets)
            {
                if (content.Contains(snippet, StringComparison.Ordinal))
                {
                    offenders.Add($"{file}: '{snippet}'");
                }
            }
        }

        offenders.ShouldBeEmpty(customMessage: string.Join(Environment.NewLine, offenders));
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Tracon.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found. Searched upward from '{AppContext.BaseDirectory}' for Tracon.slnx.");
    }
}
