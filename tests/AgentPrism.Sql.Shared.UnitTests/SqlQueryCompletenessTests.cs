using System.Reflection;

namespace AgentPrism.SqlProviders.Tests;

/// <summary>
/// Verifies that every <see cref="string"/> property on <c>SqlQueriesBase</c>
/// was actually assigned a query by the provider's constructor.
/// </summary>
/// <remarks>
/// <para>
/// <c>SqlQueriesBase</c>'s own XML documentation names this risk: a query
/// left unassigned stays <see cref="string.Empty"/> and the error surfaces
/// only at run time, when the empty text reaches the server. Phase 94 moves
/// 117 queries into the base class's constructor; a derived class that still
/// assigns the same property afterward would silently overwrite the shared
/// text (94.3, step 3) with whatever it computes -- or, if it simply forgets
/// to delete a since-shared field, leaves a dead assignment that this test
/// cannot see (that risk is closed by <see cref="SqlTextSnapshotTests"/>
/// instead, which fails on ANY text drift, not just emptiness).
/// </para>
/// <para>
/// One <see cref="object"/>, one exemption list (K-247: the same
/// <c>SqlQueriesBase</c> source compiles into three separate assemblies, so
/// there is no single shared type to reflect over once -- each provider is
/// checked independently, by its own concrete class name).
/// </para>
/// </remarks>
public sealed class SqlQueryCompletenessTests
{
    /// <summary>
    /// Properties allowed to stay empty, per provider, with the reason on
    /// record. An unlisted empty property fails the test.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExemptByProvider = new(StringComparer.Ordinal)
    {
        // SQLite cannot express "add this column only if missing" or widen a
        // primary key in a static SQL string; SqliteDialect.UpgradeMigrationsTableAsync
        // does the equivalent rebuild in code instead (K-475).
        [nameof(SqliteQueries)] = ["UpgradeMigrationsTable"],
    };

    [Fact]
    public void Postgres_leaves_no_query_empty()
        => AssertComplete(new PostgresQueries("agentprism"));

    [Fact]
    public void SqlServer_leaves_no_query_empty()
        => AssertComplete(new SqlServerQueries("agentprism"));

    [Fact]
    public void Sqlite_leaves_no_query_empty()
        => AssertComplete(new SqliteQueries("agentprism_"));

    private static void AssertComplete(object queries)
    {
        var type = queries.GetType();
        var exempt = ExemptByProvider.GetValueOrDefault(type.Name, []);

        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0);

        var empty = new List<string>();
        var leftoverPlaceholder = new List<string>();

        foreach (var property in properties)
        {
            if (exempt.Contains(property.Name, StringComparer.Ordinal))
            {
                continue;
            }

            var value = (string?)property.GetValue(queries) ?? string.Empty;

            if (value.Length == 0)
            {
                empty.Add(property.Name);
            }
            else if (value.Contains("{Schema}", StringComparison.Ordinal))
            {
                // Only possible when a raw (non-interpolated) string literal was
                // written where an interpolated one was meant -- the schema
                // placeholder is otherwise always resolved at construction time.
                leftoverPlaceholder.Add(property.Name);
            }
        }

        empty.ShouldBeEmpty(
            $"{type.Name} left these queries unassigned (stayed string.Empty): {string.Join(", ", empty)}.");
        leftoverPlaceholder.ShouldBeEmpty(
            $"{type.Name} has an unresolved '{{Schema}}' placeholder in: {string.Join(", ", leftoverPlaceholder)}.");
    }
}
