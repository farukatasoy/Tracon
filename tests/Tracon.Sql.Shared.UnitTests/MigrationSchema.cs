using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Tracon.SqlProviders.Tests;

/// <summary>
/// Reads the schema a provider's embedded migrations create: every table with its columns and its definition text.
/// </summary>
/// <remarks>
/// Text, not a database: these tests run without a container, so what the migrations say is all they can know. The
/// optional migration sets (pgvector, read views) are left out, as in <c>MigrationParityTests</c>, because they are
/// not applied to every deployment.
/// </remarks>
internal static class MigrationSchema
{
    private static readonly Regex CreateTable = new(
        @"CREATE TABLE (?:IF NOT EXISTS )?\{schema\}\.?(?<table>\w+)\s*\((?<body>.*?)\n\);",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AlterTable = new(
        @"ALTER TABLE \{schema\}\.?(?<table>\w+)(?<body>.*?);",
        RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(5));

    private static readonly Regex AddedColumn = new(
        @"\bADD\s+(?:COLUMN\s+)?(?:IF NOT EXISTS\s+)?(?<column>\w+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ColumnLine = new(
        @"^\s*(?<column>\w+)\s+\w+",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly string[] NotColumns = ["CONSTRAINT", "PRIMARY", "UNIQUE", "FOREIGN", "CHECK", "INDEX"];

    /// <summary>Every table the provider's migrations create, with its columns and its definition text.</summary>
    /// <param name="providerAssembly">The provider assembly that embeds the migrations.</param>
    /// <returns>The tables by name.</returns>
    public static Dictionary<string, (List<string> Columns, string Body)> Read(Assembly providerAssembly)
    {
        var sql = string.Join(
            '\n',
            EmbeddedMigrations(providerAssembly).Split('\n').Select(static line => line.Split("--", 2)[0]));

        var schema = new Dictionary<string, (List<string> Columns, string Body)>(StringComparer.Ordinal);

        foreach (Match match in CreateTable.Matches(sql))
        {
            var columns = match.Groups["body"].Value
                .Split('\n')
                .Select(static line => ColumnLine.Match(line))
                .Where(static column => column.Success)
                .Select(static column => column.Groups["column"].Value)
                .Where(static column => !NotColumns.Contains(column, StringComparer.OrdinalIgnoreCase))
                .ToList();

            schema[match.Groups["table"].Value] = (columns, match.Groups["body"].Value);
        }

        foreach (Match match in AlterTable.Matches(sql))
        {
            if (!schema.TryGetValue(match.Groups["table"].Value, out var table))
            {
                continue;
            }

            table.Columns.AddRange(AddedColumn.Matches(match.Groups["body"].Value)
                .Select(static added => added.Groups["column"].Value)
                .Where(static column => !NotColumns.Contains(column, StringComparer.OrdinalIgnoreCase)));
        }

        return schema;
    }

    private static string EmbeddedMigrations(Assembly assembly)
    {
        var builder = new StringBuilder();

        foreach (var name in assembly
            .GetManifestResourceNames()
            .Where(static name => name.Contains(".Migrations.", StringComparison.Ordinal))
            .Where(static name => name.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            builder.AppendLine(reader.ReadToEnd());
        }

        builder.Length.ShouldBeGreaterThan(0, $"{assembly.GetName().Name} embeds no migration files");

        return builder.ToString();
    }
}
