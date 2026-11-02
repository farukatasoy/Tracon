using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tracon.UpgradeGate;

/// <summary>The three SQL providers the seed is written for.</summary>
internal enum SeedProvider
{
    /// <summary>SQLite.</summary>
    Sqlite,

    /// <summary>PostgreSQL.</summary>
    PostgreSql,

    /// <summary>SQL Server.</summary>
    SqlServer,
}

/// <summary>One row of the seed: the column values in the order the file lists them.</summary>
/// <param name="Table">The table the row belongs to.</param>
/// <param name="Values">The column name and value pairs.</param>
internal sealed record SeedRow(string Table, IReadOnlyList<KeyValuePair<string, SeedValue>> Values)
{
    /// <summary>Gets the value of a column the row names.</summary>
    /// <param name="column">The column.</param>
    /// <returns>The value.</returns>
    public SeedValue this[string column] => Values.Single(pair => string.Equals(pair.Key, column, StringComparison.Ordinal)).Value;

    /// <summary>Whether the row names the column.</summary>
    /// <param name="column">The column.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Has(string column) => Values.Any(pair => string.Equals(pair.Key, column, StringComparison.Ordinal));
}

/// <summary>The kind of a value in the seed.</summary>
internal enum SeedKind
{
    /// <summary>NULL.</summary>
    Null,

    /// <summary>Text.</summary>
    Text,

    /// <summary>A whole number.</summary>
    Integer,

    /// <summary>A floating point number.</summary>
    Real,

    /// <summary>A decimal, kept as the text it was written with.</summary>
    Decimal,

    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>A GUID derived from a name.</summary>
    Guid,

    /// <summary>A point in time.</summary>
    Timestamp,

    /// <summary>Bytes.</summary>
    Bytes,

    /// <summary>JSON text.</summary>
    Json,

    /// <summary>An array of text, a native array where the provider has one and JSON text elsewhere.</summary>
    TextArray,
}

/// <summary>A value of the seed with its kind.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Text">The text form of the value (the JSON text of a JSON value).</param>
/// <param name="Items">The items of a text array.</param>
internal sealed record SeedValue(SeedKind Kind, string Text, IReadOnlyList<string>? Items = null)
{
    /// <summary>The NULL value.</summary>
    public static SeedValue Null { get; } = new(SeedKind.Null, string.Empty);

    /// <summary>Reads the value as a GUID.</summary>
    public Guid AsGuid => UpgradeSeed.Id(Text);

    /// <summary>Reads the value as a point in time.</summary>
    public DateTimeOffset AsTimestamp => DateTimeOffset.Parse(Text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}

/// <summary>
/// The rows the upgrade gate puts into every table of the released schema, written once and never regenerated:
/// the data a deployed release holds is not something the current code may rewrite.
/// </summary>
/// <remarks>
/// <para>
/// The file lists, for each table, the columns of every shape the table has had (a column a later migration renamed
/// or dropped is listed under its old name). A row is written with the columns the table has at the moment it is
/// seeded and the rest are left to the database, so the same rows seed the schema of the release and the schema
/// after any one migration.
/// </para>
/// <para>
/// Values are plain JSON, with a prefix where the type is not obvious: <c>g:name</c> a GUID derived from the name,
/// <c>ts:</c> a timestamp, <c>d:</c> a decimal, <c>x:</c> bytes, <c>{"$a": [...]}</c> an array of text. An object or an
/// array is JSON text.
/// </para>
/// </remarks>
internal sealed class UpgradeSeed
{
    private const string ResourceName = "UpgradeSeed.json";

    private UpgradeSeed(string tenant, IReadOnlyDictionary<string, IReadOnlyList<SeedRow>> tables)
    {
        Tenant = tenant;
        Tables = tables;
    }

    /// <summary>Gets the tenant the stores of the gate run as.</summary>
    public string Tenant { get; }

    /// <summary>Gets the rows by table, in the order the file lists the tables (a parent before the table that points at it).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SeedRow>> Tables { get; }

    /// <summary>Reads the seed embedded in the test assembly.</summary>
    /// <returns>The seed.</returns>
    public static UpgradeSeed Load()
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The test assembly does not embed '{ResourceName}'.");

        var root = JsonNode.Parse(stream)!.AsObject();
        var tables = new Dictionary<string, IReadOnlyList<SeedRow>>(StringComparer.Ordinal);

        foreach (var (table, rows) in root["tables"]!.AsObject())
        {
            tables[table] = [.. rows!.AsArray().Select(row => ReadRow(table, row!.AsObject()))];
        }

        return new UpgradeSeed(root["tenant"]!.GetValue<string>(), tables);
    }

