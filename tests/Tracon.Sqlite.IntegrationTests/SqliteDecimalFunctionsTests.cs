using System.Data.Common;
using System.Globalization;
using CsCheck;
using Microsoft.Data.Sqlite;
using Tracon.Tests.Differential;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// The two decimal functions every connection of the SQLite data source carries: the exact sum a cost total is built on and
/// the exact addition of the quota counter.
/// </summary>
/// <remarks>
/// The built-in <c>SUM</c> and <c>+</c> turn a TEXT decimal into a binary double first, so <c>0.1 + 0.2</c> is
/// <c>0.30000000000000004</c>. Each test runs on a connection the data source opened, because that is the only place the
/// functions are registered.
/// </remarks>
public sealed class SqliteDecimalFunctionsTests
{
    private const string Memory = "Data Source=:memory:";

    [Fact]
    public async Task The_built_in_sum_is_the_float_error_the_function_removes()
    {
        await using var connection = await OpenAsync();

        // The premise, stated so that no SQLite version can hide it: the engine's own sum is a double that is not 0.3
        // (older versions print it as 0.3 when it is turned into text, newer ones as 0.30000000000000004).
        (await ScalarAsync(connection, "SELECT SUM(v) = 0.3 FROM (SELECT '0.1' AS v UNION ALL SELECT '0.2')"))
            .ShouldBe("0", "SQLite itself adds TEXT decimals as doubles");

        (await ScalarAsync(connection, "SELECT tracon_decimal_sum(v) FROM (SELECT '0.1' AS v UNION ALL SELECT '0.2')"))
            .ShouldBe("0.3");
    }

    [Fact]
    public async Task One_sum_over_several_columns_takes_every_term_and_skips_the_null_ones()
    {
        await using var connection = await OpenAsync();

        const string rows = "(SELECT '0.1' AS a, '0.2' AS b, NULL AS c, '0.0000000001' AS d " +
            "UNION ALL SELECT '6765.18', NULL, '99.99', NULL " +
            "UNION ALL SELECT NULL, NULL, NULL, NULL)";

        (await ScalarAsync(connection, $"SELECT tracon_decimal_sum(a, b, c, d) FROM {rows}"))
            .ShouldBe("6865.4700000001");
    }

    [Fact]
    public async Task A_sum_with_no_value_to_add_is_null_like_the_built_in_one()
    {
        await using var connection = await OpenAsync();

        (await ScalarAsync(connection, "SELECT tracon_decimal_sum(v) FROM (SELECT NULL AS v UNION ALL SELECT NULL)")).ShouldBeNull();
        (await ScalarAsync(connection, "SELECT tracon_decimal_sum(v) FROM (SELECT 1 AS v) WHERE 0")).ShouldBeNull();
        (await ScalarAsync(connection, "SELECT COALESCE(tracon_decimal_sum(v), '0') FROM (SELECT NULL AS v)")).ShouldBe("0");
    }

    [Fact]
    public async Task A_whole_number_a_real_and_an_exponent_are_read_as_the_decimal_they_stand_for()
    {
        await using var connection = await OpenAsync();

        (await ScalarAsync(connection, "SELECT tracon_decimal_sum(v) FROM (SELECT 3 AS v UNION ALL SELECT 0.5 UNION ALL SELECT '1e-3' UNION ALL SELECT '0.00001234')"))
            .ShouldBe("3.50101234");
    }

    [Fact]
    public async Task A_value_that_is_not_a_number_fails_instead_of_counting_as_zero()
    {
        await using var connection = await OpenAsync();

        // The built-in SUM reads 'abc' as 0 and goes on; a cost total that did the same would under-report.
        foreach (var sql in new[]
        {
            "SELECT tracon_decimal_sum(v) FROM (SELECT 'abc' AS v)",
            "SELECT tracon_decimal_sum(v) FROM (SELECT x'00' AS v)",
            "SELECT tracon_decimal_add('abc', '1')",
        })
        {
            var failure = await Should.ThrowAsync<SqliteException>(() => ScalarAsync(connection, sql));

            failure.Message.ShouldNotContain("no such function", Case.Insensitive, sql);

            // The text of the error is the function's own. The framework's text for a failed parse holds the value that
            // failed, and a value in a cost column does not belong in an error that travels to a log or a response.
            failure.Message.ShouldNotContain("abc", Case.Insensitive, sql);
        }
    }

