using System.Globalization;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>
/// <c>audit_log.key_id</c> arrives with migration 0067. A chain written before it carries no key, and
/// the walk must still call it valid, count it, and read every column of the row from the right place.
/// </summary>
/// <remarks>
/// The rows are written under the PRE-upgrade schema (every migration before this one) with the plain SHA-256
/// hashes of the code that wrote them, and read back through the real log after the upgrade. The reader names
/// <c>key_id</c> by ordinal (the eleventh column of both selects), so a slip in the column order here would show
/// as a value in the wrong field and not as an error.
/// </remarks>
public sealed class AuditChainKeyMigrationTests(PostgresFixture fixture)
{
    private const int MigrationId = 67;

    private const string Tenant = "default";

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(static i => (byte)i).ToArray();

    [Fact]
    public async Task A_chain_written_before_the_column_existed_verifies_counts_and_reads_back_in_the_right_fields()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        await WriteLegacyChainAsync(context, schemaName, 3);

        (await context.Migrations.ApplyAsync()).ShouldBeGreaterThan(0);

        (await context.ScalarAsync<long>(
            "SELECT COUNT(*) FROM information_schema.columns " +
            $"WHERE table_schema = '{schemaName}' AND table_name = 'audit_log' AND column_name = 'key_id';"))
            .ShouldBe(1);

        var result = await context.AuditLog.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(3);
        result.UnkeyedEntries.ShouldBe(3);
        result.FirstFailingEntryId.ShouldBeNull();

        var entries = await context.AuditLog.QueryAsync(new AuditQuery { TenantId = Tenant });

        entries.Count.ShouldBe(3);
        entries.ShouldAllBe(static entry => entry.KeyId == null && entry.Hash != null);
        entries.Select(static entry => entry.Entity).OrderBy(static entity => entity, StringComparer.Ordinal)
            .ShouldBe(["agent:0", "agent:1", "agent:2"]);
        entries.ShouldAllBe(static entry => string.Equals(entry.Actor, "user-1", StringComparison.Ordinal)
            && string.Equals(entry.Action, "agent.update", StringComparison.Ordinal));
        entries.Single(static entry => string.Equals(entry.Entity, "agent:1", StringComparison.Ordinal)).After.ShouldBe("{\"v\":1}");
    }

    [Fact]
    public async Task A_signed_entry_written_after_the_upgrade_continues_the_legacy_chain()
    {
        var schemaName = PostgresTestContext.NewSchemaName();
        await using var context = PostgresTestContext.Create(fixture, schemaName, enableKnowledge: false);

        await ApplyThroughAsync(context, schemaName, MigrationId - 1);
        await WriteLegacyChainAsync(context, schemaName, 2);
        await context.Migrations.ApplyAsync();

        var signed = new SqlAuditLog(
            context.StoreContext,
            context.TenantContext,
            AuditChainKeyRing.Create("2026-10", new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["2026-10"] = Key }));

        await signed.WriteAsync(new AuditEntry
        {
            Id = TraconId.NewId(),
            TenantId = Tenant,
            Actor = "user-1",
            Action = "agent.update",
            Entity = "agent:after-upgrade",
            After = "{\"v\":9}",
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var result = await signed.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant });

        result.Status.ShouldBe(AuditChainStatus.Valid);
        result.EntriesChecked.ShouldBe(3);
        result.UnkeyedEntries.ShouldBe(2);

        // A host that never had the key cannot check the new row and says so.
        (await context.AuditLog.VerifyChainAsync(new AuditChainQuery { TenantId = Tenant }))
            .Status.ShouldBe(AuditChainStatus.UnknownKey);
    }

    /// <summary>Writes the rows the code of would have: a plain SHA-256 linked to the row before.</summary>
    private static async Task WriteLegacyChainAsync(PostgresTestContext context, string schemaName, int count)
    {
        string? previous = null;

        for (var i = 0; i < count; i++)
        {
            var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, i + 1, TimeSpan.Zero);
            var after = $"{{\"v\":{i}}}";
            var hash = AuditChainHasher.ComputeHash(previous, Tenant, "user-1", "agent.update", $"agent:{i}", null, after, createdAt);
            var stamp = createdAt.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);

            await context.ExecuteAsync(
                $"INSERT INTO {schemaName}.audit_log (id, tenant_id, actor, action, entity, before, after, created_at, prev_hash, hash) " +
                $"VALUES ('{Guid.NewGuid()}', '{Tenant}', 'user-1', 'agent.update', 'agent:{i}', NULL, '{after}', " +
                $"'{stamp}', {(previous is null ? "NULL" : $"'{previous}'")}, '{hash}');");

            previous = hash;
        }
    }

    private static async Task ApplyThroughAsync(PostgresTestContext context, string schemaName, int lastId)
    {
        var migrations = MigrationDescriptor
            .Discover(typeof(MigrationRunner).Assembly, "Tracon.PostgreSql.Migrations.")
            .Where(migration => migration.Id <= lastId)
            .OrderBy(migration => migration.Id)
            .ToArray();

        migrations.ShouldNotBeEmpty("the pre-upgrade migration set has to exist");

        await context.ExecuteAsync($"CREATE SCHEMA {schemaName};");
        await context.ExecuteAsync($"""
            CREATE TABLE {schemaName}.__migrations (
                id         integer     NOT NULL,
                set_name   text        NOT NULL,
                name       text        NOT NULL,
                checksum   text        NOT NULL,
                applied_at timestamptz NOT NULL,
                PRIMARY KEY (set_name, id)
            );
            """);

        foreach (var migration in migrations)
        {
            await context.ExecuteAsync(context.StoreContext.Sql.ApplySchema(migration.Sql));
            await context.ExecuteAsync($"""
                INSERT INTO {schemaName}.__migrations (id, set_name, name, checksum, applied_at)
                VALUES ({migration.Id}, 'core', '{migration.Name}', '{migration.Checksum}', now());
                """);
        }
    }
}
