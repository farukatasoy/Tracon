using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Tracon.UpgradeGate;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// The migrations of the tree start with the files the last release shipped, and the few that differ say why.
/// </summary>
/// <remarks>
/// <para>
/// 🚨 The upgrade gate builds the schema of the release from the migrations in the tree up to the last file the release
/// held. That is only the schema the release had if those files are the release's files. This test is the proof: it holds
/// each file against what the release tag said (<c>UpgradeBaseline.json</c>, written from the tag by
/// <c>scripts/upgrade_baseline.py</c>), so a statement edited inside an applied migration is red here whatever the
/// integrity manifest of the applied migrations was moved to.
/// </para>
/// <para>
/// A preview line has no upgrade path for stored data between previews, so a comment may move: the files are compared
/// by their statements, and the ledger checksum of the release is allowed to differ. From the first release candidate
/// the files are compared byte for byte and no exemption is allowed. An exemption names a file whose statements were
/// changed on purpose; it is only valid while the file still differs, and it dies with the next baseline.
/// </para>
/// </remarks>
public sealed class UpgradeBaselinePinTests
{
    [Theory]
    [InlineData("sqlite", "Sqlite")]
    [InlineData("postgresql", "PostgreSql")]
    [InlineData("sqlserver", "SqlServer")]
    public void Every_migration_the_last_release_held_is_in_the_tree_with_the_same_statements(string directory, string provider)
    {
        var baseline = UpgradeBaseline.Load();
        var files = TreeMigrations(ProviderAssembly(provider), provider);
        var problems = new List<string>();

        baseline.Providers[directory].ShouldNotBeEmpty();

        foreach (var released in baseline.Providers[directory])
        {
            if (!files.TryGetValue(released.Name, out var sql))
            {
                problems.Add($"{released.Name}: the release held it and the tree does not");
                continue;
            }

            var statements = UpgradeBaseline.StatementDigest(sql);
            var exemption = baseline.Exemptions.SingleOrDefault(candidate =>
                string.Equals(candidate.Provider, directory, StringComparison.Ordinal)
                && string.Equals(candidate.Name, released.Name, StringComparison.Ordinal));

            if (baseline.ChecksumsMayDrift)
            {
                if (string.Equals(statements, released.Statements, StringComparison.Ordinal))
                {
                    if (exemption is not null)
                    {
                        problems.Add($"{released.Name}: listed as edited on purpose, but its statements are the release's again; remove the exemption");
                    }
                }
                else if (exemption is null)
                {
                    problems.Add($"{released.Name}: the statements differ from the release and no exemption explains it");
                }
                else if (!string.Equals(exemption.Statements, statements, StringComparison.Ordinal))
                {
                    problems.Add($"{released.Name}: edited again after the exemption was written; the exemption names other statements");
                }
            }
            else if (!string.Equals(Checksum(sql), released.Checksum, StringComparison.Ordinal))
            {
                problems.Add($"{released.Name}: the file differs from the release byte for byte; an applied migration is never edited, add a new one");
            }
        }

        foreach (var exemption in baseline.Exemptions.Where(candidate => string.Equals(candidate.Provider, directory, StringComparison.Ordinal)))
        {
            if (baseline.Providers[directory].All(released => !string.Equals(released.Name, exemption.Name, StringComparison.Ordinal)))
            {
                problems.Add($"{exemption.Name}: exempted, but the release did not hold it");
            }

            if (string.IsNullOrWhiteSpace(exemption.Reason))
            {
                problems.Add($"{exemption.Name}: an exemption states why the file was edited");
            }
        }

        problems.ShouldBeEmpty(
            $"{baseline.Tag} is the last release; the files it held are the schema the upgrade gate starts from. " +
            "A new change to the schema is a new migration");
    }

    [Theory]
    [InlineData("sqlite", "Sqlite")]
    [InlineData("postgresql", "PostgreSql")]
    [InlineData("sqlserver", "SqlServer")]
    public void Every_optional_migration_the_last_release_held_is_in_the_tree_with_the_same_statements(string directory, string provider)
    {
        var baseline = UpgradeBaseline.Load();
        var problems = new List<string>();

        baseline.OptionalSets[directory].ShouldNotBeEmpty($"{directory}: the release offered at least the views set");

        foreach (var (set, released) in baseline.OptionalSets[directory])
        {
            var files = TreeOptionalMigrations(ProviderAssembly(provider), provider, set);

            foreach (var file in released)
            {
                if (!files.TryGetValue(file.Name, out var sql))
                {
                    problems.Add($"{set}/{file.Name}: the release held it and the tree does not");
                }
                else if (!string.Equals(UpgradeBaseline.StatementDigest(sql), file.Statements, StringComparison.Ordinal))
                {
                    // An optional set has no exemption list: its files are never edited, a change is a new file.
                    problems.Add($"{set}/{file.Name}: the statements differ from the release; an applied migration is never edited, add a new one");
                }
            }
        }

        problems.ShouldBeEmpty(
            $"{baseline.Tag} is the last release; the files its optional sets held are the schema the upgrade gate starts from");
    }

    [Fact]
    public void A_release_candidate_or_later_baseline_allows_no_exemption()
    {
        var baseline = UpgradeBaseline.Load();

        if (!baseline.ChecksumsMayDrift)
        {
            baseline.Exemptions.ShouldBeEmpty("from the first release candidate on an applied migration is never edited");
        }
    }

    [Fact]
    public void The_baseline_names_one_release_for_all_three_providers()
    {
        var baseline = UpgradeBaseline.Load();

        baseline.Providers.Keys.Order(StringComparer.Ordinal).ShouldBe(["postgresql", "sqlite", "sqlserver"]);
        baseline.Tag.ShouldBe("v" + baseline.Version);

        foreach (var (directory, migrations) in baseline.Providers)
        {
            migrations.Select(static migration => migration.Id).ShouldBe(
                migrations.Select(static migration => migration.Id).Order().Distinct(),
                $"{directory}: the sequence numbers are ordered and unique");
        }
    }

    /// <summary>The checksum the migration runner writes into the ledger.</summary>
    private static string Checksum(string sql)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql.Replace("\r\n", "\n", StringComparison.Ordinal))));

    private static Assembly ProviderAssembly(string provider) => provider switch
    {
        "Sqlite" => typeof(SqliteQueries).Assembly,
        "PostgreSql" => typeof(PostgresQueries).Assembly,
        _ => typeof(SqlServerQueries).Assembly,
    };

    /// <summary>The migrations of one optional set (<c>views</c>, <c>knowledge</c>) a provider embeds, by name.</summary>
    private static Dictionary<string, string> TreeOptionalMigrations(Assembly assembly, string provider, string set)
    {
        var prefix = $"Tracon.{provider}.Migrations{char.ToUpperInvariant(set[0])}{set[1..]}.";
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);

            files[resource[prefix.Length..^".sql".Length]] = reader.ReadToEnd();
        }

        files.ShouldNotBeEmpty($"{provider} embeds no migration of the {set} set");

        return files;
    }

    /// <summary>The core migrations a provider embeds, by name.</summary>
    private static Dictionary<string, string> TreeMigrations(Assembly assembly, string provider)
    {
        var prefix = $"Tracon.{provider}.Migrations.";
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".sql", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);

            files[resource[prefix.Length..^".sql".Length]] = reader.ReadToEnd();
        }

        files.ShouldNotBeEmpty($"{provider} embeds no core migration");

        return files;
    }
}
