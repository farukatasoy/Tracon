using System.Globalization;
using Tracon.Sqlite.IntegrationTests.Infrastructure;

namespace Tracon.Sqlite.IntegrationTests;

/// <summary>
/// Seven columns of <c>runs</c> were declared <c>NUMERIC</c> and the migration after migration 0057 gives them <c>TEXT</c>:
/// <c>cached_input_cost</c>, <c>non_token_cost</c> and the five unit prices. The stored value is carried over, and the
/// quota counters the old float addition left a tail on are put right.
/// </summary>
/// <remarks>
/// Every test writes under the schema of every migration before the new one, with plain SQL (the driver writes a decimal as
/// text and the NUMERIC affinity then turns it into an INTEGER or a REAL, so the SQL does the same), runs the real
/// <see cref="MigrationRunner"/> over the rest, and reads the result both raw and through the store.
/// </remarks>
public sealed class DecimalColumnsMigrationTests(SqliteFixture fixture)
{
    private const string Tenant = "default";

    private const string Seeded = "2026-01-01T00:00:00.0000000Z";

    private static readonly string[] Columns =
    [
        "cached_input_cost",
        "non_token_cost",
        "input_price_per_mtok",
        "output_price_per_mtok",
        "cached_input_price_per_mtok",
        "cache_write_price_per_mtok",
        "cache_write_long_price_per_mtok",
    ];

