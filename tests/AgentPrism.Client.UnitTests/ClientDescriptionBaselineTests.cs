namespace AgentPrism.Client.UnitTests;

/// <summary>
/// Guards the XML doc coverage of the generated client against regressing
/// (section 83.4).
/// </summary>
/// <remarks>
/// <para>
/// <c>Generated/AgentPrismApiClient.g.cs</c> carries a file-level
/// <c>#pragma warning disable 1591</c>: NSwag writes it itself, and without
/// it CS1591 would fail the build the moment a schema lacks a
/// <c>description</c>. The pragma keeps the build green but silently ships a
/// client with empty IntelliSense for whatever it covers - the same trap
/// <c>source-language-baseline.txt</c> and <c>capability-example-baseline.txt</c>
/// close elsewhere in this repo: a taken-for-granted baseline this test only
/// lets shrink, never grow.
/// </para>
/// <para>
/// A public member counts as documented when the line immediately above it
/// (skipping attribute lines) starts with <c>///</c>. This is intentionally
/// a text heuristic, not a Roslyn symbol walk: the point is catching an
/// INCREASE after a regeneration, not distinguishing every doc-comment
/// shape perfectly.
/// </para>
/// </remarks>
public sealed class ClientDescriptionBaselineTests
{
    [Fact]
    public void Undocumented_public_member_count_does_not_grow()
    {
        var current = CountUndocumentedPublicMembers(File.ReadAllLines(GeneratedClientPath));
        var baseline = int.Parse(File.ReadAllText(BaselinePath).Trim(), System.Globalization.CultureInfo.InvariantCulture);

        current.ShouldBeLessThanOrEqualTo(
            baseline,
            customMessage: $"The generated client now has {current} undocumented public members, up from a " +
                            $"baseline of {baseline}. Either the OpenAPI document lost a description that used to " +
                            $"exist (regression - fix the source), or genuinely new undocumented surface was added " +
                            $"(update {Path.GetFileName(BaselinePath)} to {current} only after confirming that).");
    }

    /// <summary>
    /// A public member is "documented" when the nearest non-attribute line
    /// above it starts with <c>///</c>.
    /// </summary>
    private static int CountUndocumentedPublicMembers(string[] lines)
    {
        var count = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("public ", StringComparison.Ordinal))
            {
                continue;
            }

            // NSwag separates a member's XML doc block from its
            // [JsonPropertyName(...)] attribute with a blank line; skip both
            // attribute lines and blank lines walking back to the doc block.
            var j = i - 1;
            while (j >= 0 && (lines[j].TrimStart().StartsWith('[') || lines[j].Trim().Length == 0))
            {
                j--;
            }

            var documented = j >= 0 && lines[j].TrimStart().StartsWith("///", StringComparison.Ordinal);
            if (!documented)
            {
                count++;
            }
        }

        return count;
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string GeneratedClientPath { get; } =
        Path.Combine(RepositoryRoot, "src", "AgentPrism.Client", "Generated", "AgentPrismApiClient.g.cs");

    private static string BaselinePath { get; } =
        Path.Combine(RepositoryRoot, "tests", "AgentPrism.Client.UnitTests", "client-description-baseline.txt");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AgentPrism.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("AgentPrism.slnx not found.");
    }
}
