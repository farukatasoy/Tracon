using AgentPrism.PostgreSql.IntegrationTests.Infrastructure;

namespace AgentPrism.PostgreSql.IntegrationTests;

/// <summary>
/// Proof that concurrent <c>PUT</c>s against the same tenant provider binding
/// (phase 65, BYOK) never corrupt the row or leave more than one behind.
/// </summary>
/// <remarks>
/// Two real <see cref="AgentPrism.SqlTenantProviderBindingStore"/> instances
/// connect to the same database and race to upsert the same
/// <c>(tenant_id, provider_name)</c> key. The guarantee comes from
/// PostgreSQL's native <c>ON CONFLICT ... DO UPDATE</c>, not from
/// application-level locking; only a real database under real concurrency
/// proves it. Precedent: <see cref="JobStoreConcurrencyTests"/>.
/// </remarks>
public sealed class TenantProviderBindingConcurrencyTests(PostgresFixture fixture)
{
    private const int WriterCount = 20;

    [Fact]
    public async Task Racing_upserts_to_the_same_binding_leave_exactly_one_consistent_row()
    {
        var schemaName = PostgresTestContext.NewSchemaName();

        await using var seed = PostgresTestContext.Create(fixture, schemaName);
        await seed.Migrations.ApplyAsync();

        var writerNames = Enumerable.Range(0, WriterCount)
            .Select(i => $"AgentPrism:ProviderKeys:Acme:OpenAI-{i}")
            .ToList();

        await Task.WhenAll(writerNames.Select(async name =>
        {
            await using var worker = PostgresTestContext.Create(fixture, schemaName);

            await worker.TenantProviderBindings.UpsertAsync(new TenantProviderBinding
            {
                TenantId = "acme",
                ProviderName = "openai",
                ApiKeyConfigurationName = name,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }));

        var all = await seed.TenantProviderBindings.ListAsync("acme");

        // Exactly one row for the (tenant, provider) key -- no duplicate
        // inserted by a lost ON CONFLICT race -- and its value is one of the
        // ones actually written, not a corrupted hybrid.
        all.Count.ShouldBe(1);
        writerNames.ShouldContain(all[0].ApiKeyConfigurationName, StringComparer.Ordinal);
    }
}