    [Theory]
    [InlineData("0,5")]
    [InlineData("1,5")]
    [InlineData("1,000.5")]
    [InlineData("5-")]
    [InlineData("(5)")]
    public async Task A_text_that_a_culture_would_read_as_a_number_but_is_not_one_fails(string text)
    {
        await using var connection = await OpenAsync();

        // A thousands separator or a trailing sign is not part of the text a cost is stored as. Read leniently, a hand-written
        // '1,5' sums as 15 and 'abc' is an error: the rule is that an invalid value is an error, whatever it resembles.
        foreach (var sql in new[]
        {
            "SELECT tracon_decimal_sum(v) FROM (SELECT '" + text + "' AS v)",
            "SELECT tracon_decimal_add('" + text + "', '1')",
        })
        {
            var failure = await Should.ThrowAsync<SqliteException>(() => ScalarAsync(connection, sql));

            failure.Message.ShouldNotContain("no such function", Case.Insensitive, sql);
        }
    }

    [Fact]
    public async Task A_sum_past_the_range_of_a_decimal_fails()
    {
        await using var connection = await OpenAsync();

        var failure = await Should.ThrowAsync<SqliteException>(() => ScalarAsync(
            connection,
            "SELECT tracon_decimal_sum(v) FROM (SELECT '79228162514264337593543950335' AS v UNION ALL SELECT '1')"));

        failure.Message.ShouldNotContain("no such function", Case.Insensitive);
    }

    [Fact]
    public async Task Adding_two_decimals_is_exact_and_a_null_makes_the_result_null()
    {
        await using var connection = await OpenAsync();

        (await ScalarAsync(connection, "SELECT tracon_decimal_add('0.1', '0.2')")).ShouldBe("0.3");
        (await ScalarAsync(connection, "SELECT tracon_decimal_add('0.7', '0.1')")).ShouldBe("0.8");
        (await ScalarAsync(connection, "SELECT tracon_decimal_add('0.30000000000000004', '0.1')")).ShouldBe("0.40000000000000004");
        (await ScalarAsync(connection, "SELECT tracon_decimal_add('0.1', NULL)")).ShouldBeNull();
        (await ScalarAsync(connection, "SELECT tracon_decimal_add(NULL, '0.1')")).ShouldBeNull();
    }

