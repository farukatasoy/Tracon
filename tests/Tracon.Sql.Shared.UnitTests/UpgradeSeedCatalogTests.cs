using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Tracon.UpgradeGate;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The rows the upgrade gate fills the old database with cover every table, and every column of every table, of every
/// provider.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 A table the seed leaves empty is a table the upgrade is never tried on: a migration that fails only on rows
/// (a column that cannot be NOT NULL, a cast that one value breaks, a key two rows share) passes over an empty table
/// and fails on the first customer's data. This catalog makes the next table, and the next column, a failure of the
/// build until a row carries it.
/// </para>
/// <para>
/// The schema is read from each provider's own embedded migrations (text, not a database), as the other catalogs of this
/// project do. A column a later migration dropped or renamed is still listed, because the seed is also written into the
/// schema of every earlier migration.
/// </para>
/// </remarks>
public sealed class UpgradeSeedCatalogTests
{
    /// <summary>Columns a migration creates that the seed leaves empty, and why. The list only shrinks.</summary>
    private static readonly Dictionary<string, string> UnseededColumns = new(StringComparer.Ordinal)
    {
        ["audit_log.key_id"] = "added after the release, so a row the release wrote cannot hold one; signed chains are the audit tests' business",
        ["audit_log.chain_seq"] = "assigned by the database",
        ["run_scores.message_key"] = "computed from message_id",
        ["quota_usage.notified_thresholds"] = "its encoding differs by provider and it is only appended to, never read back by value",
    };

    /// <summary>The names a migration moves away from: a table it rebuilds under another name and a column it renames.</summary>
    private static readonly Regex RenamedAway = new(
        @"ALTER TABLE \{schema\}\.?(?<table>\w+)\s+RENAME (?:TO|COLUMN (?<column>\w+) TO)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void Every_table_the_migrations_create_has_a_row_in_the_seed(string provider)
    {
        var schema = MigrationSchema.Read(ProviderAssembly(provider));
        var seed = UpgradeSeed.Load();

        var transient = RenamedAwayNames(provider).Where(static name => !name.Contains('.', StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

        var empty = schema.Keys
            .Where(table => !transient.Contains(table) && (!seed.Tables.TryGetValue(table, out var rows) || rows.Count == 0))
            .Order(StringComparer.Ordinal)
            .ToList();

        empty.ShouldBeEmpty(
            $"{provider}: add a row of each table to tests/Shared/Upgrade/UpgradeSeed.json, written with the columns the table has had, " +
            "so the upgrade gate tries the migrations on it");
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void Every_table_of_the_seed_exists_in_the_migrations(string provider)
    {
        var schema = MigrationSchema.Read(ProviderAssembly(provider));
        var seed = UpgradeSeed.Load();

        seed.Tables.Keys.Where(table => !schema.ContainsKey(table)).Order(StringComparer.Ordinal).ShouldBeEmpty(
            $"{provider}: a table of the seed that no migration creates is a typo, or a table that was dropped");
    }

    [Theory]
    [InlineData("Sqlite")]
    [InlineData("PostgreSql")]
    [InlineData("SqlServer")]
    public void Every_column_of_every_table_is_filled_by_some_row_of_the_seed(string provider)
    {
        var schema = MigrationSchema.Read(ProviderAssembly(provider));
        var seed = UpgradeSeed.Load();
        var unfilled = new List<string>();
        var transient = RenamedAwayNames(provider).ToHashSet(StringComparer.Ordinal);

        foreach (var (table, (columns, _)) in schema)
        {
            if (!seed.Tables.TryGetValue(table, out var rows))
            {
                continue;
            }

            foreach (var column in columns.Distinct(StringComparer.Ordinal))
            {
                var filled = rows.Any(row => row.Has(column) && row[column].Kind != SeedKind.Null);

                if (!filled && !UnseededColumns.ContainsKey($"{table}.{column}") && !transient.Contains($"{table}.{column}"))
                {
                    unfilled.Add($"{table}.{column}");
                }
            }
        }

        unfilled.Order(StringComparer.Ordinal).ShouldBeEmpty(
            $"{provider}: a column no seed row fills is a column the upgrade never reads back with a value; " +
            "give a row a value for it (a column added later is skipped by the gate when the schema it seeds lacks it)");
    }

    [Fact]
    public void Every_column_a_seed_row_names_is_written_by_some_migration_of_some_provider()
    {
        var seed = UpgradeSeed.Load();
        var text = string.Join('\n', new[] { "Sqlite", "PostgreSql", "SqlServer" }.Select(static provider => AllMigrationText(ProviderAssembly(provider))));
        var typos = new List<string>();

        foreach (var (table, rows) in seed.Tables)
        {
            foreach (var column in rows.SelectMany(static row => row.Values.Select(static pair => pair.Key)).Distinct(StringComparer.Ordinal))
            {
                if (!Regex.IsMatch(text, $@"\b{Regex.Escape(column)}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(5)))
                {
                    typos.Add($"{table}.{column}");
                }
            }
        }

        typos.ShouldBeEmpty("the seed writes a column no migration has ever had; the gate would skip it without a word");
    }

    [Fact]
    public void The_unseeded_list_only_names_columns_that_exist()
    {
        // The check keeps a stale entry from outliving the column it excused.
        var schemas = new[] { "Sqlite", "PostgreSql", "SqlServer" }.Select(static provider => MigrationSchema.Read(ProviderAssembly(provider))).ToList();

        foreach (var entry in UnseededColumns.Keys)
        {
            var parts = entry.Split('.');

            schemas.Any(schema => schema.TryGetValue(parts[0], out var table) && table.Columns.Contains(parts[1], StringComparer.Ordinal))
                .ShouldBeTrue($"{entry} is excused but no provider has the column");
        }
    }

    /// <summary>The tables (<c>sessions_new</c>) and columns (<c>run_scores.value_real</c>) the provider's migrations rename away.</summary>
    private static IEnumerable<string> RenamedAwayNames(string provider)
    {
        foreach (Match match in RenamedAway.Matches(AllMigrationText(ProviderAssembly(provider))))
        {
            yield return match.Groups["column"].Success
                ? $"{match.Groups["table"].Value}.{match.Groups["column"].Value}"
                : match.Groups["table"].Value;
        }
    }

    private static Assembly ProviderAssembly(string provider) => provider switch
    {
        "Sqlite" => typeof(SqliteQueries).Assembly,
        "PostgreSql" => typeof(PostgresQueries).Assembly,
        _ => typeof(SqlServerQueries).Assembly,
    };

    private static string AllMigrationText(Assembly assembly)
    {
        var builder = new StringBuilder();

        foreach (var name in assembly.GetManifestResourceNames().Where(static name => name.Contains(".Migrations.", StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);

            builder.AppendLine(reader.ReadToEnd());
        }

        return builder.ToString();
    }
}
