using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// No column of the schema keeps a number as a binary float.
/// </summary>
/// <remarks>
/// SQLite gives a column its affinity from the declared type. A <c>NUMERIC</c>, <c>REAL</c>, <c>FLOAT</c>, <c>DOUBLE</c> or
/// <c>DECIMAL</c> column turns the text the driver writes for a decimal into a double and keeps only 15 digits of it; the
/// amount then reads back different from what was written, and the SQLite version decides how. Every other cost column was
/// <c>TEXT</c> from the start; seven were not. The list below is the whole of what may stay a float, with the reason, and it
/// only shrinks.
/// </remarks>
public sealed class SqliteSchemaAffinityTests(SqliteFixture fixture)
{
    private static readonly string[] FloatTypes = ["NUMERIC", "REAL", "FLOAT", "DOUBLE", "DECIMAL"];

    /// <summary>The columns that are floats on purpose: a score is a measure, not an amount of money.</summary>
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "run_scores.value",
    };

    [Fact]
    public async Task No_column_is_declared_with_a_floating_point_affinity_except_the_listed_ones()
    {
        await using var context = await SqliteTestContext.CreateAsync(fixture);
        var prefix = context.TablePrefix;

        var found = new List<string>();

        var like = prefix.Replace("_", "\\_", StringComparison.Ordinal) + "%";

        await using (var command = context.DataSource.CreateCommand(
            "SELECT m.name, p.name, p.type FROM sqlite_master m, pragma_table_info(m.name) p " +
            $"WHERE m.type = 'table' AND m.name LIKE '{like}' ESCAPE '\\' ORDER BY m.name, p.cid;"))
        {
            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var table = reader.GetString(0)[prefix.Length..];
                var column = reader.GetString(1);
                var type = reader.GetString(2).ToUpperInvariant();

                if (FloatTypes.Any(floatType => type.Contains(floatType, StringComparison.Ordinal)) && !Allowed.Contains($"{table}.{column}"))
                {
                    found.Add($"{table}.{column} {type}");
                }
            }
        }

        found.ShouldBeEmpty("a column that holds an amount must be TEXT, so that the driver's decimal text is kept as it is");
    }
}
