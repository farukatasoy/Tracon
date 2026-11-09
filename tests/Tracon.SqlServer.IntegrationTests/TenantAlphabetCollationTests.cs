using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Tracon.SqlServer.IntegrationTests.Infrastructure;

namespace Tracon.SqlServer.IntegrationTests;

/// <summary>
/// Measures the claim the tenant rule rests on: inside the tenant alphabet, the collation of a tenant column
/// joins no two identifiers that differ after the fold.
/// </summary>
/// <remarks>
/// <para>
/// The tenant columns keep the collation of the database, which ignores letter case, accents and width. The
/// rule that holds the isolation is not a collation: <see cref="AmbientTenantScope.Normalize"/> admits only the
/// ASCII letters, the digits, <c>_</c>, <c>.</c> and <c>-</c>, and folds the letters to lower case. That is enough
/// only if no collation treats two different strings over those characters as equal (a separator the collation
/// ignores would turn <c>a-b</c> and <c>ab</c> into one tenant). So this class asks the server.
/// </para>
/// <para>
/// The alphabet is derived from the code, never listed here: every character of the basic plane that
/// <see cref="AmbientTenantScope.TryNormalize"/> accepts, in its canonical form. The comparison runs on a column
/// declared with the collation of the real <c>tenant_id</c> column, and covers every string of one and of two
/// characters, which includes each separator against each letter. The legs of the matrix
/// (<c>TRACON_TEST_SQLSERVER_COLLATION</c>, <c>TRACON_TEST_SQLSERVER_LCID</c>, the 2019 image) run the same class.
/// </para>
/// </remarks>
public sealed class TenantAlphabetCollationTests(SqlServerSchemaFixture schema) : IClassFixture<SqlServerSchemaFixture>
{
    private const int LoginFailedNumber = 18456;

    [Fact]
    public async Task No_two_different_canonical_strings_over_the_alphabet_are_equal_in_a_tenant_column()
    {
        // No reset: the class reads the catalog and writes only to temporary tables, so the schema holds no row of
        // its own. The first statement of each test stays the catalog read, which handles the login failure below.
        var context = schema.Context;

        var alphabet = Alphabet();

        alphabet.Count.ShouldBe(39, "the alphabet is the 26 letters in lower case, the 10 digits and the three separators");

        var strings = alphabet
            .Select(static character => character.ToString())
            .Concat(alphabet.SelectMany(static first => Alphabet().Select(second => string.Concat(first, second))))
            .ToList();

        var collation = await TenantColumnCollationAsync(context);
        var joined = await EqualPairsAsync(context, collation, strings);

        joined.ShouldBeEmpty(
            $"under {collation} the strings listed are equal to another string of the alphabet, so two tenants would be one; " +
            "the tenant rule has to be narrower or the tenant columns have to compare by code point");
    }

    [Fact]
    public async Task The_measurement_sees_a_fold_when_there_is_one_outside_the_alphabet()
    {
        // No reset, for the reason the test above gives.
        var context = schema.Context;

        // The control that gives the test above its meaning: a query that could not see the collation fold
        // anything would also report no pair inside the alphabet. Composed against decomposed is the case the
        // tenant rule exists for (a tenant written both ways is one tenant to the server and two to the others).
        var composed = char.ConvertFromUtf32(0x00E9);
        var decomposed = "e" + char.ConvertFromUtf32(0x0301);
        var fullWidth = char.ConvertFromUtf32(0xFF41);

        AmbientTenantScope.TryNormalize(composed, out _).ShouldBeFalse();
        AmbientTenantScope.TryNormalize(decomposed, out _).ShouldBeFalse();
        AmbientTenantScope.TryNormalize(fullWidth, out _).ShouldBeFalse();

        var collation = await TenantColumnCollationAsync(context);
        var joined = await EqualPairsAsync(context, collation, [composed, decomposed, "a", fullWidth]);

        joined.ShouldNotBeEmpty($"under {collation} an accented letter, composed and decomposed, is expected to compare equal");
    }

    /// <summary>Every character of the basic plane the tenant rule accepts, as its canonical single character.</summary>
    private static List<char> Alphabet()
        => Enumerable.Range(0, 0x10000)
            .Select(static code => (char)code)
            .Select(static character => AmbientTenantScope.TryNormalize(character.ToString(), out var canonical) ? canonical : null)
            .Where(static canonical => canonical is not null)
            .Select(static canonical => canonical![0])
            .Distinct()
            .Order()
            .ToList();

    private static async ValueTask<string> TenantColumnCollationAsync(SqlServerTestContext context)
    {
        const string query =
            "SELECT c.collation_name FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'{0}.sessions') AND c.name = N'tenant_id';";
        var sql = string.Format(CultureInfo.InvariantCulture, query, context.SchemaName);

        string? collation;

        try
        {
            collation = await context.CatalogScalarAsync<string>(sql);
        }
        catch (SqlException failure) when (failure.Number == LoginFailedNumber)
        {
            // On a Turkish server the first pooled connection can come back from sp_reset_connection with a login
            // failure; the pool drops that connection and the second try gets a fresh one. The cause is not in the
            // collation under test.
            SqlConnection.ClearAllPools();
            collation = await context.CatalogScalarAsync<string>(sql);
        }

        collation.ShouldNotBeNull("sessions.tenant_id has to be a text column of the migrated schema");

        return collation;
    }

    /// <summary>Returns the strings of <paramref name="strings"/> that compare equal to a different one, under the collation.</summary>
    private static async ValueTask<IReadOnlyList<string>> EqualPairsAsync(SqlServerTestContext context, string collation, List<string> strings)
    {
        var table = string.Create(CultureInfo.InvariantCulture, $"#tenant_probe_{Guid.NewGuid():N}");
        var script = new StringBuilder();

        // One batch on one connection: a temporary table lives only as long as the session that made it.
        script.Append(CultureInfo.InvariantCulture, $"CREATE TABLE {table} (id int NOT NULL PRIMARY KEY, v nvarchar(10) COLLATE {collation} NOT NULL);");

        for (var offset = 0; offset < strings.Count; offset += 500)
        {
            script.Append(CultureInfo.InvariantCulture, $"INSERT INTO {table} (id, v) VALUES ");
            script.Append(string.Join(
                ", ",
                strings.Skip(offset).Take(500).Select((text, index) => string.Create(CultureInfo.InvariantCulture, $"({offset + index}, {ToUnicodeLiteral(text)})"))));
            script.Append(';');
        }

        script.Append(
            CultureInfo.InvariantCulture,
            $"SELECT STRING_AGG(CAST(CONCAT(a.id, N'=', b.id) AS nvarchar(max)), N';') FROM {table} a JOIN {table} b ON a.v = b.v AND a.id < b.id;");

        var pairs = await context.ScalarAsync<string>(script.ToString());

        return (pairs ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=') is [var left, var right]
                ? $"{Show(strings[int.Parse(left, CultureInfo.InvariantCulture)])} = {Show(strings[int.Parse(right, CultureInfo.InvariantCulture)])}"
                : pair)
            .ToList();
    }

    /// <summary>Writes a string as <c>NCHAR</c> code units, so no character of it depends on how the script is encoded.</summary>
    private static string ToUnicodeLiteral(string text)
        => string.Join(" + ", text.Select(static character => string.Create(CultureInfo.InvariantCulture, $"NCHAR({(int)character})")));

    private static string Show(string text)
        => string.Join(' ', text.Select(static character => string.Create(CultureInfo.InvariantCulture, $"U+{(int)character:X4}")));
}
