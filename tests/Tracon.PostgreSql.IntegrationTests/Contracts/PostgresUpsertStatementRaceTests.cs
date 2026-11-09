using Npgsql;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests.Contracts;

/// <summary>
/// The webhook subscription upsert statement on its own, sent once, never fails when writers save the same new record.
/// </summary>
/// <remarks>
/// <para>
/// The store sends an upsert a second time when its primary key lost a race, so the store-level contract passes whether
/// the statement is safe or only usually lucky the second time: it failed on CI about once in twelve hundred writers,
/// with the second attempt in place. This class sends the statement once, with no second attempt, so the statement itself
/// has to be free of the race: <c>ON CONFLICT</c> names one index, and a statement that passes that index's check before
/// another writer has written to it fails later on the primary key.
/// </para>
/// <para>
/// The identifier and the name are the same in every writer of a round, as in the store-level contract, and the rounds
/// are many because the window is narrow.
/// </para>
/// </remarks>
/// <param name="schema">The schema shared by the tests of this class.</param>
public sealed class PostgresUpsertStatementRaceTests(PostgresSchemaFixture schema)
    : IClassFixture<PostgresSchemaFixture>
{
    private const string Tenant = "statement-race";
    private const int Rounds = 150;
    private const int Writers = 8;

    private static readonly DateTimeOffset Created = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task One_send_of_the_statement_from_writers_of_one_new_record_never_fails()
    {
        await schema.ResetAsync();

        var context = schema.Context.StoreContext;
        var sql = context.Sql.UpsertWebhookSubscription;
        var failures = new List<string>();

        for (var round = 0; round < Rounds; round++)
        {
            var id = Guid.NewGuid();
            var name = "statement-" + round;
            var gate = new TaskCompletionSource();

            var writers = Enumerable.Range(0, Writers)
                .Select(index => Task.Run(
                    async () =>
                    {
                        await gate.Task;

                        try
                        {
                            await using var command = context.CreateCommand(sql);

                            Bind(context, command, id, name, "https://example.com/" + index);

                            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
                        }
                        catch (PostgresException exception)
                        {
                            lock (failures)
                            {
                                failures.Add($"round {round}, writer {index}: {exception.SqlState} {exception.ConstraintName}");
                            }
                        }
                    }))
                .ToList();

            gate.SetResult();

            await Task.WhenAll(writers);
        }

        failures.ShouldBeEmpty(
            $"{failures.Count} of {Rounds * Writers} sends failed although each saved a record whose name no row held");

        var rows = await schema.Context.Webhooks.ListSubscriptionsAsync(Tenant, TestContext.Current.CancellationToken);

        rows.Count.ShouldBe(Rounds);
    }

    private static void Bind(SqlStoreContext context, System.Data.Common.DbCommand command, Guid id, string name, string url)
    {
        var dialect = context.Dialect;

        DbHelpers.Add(command, "id", id);
        DbHelpers.AddTenant(command, Tenant);
        DbHelpers.Add(command, "name", name);
        DbHelpers.Add(command, "url", url);
        dialect.AddTextArray(command, "events", ["run.completed"]);
        dialect.AddText(command, "secret_configuration_key", null);
        dialect.AddJsonb(command, "headers", "{}");
        dialect.AddJsonb(command, "header_configuration_keys", "{}");
        DbHelpers.Add(command, "enabled", true);
        DbHelpers.Add(command, "consecutive_failures", 0);
        dialect.AddTimestamp(command, "created_at", Created);
        dialect.AddTimestamp(command, "updated_at", Created);
    }
}
