using System.Text.Json;

namespace AgentPrism.StoreContracts;

/// <summary>
/// Behavior tests for the <see cref="IToolApprovalRuleStore"/> contract.
/// </summary>
/// <remarks>
/// Added in phase 41. A persisted approval rule is a <strong>security
/// record</strong>: one tenant's rule must never let another tenant's tool
/// call through without approval.
/// </remarks>
public abstract class ToolApprovalRuleStoreContract : TenantIsolationContract<IToolApprovalRuleStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
        => (await Store.AddAsync(Rule(tenantId, name))).Id;

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => (await Store.ListAsync(tenantId)).Any(rule => rule.Id == (Guid)key);

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (Guid)key);

    [Fact]
    public async Task Added_rule_is_read_back()
    {
        var added = await Store.AddAsync(Rule("tenant-a", "get_order"));

        added.Id.ShouldNotBe(Guid.Empty);

        var loaded = (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();

        loaded.ToolName.ShouldBe("get_order");
        loaded.AgentName.ShouldBe("support");
        loaded.CreatedBy.ShouldBe("operator@example");
    }

    [Fact]
    public async Task Same_scope_added_twice_leaves_a_single_record()
    {
        // agent_name and arguments_hash can be NULL; if NULL semantics for
        // uniqueness are not set up correctly, duplicate rows accumulate.
        await Store.AddAsync(Rule("tenant-a", "get_order") with { AgentName = null, ArgumentsHash = null });
        await Store.AddAsync(Rule("tenant-a", "get_order") with { AgentName = null, ArgumentsHash = null });

        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Different_agent_becomes_a_separate_rule()
    {
        await Store.AddAsync(Rule("tenant-a", "get_order") with { AgentName = null });
        await Store.AddAsync(Rule("tenant-a", "get_order") with { AgentName = "support" });

        (await Store.ListAsync("tenant-a")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Deleting_a_nonexistent_rule_returns_false()
        => (await Store.DeleteAsync("tenant-a", Guid.NewGuid())).ShouldBeFalse();

    [Fact]
    public async Task Argument_conditions_round_trip()
    {
        var conditions = new[]
        {
            new ToolArgumentCondition
            {
                Path = "amount",
                Operator = ToolArgumentOperator.LessThanOrEqual,
                Value = JsonSerializer.SerializeToElement(100),
            },
        };

        await Store.AddAsync(Rule("tenant-a", "refund_order") with { ArgumentConditions = conditions });

        var loaded = (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();

        var condition = loaded.ArgumentConditions.ShouldHaveSingleItem();
        condition.Path.ShouldBe("amount");
        condition.Operator.ShouldBe(ToolArgumentOperator.LessThanOrEqual);
        condition.Value.GetDouble().ShouldBe(100);
    }

    [Fact]
    public async Task Condition_scoped_rule_added_twice_leaves_a_single_record()
    {
        // conditions_hash widens the uniqueness key the same way arguments_hash
        // does: without it, a second identical condition-based rule would
        // accumulate endlessly (0004_skill_scripts.sql's COALESCE lesson).
        var conditions = new[]
        {
            new ToolArgumentCondition
            {
                Path = "amount",
                Operator = ToolArgumentOperator.LessThanOrEqual,
                Value = JsonSerializer.SerializeToElement(100),
            },
        };

        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = conditions });
        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = conditions });

        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Different_conditions_become_a_separate_rule()
    {
        var lowThreshold = new[]
        {
            new ToolArgumentCondition
            {
                Path = "amount",
                Operator = ToolArgumentOperator.LessThanOrEqual,
                Value = JsonSerializer.SerializeToElement(100),
            },
        };
        var highThreshold = new[]
        {
            new ToolArgumentCondition
            {
                Path = "amount",
                Operator = ToolArgumentOperator.LessThanOrEqual,
                Value = JsonSerializer.SerializeToElement(500),
            },
        };

        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = lowThreshold });
        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = highThreshold });

        (await Store.ListAsync("tenant-a")).Count.ShouldBe(2);
    }

    private static ToolApprovalRule Rule(string tenantId, string toolName)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AgentName = "support",
            ToolName = toolName,
            CreatedBy = "operator@example",
            CreatedAt = new DateTimeOffset(2026, 8, 7, 9, 0, 0, TimeSpan.Zero),
        };
}

/// <summary>
/// Behavior tests for the <see cref="IMcpServerStore"/> contract.
/// </summary>
/// <remarks>
/// Added in phase 41. An MCP server registration accepts a tool definition
/// from an external source; one tenant's server showing up in another
/// tenant's catalog is a security boundary. The record <strong>never carries
/// a secret</strong> (K-059) -- it only stores the name of the configuration
/// key the value will be read from.
/// </remarks>
public abstract class McpServerStoreContract : TenantIsolationContract<IMcpServerStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.SaveAsync(Server(tenantId, name));
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId)
        => (await Store.ListAsync(tenantId)).Count;

    /// <inheritdoc />
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.DeleteAsync(tenantId, (string)key);

    [Fact]
    public async Task Saved_server_is_read_back()
    {
        await Store.SaveAsync(Server("tenant-a", "github"));

        var loaded = await Store.GetAsync("tenant-a", "github");

        loaded.ShouldNotBeNull();
        loaded.Endpoint.ToString().ShouldBe("https://example.test/mcp");
        loaded.Enabled.ShouldBeTrue();
        loaded.AuthorizationConfigurationKey.ShouldBe("AgentPrism:McpSecrets:GithubToken");
    }

    [Fact]
    public async Task Saving_the_same_name_a_second_time_overwrites_it()
    {
        await Store.SaveAsync(Server("tenant-a", "github"));
        await Store.SaveAsync(Server("tenant-a", "github") with { Description = "updated" });

        (await Store.GetAsync("tenant-a", "github"))!.Description.ShouldBe("updated");
        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Nonexistent_server_returns_null()
        => (await Store.GetAsync("tenant-a", "missing")).ShouldBeNull();

    [Fact]
    public async Task Deleting_a_nonexistent_record_returns_false()
        => (await Store.DeleteAsync("tenant-a", "missing")).ShouldBeFalse();

    private static McpServerDefinition Server(string tenantId, string name)
        => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = "Sample server.",
            Endpoint = new Uri("https://example.test/mcp"),
            AuthorizationConfigurationKey = "AgentPrism:McpSecrets:GithubToken",
            Enabled = true,
        };
}
