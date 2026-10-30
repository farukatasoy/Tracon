using System.Reflection;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Reads the embedded <c>runs_v1</c> migration SQL text of the "views"
/// optional set and extracts a marker-delimited block, without
/// opening a database connection.
/// </summary>
/// <remarks>
/// Reads the manifest resource directly instead of going through
/// <c>MigrationDescriptor</c>/<c>SqlDialect</c>: those are shared linked
/// source compiled separately into all three provider assemblies, so
/// naming either type here would hit the same cross-assembly ambiguity
/// (CS0433) the repository already warns about for this test project.
/// </remarks>
internal static class ReadViewSqlText
{
    /// <summary>
    /// Reads the LATEST <c>.sql</c> resource under <paramref name="resourcePrefix"/> (the highest
    /// number) and returns the text between a <c>-- {beginMarker}</c> comment and a
    /// <c>-- {endMarker}</c> comment.
    /// </summary>
    /// <remarks>
    /// The view is recreated in full by each of its migrations, so the last one is its current
    /// definition: the one a database that applied them all holds. Reading the first would test
    /// a definition no database has any more.
    /// </remarks>
    /// <param name="assembly">The provider assembly the migration is embedded in.</param>
    /// <param name="resourcePrefix">
    /// The embedded resource name prefix, matching the dialect's
    /// <c>OptionalMigrationResourcePrefixes["views"]</c> entry.
    /// </param>
    /// <param name="beginMarker">The text following <c>-- </c> that opens the block.</param>
    /// <param name="endMarker">The text following <c>-- </c> that closes the block.</param>
    /// <returns>The raw SQL substring between the two markers, exclusive.</returns>
    public static string ExtractMarkedBlock(Assembly assembly, string resourcePrefix, string beginMarker, string endMarker)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var resourceName = assembly.GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(resourcePrefix, StringComparison.Ordinal) &&
                name.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .Last();

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();

        var beginComment = $"-- {beginMarker}";
        var endComment = $"-- {endMarker}";

        var beginIndex = sql.IndexOf(beginComment, StringComparison.Ordinal);
        beginIndex.ShouldBeGreaterThanOrEqualTo(0, $"Marker '{beginComment}' was not found in the read view migration.");

        var contentStart = beginIndex + beginComment.Length;
        var endIndex = sql.IndexOf(endComment, contentStart, StringComparison.Ordinal);
        endIndex.ShouldBeGreaterThan(contentStart, $"Marker '{endComment}' was not found after '{beginComment}'.");

        return sql[contentStart..endIndex];
    }
}
