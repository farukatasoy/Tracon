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