    /// <summary>
    /// What the driver wrote and what a run must read as afterwards. SQLite keeps the first 15 significant digits of a
    /// number written into a NUMERIC column and the migration gives back those 15.
    /// </summary>
    public static TheoryData<string, string> Matrix { get; } = new()
    {
        { "3", "3" },
        { "3.00", "3" },
        { "0.5", "0.5" },
        { "0.0008977361", "0.0008977361" },
        { "999999999.999999", "999999999.999999" },
        { "0.00001234", "0.00001234" },
        { "0.0000000001", "0.0000000001" },
        { "0.1250000000", "0.125" },
        { "1e3", "1000" },
        { "-0.5", "-0.5" },
        { "-0.00001234", "-0.00001234" },
        { "123456789012345678", "123456789012345678" },
        { "100000000000000000000", "100000000000000000000" },
        { "1234567890.1234567890", "1234567890.12346" },
        { "0.1234567890123456789", "0.123456789012346" },
        { "0.30000000000000004", "0.3" },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task A_value_written_before_the_upgrade_is_carried_over_in_every_one_of_the_seven_columns(string written, string expected)
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        var rows = new List<(Guid Id, string Column)>();

        foreach (var column in Columns)
        {
            var id = Guid.NewGuid();
            await InsertAsync(context, prefix, id, $"{column} = '{written}'");
            rows.Add((id, column));
        }

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var (id, column) in rows)
        {
            var text = await context.ScalarAsync<string>($"SELECT {column} FROM {prefix}runs WHERE id = '{Id(id)}';");
            var type = await context.ScalarAsync<string>($"SELECT typeof({column}) FROM {prefix}runs WHERE id = '{Id(id)}';");

            type.ShouldBe("text", column);
            text.ShouldNotBeNull().ToUpperInvariant().ShouldNotContain("E", Case.Sensitive, $"{column} holds an exponent: {text}");
            Parse(text).ShouldBe(Parse(expected), $"{column} written as {written} reads {text}");
        }
    }

    [Fact]
    public async Task The_stored_values_read_back_through_the_store_as_the_decimals_that_were_written()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        var run = Guid.NewGuid();
        var silent = Guid.NewGuid();

        await InsertAsync(
            context,
            prefix,
            run,
            "input_cost = '1.0', cost_currency = 'USD', pricing_source = 1, " +
            "cached_input_cost = '999999999.999999', input_price_per_mtok = '0.0000123', output_price_per_mtok = '15', " +
            "cached_input_price_per_mtok = '0.075', cache_write_price_per_mtok = '3.75', cache_write_long_price_per_mtok = '6', " +
            "non_token_cost = '0.0020000000', non_token_currency = 'USD', non_token_estimated = 0, non_token_unsummed = 0");
        await InsertAsync(context, prefix, silent, null);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}runs WHERE id = '{Id(run)}' AND tenant_id = '{Tenant}';")).ShouldBe(1);

        var read = (await context.Runs.GetRunAsync(run)).ShouldNotBeNull("the priced run");
        var cost = read.Cost.ShouldNotBeNull("its cost");

        cost.CachedInputCost.ShouldBe(999999999.999999m);
        cost.InputPricePerMillionTokens.ShouldBe(0.0000123m);
        cost.OutputPricePerMillionTokens.ShouldBe(15m);
        cost.CachedInputPricePerMillionTokens.ShouldBe(0.075m);
        cost.CacheWritePricePerMillionTokens.ShouldBe(3.75m);
        cost.CacheWriteLongPricePerMillionTokens.ShouldBe(6m);
        read.NonTokenCost.ShouldNotBeNull("its non-token cost").Amount.ShouldBe(0.002m);

        var unpriced = (await context.Runs.GetRunAsync(silent)).ShouldNotBeNull("the unpriced run");
        unpriced.Cost.ShouldBeNull("a run with no price reads back with no cost, whatever the unit columns hold");
        unpriced.NonTokenCost.ShouldBeNull();

        // A value written after the upgrade is exact too: the column keeps what the driver writes.
        var fresh = Guid.NewGuid();
        await context.Runs.StartRunAsync(new RunStartInfo { RunId = fresh, AgentName = "agent", StartedAt = DateTimeOffset.UtcNow, TenantId = Tenant });
        await context.Runs.CompleteRunAsync(new RunCompletion
        {
            RunId = fresh,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            TenantId = Tenant,
            Cost = new RunCost { CachedInputCost = 999999999.999999m, Currency = "USD", Source = PricingSource.Catalog },
        });

        (await context.Runs.GetRunAsync(fresh))!.Cost!.CachedInputCost.ShouldBe(999999999.999999m);
    }

    [Fact]
    public async Task The_seven_columns_are_text_and_nothing_else_of_the_table_moves()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        var parent = Guid.NewGuid();
        await InsertAsync(context, prefix, parent, "cached_input_cost = '0.5'");
        await context.ExecuteAsync(
            $"INSERT INTO {prefix}run_events (run_id, seq, type, created_at) VALUES ('{Id(parent)}', 1, 0, '{Seeded}');");

        var before = await SnapshotAsync(context, prefix);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var after = await SnapshotAsync(context, prefix);

        foreach (var column in Columns)
        {
            after.Types[column].ShouldBe("TEXT", column);
        }

        after.Types.Keys.Where(static name => name.EndsWith("_text", StringComparison.Ordinal)).ShouldBeEmpty("no scratch column is left behind");
        after.Types.Keys.Order(StringComparer.Ordinal).ShouldBe(before.Types.Keys.Order(StringComparer.Ordinal), "same column set");

        foreach (var (name, type) in before.Types.Where(pair => !Columns.Contains(pair.Key, StringComparer.Ordinal)))
        {
            after.Types[name].ShouldBe(type, $"the declared type of {name} did not change");
        }

        after.Indexes.ShouldBe(before.Indexes, "no index of the table is lost or renamed");
        after.RowCount.ShouldBe(before.RowCount);
        after.EventCount.ShouldBe(1, "ON DELETE CASCADE did not run: the table was not rebuilt");
        after.Violations.ShouldBe(0L, "no foreign key is broken");
    }

    [Fact]
    public async Task A_row_with_nothing_priced_keeps_null_in_all_seven_columns()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        var id = Guid.NewGuid();
        await InsertAsync(context, prefix, id, null);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        foreach (var column in Columns)
        {
            (await context.ScalarAsync<string>($"SELECT typeof({column}) FROM {prefix}runs WHERE id = '{Id(id)}';")).ShouldBe("null", column);
        }
    }

    [Fact]
    public async Task A_quota_counter_the_old_addition_left_a_float_tail_on_is_put_right()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        var counters = new (string Agent, string Written, string Expected)[]
        {
            ("tail", "0.30000000000000004", "0.3"),
            ("below", "0.7999999999999999", "0.8"),
            ("exponent", "3.0e-05", "0.00003"),
            ("clean", "12345.678", "12345.678"),
            ("zero", "0", "0"),
            ("tenth", "0.1", "0.1"),
            ("ten-decimals", "0.0000000001", "0.0000000001"),
        };

        foreach (var (agent, written, _) in counters)
        {
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}quota_usage (tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at) " +
                $"VALUES ('{Tenant}', '{agent}', 1, '2026-11-04', 1, 1, '{written}', '{Seeded}');");
        }

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        var usage = await context.Quotas.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant });

        foreach (var (agent, _, expected) in counters)
        {
            usage.Single(record => string.Equals(record.AgentName, agent, StringComparison.Ordinal)).Cost.ShouldBe(Parse(expected), agent);
        }
    }

    [Fact]
    public async Task A_run_started_after_the_upgrade_adds_to_a_converted_counter_exactly()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await context.ExecuteAsync(
            $"INSERT INTO {prefix}quota_usage (tenant_id, agent_name, period, period_start, runs, tokens, cost, updated_at) " +
            $"VALUES ('{Tenant}', 'a', 1, '2026-11-04', 1, 1, '0.30000000000000004', '{Seeded}');");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        await context.Quotas.AddUsageAsync(
            new QuotaConsumption { TenantId = Tenant, AgentName = "a", Runs = 1, Tokens = 1, Cost = 0.5m, OccurredAt = DateTimeOffset.UtcNow },
            new Dictionary<QuotaPeriod, DateOnly> { [QuotaPeriod.Monthly] = new DateOnly(2026, 11, 4) });

        var usage = await context.Quotas.GetUsageAsync(new QuotaUsageQuery { TenantId = Tenant, AgentName = "a" });

        usage.Single().Cost.ShouldBe(0.8m);
    }

    [Fact]
    public async Task A_view_of_the_host_over_one_of_the_columns_stops_the_migration_and_leaves_the_database_as_it_was()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);

        var id = Guid.NewGuid();
        await InsertAsync(context, prefix, id, "cached_input_cost = '0.5'");
        await context.ExecuteAsync($"CREATE VIEW {prefix}host_costs AS SELECT id, cached_input_cost FROM {prefix}runs;");

        var failure = await Should.ThrowAsync<TraconException>(async () => await context.Migrations.ApplyAsync());

        failure.Message.ShouldContain("host_costs");

        (await context.ScalarAsync<string>(
            $"SELECT type FROM pragma_table_info('{prefix}runs') WHERE name = 'cached_input_cost';")).ShouldBe("NUMERIC");
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM pragma_table_info('{prefix}runs') WHERE name LIKE '%_text';")).ShouldBe(0);
        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}__migrations WHERE id = {MigrationId};")).ShouldBe(0);
        (await context.ScalarAsync<string>($"SELECT CAST(cached_input_cost AS TEXT) FROM {prefix}runs WHERE id = '{Id(id)}';")).ShouldBe("0.5");
    }

    [Fact]
    public async Task The_read_view_is_rebuilt_over_the_new_columns_and_keeps_its_number_types()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix, Tenant, enableReadViews: true);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await ApplyViewsThroughAsync(context, prefix, lastId: 2);

        var id = Guid.NewGuid();
        await InsertAsync(context, prefix, id, "cached_input_cost = '0.5', input_price_per_mtok = '3', output_price_per_mtok = '15.5'");

        (await context.ScalarAsync<string>($"SELECT typeof(cached_input_cost) FROM {prefix}runs_v1;")).ShouldBe("real");

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'view' AND name = '{prefix}runs_v1';")).ShouldBe(1);

        // The published contract: the columns that were numbers stay numbers, and total_cost is the same real sum.
        (await context.ScalarAsync<string>($"SELECT typeof(cached_input_cost) FROM {prefix}runs_v1 WHERE run_id = '{Id(id)}';")).ShouldBe("real");
        (await context.ScalarAsync<string>($"SELECT typeof(input_price_per_mtok) FROM {prefix}runs_v1 WHERE run_id = '{Id(id)}';")).ShouldBe("real");
        (await context.ScalarAsync<double>($"SELECT cached_input_cost FROM {prefix}runs_v1 WHERE run_id = '{Id(id)}';")).ShouldBe(0.5);
        (await context.ScalarAsync<double>($"SELECT total_cost FROM {prefix}runs_v1 WHERE run_id = '{Id(id)}';")).ShouldBe(0.5);
    }

    [Fact]
    public async Task A_view_left_behind_by_a_host_that_switched_the_views_off_is_dropped_by_the_migration()
    {
        var prefix = SqliteTestContext.NewTablePrefix();
        await using var context = SqliteTestContext.Create(fixture, prefix);

        await ApplyThroughAsync(context, prefix, MigrationId - 1);
        await ApplyViewsThroughAsync(context, prefix, lastId: 2);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<long>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'view' AND name = '{prefix}runs_v1';")).ShouldBe(0);
    }

    private static int MigrationId => MigrationDescriptor
        .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.")
        .Single(static migration => migration.Name.Contains("decimal_columns_as_text", StringComparison.Ordinal))
        .Id;

    private static decimal Parse(string text) => decimal.Parse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

    /// <summary>An identifier in the shape a non-nullable Guid parameter takes.</summary>
    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture).ToUpperInvariant();

    /// <summary>
    /// A run row in the shape the schema before the migration takes. The extra assignments are <c>column = 'literal'</c>
    /// pairs: the literal is TEXT, so the NUMERIC affinity of the old column does to it what it did to the driver's text.
    /// </summary>
    private static async Task InsertAsync(SqliteTestContext context, string prefix, Guid runId, string? assignments)
    {
        var columns = new List<string> { "id", "tenant_id", "agent_name", "status", "started_at", "is_streaming", "depth" };
        var values = new List<string> { $"'{Id(runId)}'", $"'{Tenant}'", "'legacy-agent'", "1", $"'{Seeded}'", "0", "0" };

        foreach (var pair in (assignments ?? string.Empty).Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            var split = pair.Split(" = ", 2);
            columns.Add(split[0]);
            values.Add(split[1]);
        }

        await context.ExecuteAsync($"INSERT INTO {prefix}runs ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)});");
    }

    private static async Task<TableSnapshot> SnapshotAsync(SqliteTestContext context, string prefix)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var command = context.DataSource.CreateCommand($"SELECT name, type FROM pragma_table_info('{prefix}runs');"))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                types[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var indexes = new List<string>();

        await using (var command = context.DataSource.CreateCommand(
            $"SELECT name || ':' || COALESCE(sql, '') FROM sqlite_master WHERE type = 'index' AND tbl_name = '{prefix}runs' ORDER BY name;"))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                indexes.Add(reader.GetString(0));
            }
        }

        return new TableSnapshot(
            types,
            indexes,
            await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}runs;"),
            await context.ScalarAsync<long>($"SELECT COUNT(*) FROM {prefix}run_events;"),
            await context.ScalarAsync<long>("SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    private static async Task ApplyThroughAsync(SqliteTestContext context, string prefix, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty();

        await context.ExecuteAsync(context.StoreContext.Sql.CreateMigrationsTable);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('core', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '{Seeded}');");
        }
    }

    private static async Task ApplyViewsThroughAsync(SqliteTestContext context, string prefix, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.Sqlite.MigrationsViews.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty();

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync(
                $"INSERT INTO {prefix}__migrations (set_name, id, name, checksum, applied_at) " +
                $"VALUES ('views', {migration.Id}, '{migration.Name}', '{migration.Checksum}', '{Seeded}');");
        }
    }

    private sealed record TableSnapshot(
        Dictionary<string, string> Types,
        List<string> Indexes,
        long RowCount,
        long EventCount,
        long Violations);
}