    [Fact]
    public async Task Every_connection_the_data_source_opens_has_the_functions_including_one_taken_back_from_the_pool()
    {
        var path = Path.Combine(Path.GetTempPath(), "tracon-decimal-functions-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            var dataSource = new SqliteDataSource($"Data Source={path};Pooling=True");

            for (var round = 0; round < 3; round++)
            {
                await using var connection = await dataSource.OpenConnectionAsync();

                (await ScalarAsync(connection, "SELECT tracon_decimal_add('0.1', '0.2')")).ShouldBe("0.3", $"round {round}");
            }

            await using var first = await dataSource.OpenConnectionAsync();
            await using var second = await dataSource.OpenConnectionAsync();

            (await ScalarAsync(first, "SELECT tracon_decimal_sum(v) FROM (SELECT '1' AS v UNION ALL SELECT '2')")).ShouldBe("3");
            (await ScalarAsync(second, "SELECT tracon_decimal_sum(v) FROM (SELECT '1' AS v UNION ALL SELECT '2')")).ShouldBe("3");
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task The_functions_live_as_long_as_the_native_connection_so_a_pooled_open_has_nothing_to_register()
    {
        // A function the driver registers (CreateFunction) is taken off again when the connection closes, and the next open
        // has to pay for the registration again: about 1.9 KB on every open, which every store call makes. A function
        // registered on the native connection stays there while the pool keeps it. The proof is a connection the DRIVER opens
        // from the same pool, with the data source out of the way: it must find the functions already there.
        var path = Path.Combine(Path.GetTempPath(), "tracon-decimal-native-" + Guid.NewGuid().ToString("N") + ".db");
        var connectionString = $"Data Source={path};Pooling=True";

        try
        {
            var dataSource = new SqliteDataSource(connectionString);

            await using (await dataSource.OpenConnectionAsync())
            {
            }

            await using (var plain = new SqliteConnection(connectionString))
            {
                await plain.OpenAsync();

                (await ScalarAsync(plain, "SELECT tracon_decimal_add('0.1', '0.2')")).ShouldBe("0.3", "the pooled native connection kept the scalar");
                (await ScalarAsync(plain, "SELECT tracon_decimal_sum(v) FROM (SELECT '1' AS v UNION ALL SELECT '2')")).ShouldBe("3", "and the aggregate");
            }

            // A native connection the pool let go is a new one with nothing registered: the data source gives it the
            // functions again, whatever it knew about the one before.
            SqliteConnection.ClearAllPools();

            await using var again = await dataSource.OpenConnectionAsync();

            (await ScalarAsync(again, "SELECT tracon_decimal_add('0.1', '0.2')")).ShouldBe("0.3");
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*"))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task Two_sums_in_one_statement_and_one_sum_per_group_keep_their_own_totals()
    {
        // The aggregate keeps its running total per call of the function, not per function: a statement that names it twice,
        // or once per group, must not mix the totals.
        await using var connection = await OpenAsync();

        const string rows = "(SELECT 1 AS g, '0.1' AS a, '10' AS b UNION ALL SELECT 1, '0.2', '20' UNION ALL SELECT 2, '0.4', '40')";

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT tracon_decimal_sum(a), tracon_decimal_sum(b) FROM {rows}";

        await using (var reader = await command.ExecuteReaderAsync())
        {
            (await reader.ReadAsync()).ShouldBeTrue();
            (reader.GetString(0), reader.GetString(1)).ShouldBe(("0.7", "70"));
        }

        command.CommandText = $"SELECT g, tracon_decimal_sum(a), tracon_decimal_sum(b), (SELECT tracon_decimal_sum(x.a) FROM {rows} x WHERE x.g = t.g) FROM {rows} t GROUP BY g ORDER BY g";

        var groups = new List<(long Group, string A, string B, string Inner)>();

        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                groups.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        groups.ShouldBe([(1L, "0.3", "30", "0.3"), (2L, "0.4", "40", "0.4")]);
    }

    [Fact]
    public async Task A_sum_over_generated_costs_is_the_decimal_sum_of_the_costs()
    {
        // The oracle is System.Decimal addition done here term by term, not the function under test; what the
        // property pins is the wiring: the text round trip, the NULL rule and the grouping of several columns.
        await using var connection = await OpenAsync();
        var gen = Gen.Select(BoundaryValues.Cost, Gen.Bool, static (cost, isNull) => isNull ? (decimal?)null : cost).Array[1, 12];

        gen.Sample(
            values =>
            {
                var rows = string.Join(
                    " UNION ALL ",
                    values.Select(static value => value is { } cost
                        ? $"SELECT '{cost.ToString(CultureInfo.InvariantCulture)}' AS v"
                        : "SELECT NULL AS v"));

                var actual = ScalarAsync(connection, $"SELECT tracon_decimal_sum(v) FROM ({rows})").GetAwaiter().GetResult();

                if (values.All(static value => value is null))
                {
                    actual.ShouldBeNull();
                    return;
                }

                var expected = 0m;

                foreach (var value in values)
                {
                    expected += value ?? 0m;
                }

                decimal.Parse(actual!, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture).ShouldBe(expected);
            },
            iter: 300,
            threads: 1);
    }

    private static async Task<DbConnection> OpenAsync()
        => await new SqliteDataSource(Memory).OpenConnectionAsync();

    private static async Task<string?> ScalarAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) is { Length: > 0 } text
            ? text
            : null;
    }
}