    /// <summary>Derives the identifier a seed name stands for: stable, unique per name, and the same in every provider.</summary>
    /// <param name="name">The name written in the seed.</param>
    /// <returns>The GUID.</returns>
    public static Guid Id(string name)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("tracon-upgrade-seed:" + name));

        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>Gets the first row of a table whose column holds the text.</summary>
    /// <param name="table">The table.</param>
    /// <param name="column">The column.</param>
    /// <param name="text">The text, or the seed name of a GUID.</param>
    /// <returns>The row.</returns>
    public SeedRow Row(string table, string column, string text)
        => Tables[table].Single(row => row.Has(column) && string.Equals(row[column].Text, text, StringComparison.Ordinal));

    /// <summary>Renders the INSERT statement for a row, with the columns the table has.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="qualifiedTable">The table name as the provider addresses it.</param>
    /// <param name="row">The row.</param>
    /// <param name="liveColumns">The columns the table has now.</param>
    /// <returns>The statement, or <see langword="null"/> when the row has no column in common with the table.</returns>
    public static string? RenderInsert(
        SeedProvider provider,
        string qualifiedTable,
        SeedRow row,
        IReadOnlySet<string> liveColumns)
    {
        var present = row.Values.Where(pair => liveColumns.Contains(pair.Key)).ToList();

        if (present.Count == 0)
        {
            return null;
        }

        return $"INSERT INTO {qualifiedTable} ({string.Join(", ", present.Select(static pair => pair.Key))}) " +
            $"VALUES ({string.Join(", ", present.Select(pair => Render(provider, pair.Value)))});";
    }

    /// <summary>Renders a value as a SQL literal.</summary>
    /// <param name="provider">The provider.</param>
    /// <param name="value">The value.</param>
    /// <returns>The literal.</returns>
    public static string Render(SeedProvider provider, SeedValue value)
    {
        var sqlServer = provider == SeedProvider.SqlServer;

        switch (value.Kind)
        {
            case SeedKind.Null:
                return "NULL";
            case SeedKind.Boolean:
                return provider == SeedProvider.PostgreSql
                    ? (string.Equals(value.Text, "true", StringComparison.Ordinal) ? "true" : "false")
                    : (string.Equals(value.Text, "true", StringComparison.Ordinal) ? "1" : "0");
            case SeedKind.Integer:
            case SeedKind.Real:
                return value.Text;
            case SeedKind.Decimal:
                return provider == SeedProvider.Sqlite ? Quote(value.Text, unicode: false) : value.Text;
            case SeedKind.Guid:
                {
                    var id = Id(value.Text).ToString("D", CultureInfo.InvariantCulture);

                    return Quote(provider == SeedProvider.PostgreSql ? id : id.ToUpperInvariant(), unicode: false);
                }

            case SeedKind.Timestamp:
                {
                    var at = value.AsTimestamp.UtcDateTime;

                    return Quote(
                        provider switch
                        {
                            SeedProvider.PostgreSql => at.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + "+00",
                            SeedProvider.SqlServer => at.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "+00:00",
                            _ => at.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture) + "Z",
                        },
                        unicode: false);
                }

            case SeedKind.Bytes:
                return provider switch
                {
                    SeedProvider.Sqlite => $"X'{value.Text}'",
                    SeedProvider.PostgreSql => $"'\\x{value.Text}'",
                    _ => $"0x{value.Text}",
                };
            case SeedKind.TextArray:
                return provider == SeedProvider.PostgreSql
                    ? $"ARRAY[{string.Join(", ", value.Items!.Select(static item => Quote(item, unicode: false)))}]::text[]"
                    : Quote(JsonSerializer.Serialize(value.Items), sqlServer);
            default:
                return Quote(value.Text, sqlServer);
        }
    }

    private static string Quote(string text, bool unicode)
        => (unicode ? "N'" : "'") + text.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static SeedRow ReadRow(string table, JsonObject row)
        => new(table, [.. row.Select(static pair => KeyValuePair.Create(pair.Key, ReadValue(pair.Value)))]);

    private static SeedValue ReadValue(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return SeedValue.Null;
            case JsonObject { Count: 1 } array when array["$a"] is JsonArray items:
                return new SeedValue(SeedKind.TextArray, string.Empty, [.. items.Select(static item => item!.GetValue<string>())]);
            case JsonObject or JsonArray:
                return new SeedValue(SeedKind.Json, node.ToJsonString());
            case JsonValue value when value.TryGetValue<bool>(out var flag):
                return new SeedValue(SeedKind.Boolean, flag ? "true" : "false");
            case JsonValue value when value.TryGetValue<long>(out var whole):
                return new SeedValue(SeedKind.Integer, whole.ToString(CultureInfo.InvariantCulture));
            case JsonValue value when value.TryGetValue<double>(out var real):
                return new SeedValue(SeedKind.Real, real.ToString("R", CultureInfo.InvariantCulture));
            case JsonValue value:
                {
                    var text = value.GetValue<string>();

                    return text switch
                    {
                        _ when text.StartsWith("g:", StringComparison.Ordinal) => new SeedValue(SeedKind.Guid, text[2..]),
                        _ when text.StartsWith("ts:", StringComparison.Ordinal) => new SeedValue(SeedKind.Timestamp, text[3..]),
                        _ when text.StartsWith("d:", StringComparison.Ordinal) => new SeedValue(SeedKind.Decimal, text[2..]),
                        _ when text.StartsWith("x:", StringComparison.Ordinal) => new SeedValue(SeedKind.Bytes, text[2..]),
                        _ => new SeedValue(SeedKind.Text, text),
                    };
                }

            default:
                throw new InvalidOperationException($"The seed holds a value it cannot read: {node.ToJsonString()}.");
        }
    }
}
