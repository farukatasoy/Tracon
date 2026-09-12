using System.Text;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Keeps <see cref="ProtectedColumn"/> in step with the code: every column
/// that at-rest content protection can be applied to must be referenced by
/// an actual <c>ProtectedValue.Write</c>/<c>WriteBytes</c> call in the shared
/// store layer.
/// </summary>
/// <remarks>
/// <para>
/// The scope list (<see cref="ProtectedColumn"/>) and the code that applies
/// it (<c>src/Tracon.Sql.Shared/Stores/</c>) live in two different
/// places on purpose — the enum is public API in <c>Tracon.Abstractions</c>,
/// the wiring is internal to the persistence layer. Nothing stops them from
/// drifting apart except this test: a column added to the enum without a
/// matching write call would silently promise protection the code never
/// delivers.
/// </para>
/// <para>
/// Read coverage is not checked separately: <c>ProtectedValue.Read</c>/
/// <c>ReadBytes</c> take no <see cref="ProtectedColumn"/> parameter — they are
/// unconditional and self-describing (the <c>$apEnc</c> tag decides, not
/// configuration) — so the only place a column name is ever written in code
/// is the write call site this test looks for.
/// </para>
/// </remarks>
public sealed class ProtectedColumnCoverageTests
{
    /// <summary>
    /// Columns that are deliberately not written by any store today, with the
    /// measured reason. Every entry needs one; see
    /// <see cref="Every_exemption_carries_a_non_empty_reason"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<ProtectedColumn, string> ExemptFromWriteCoverage =
        new Dictionary<ProtectedColumn, string>
        {
            [ProtectedColumn.ResponsePayload] =
                "No store writes to the responses table (grep for INSERT INTO ... responses finds nothing); " +
                "the column stays in scope so it starts protected the day a store fills it in.",
        };

    [Fact]
    public void Every_protected_column_is_referenced_by_a_write_call_in_the_shared_store_layer()
    {
        var storesText = ReadStoresSourceText();
        var missing = new List<string>();

        foreach (var column in Enum.GetValues<ProtectedColumn>())
        {
            if (ExemptFromWriteCoverage.ContainsKey(column))
            {
                continue;
            }

            if (!storesText.Contains($"ProtectedColumn.{column}", StringComparison.Ordinal))
            {
                missing.Add(column.ToString());
            }
        }

        missing.ShouldBeEmpty(
            "The following ProtectedColumn values are never referenced from " +
            $"src/Tracon.Sql.Shared/Stores/: {string.Join(", ", missing)}. Either wire a " +
            $"ProtectedValue.Write/WriteBytes call for the column, or add it to " +
            $"{nameof(ExemptFromWriteCoverage)} with a measured reason.");
    }

    [Fact]
    public void Every_exemption_carries_a_non_empty_reason()
    {
        foreach (var (column, reason) in ExemptFromWriteCoverage)
        {
            reason.ShouldNotBeNullOrWhiteSpace($"{column} is exempt from write coverage but carries no reason.");
        }
    }

    [Fact]
    public void Every_exempted_column_still_has_no_write_call_today()
    {
        // The mirror check: an exemption that quietly went stale (a write call
        // was added later, but the entry was never removed) is just as wrong
        // as a missing one - it hides that the column IS now covered.
        var storesText = ReadStoresSourceText();
        var staleExemptions = ExemptFromWriteCoverage.Keys
            .Where(column => storesText.Contains($"ProtectedColumn.{column}", StringComparison.Ordinal))
            .Select(static column => column.ToString())
            .ToList();

        staleExemptions.ShouldBeEmpty(
            $"The following columns are exempted from write coverage but ARE now referenced: " +
            $"{string.Join(", ", staleExemptions)}. Remove the stale entry from {nameof(ExemptFromWriteCoverage)}.");
    }

    private static string ReadStoresSourceText()
    {
        var storesDirectory = Path.Combine(RepositoryRoot, "src", "Tracon.Sql.Shared", "Stores");

        Directory.Exists(storesDirectory).ShouldBeTrue($"'{storesDirectory}' was not found.");

        var builder = new StringBuilder();

        foreach (var file in Directory.EnumerateFiles(storesDirectory, "*.cs", SearchOption.AllDirectories))
        {
            builder.Append(File.ReadAllText(file));
        }

        return builder.ToString();
    }

    /// <remarks>
    /// The test build output lives under artifacts/, so a fixed relative
    /// path cannot be used; the tree is walked upward searching for
    /// Tracon.slnx instead.
    /// </remarks>
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
