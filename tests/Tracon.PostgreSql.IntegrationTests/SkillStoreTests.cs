using Microsoft.Extensions.Options;
using Tracon.PostgreSql.IntegrationTests.Infrastructure;

namespace Tracon.PostgreSql.IntegrationTests;

/// <summary>Verifies the skill store's PostgreSQL-specific behavior.</summary>
public sealed class SkillStoreTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Resources_are_read_with_the_skill_and_cascade_on_delete()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var store = CreateStore(context);

        var saved = await store.SaveAsync(Skill("alpha", "invoice") with
        {
            Resources =
            [
                new AgentSkillResourceDefinition
                {
                    Name = "policy.md",
                    Description = "Invoice policy.",
                    Content = "Review every invoice.",
                },
            ],
        });

        var loaded = await store.GetAsync("alpha", "invoice");
        loaded.ShouldNotBeNull();
        loaded.Resources.Single().Content.ShouldBe("Review every invoice.");

        (await store.DeleteAsync("alpha", "invoice")).ShouldBeTrue();

        var count = await context.ScalarAsync<long>(
            $"SELECT count(*) FROM {context.SchemaName}.agent_skill_resources WHERE skill_id = '{saved.Id}';");
        count.ShouldBe(0);
    }

    [Fact]
    public async Task Same_skill_name_is_isolated_between_tenants()
    {
        await using var context = await PostgresTestContext.CreateAsync(fixture);
        var store = CreateStore(context);

        await store.SaveAsync(Skill("alpha", "invoice") with { Instructions = "Alpha instructions." });
        await store.SaveAsync(Skill("beta", "invoice") with { Instructions = "Beta instructions." });

        (await store.GetAsync("alpha", "invoice"))!.Instructions.ShouldBe("Alpha instructions.");
        (await store.GetAsync("beta", "invoice"))!.Instructions.ShouldBe("Beta instructions.");
        (await store.DeleteAsync("beta", "invoice")).ShouldBeTrue();
        (await store.GetAsync("alpha", "invoice")).ShouldNotBeNull();
    }

    private static SqlAgentSkillStore CreateStore(PostgresTestContext context)
        => new(context.StoreContext);

    private static AgentSkillDefinition Skill(string tenantId, string name)
        => new()
        {
            TenantId = tenantId,
            Name = name,
            Description = "Reviews invoices.",
            Instructions = "Review invoices.",
        };
}
