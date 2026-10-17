namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IToolApprovalRuleStore"/> contract.
/// </summary>
/// <remarks>
/// A persisted approval rule is a <strong>security
/// record</strong>: one tenant's rule must never let another tenant's tool
/// call through without approval.
/// </remarks>
public abstract class ToolApprovalRuleStoreContract : TenantIsolationContract<IToolApprovalRuleStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
        => (await Store.AddAsync(Rule(tenantId, name), cancellationToken)).Id;

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => (await Store.ListAsync(tenantId)).Any(rule => rule.Id == (Guid)key);

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

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
                Value = TestData.State("100"),
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
                Value = TestData.State("100"),
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
                Value = TestData.State("100"),
            },
        };
        var highThreshold = new[]
        {
            new ToolArgumentCondition
            {
                Path = "amount",
                Operator = ToolArgumentOperator.LessThanOrEqual,
                Value = TestData.State("500"),
            },
        };

        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = lowThreshold });
        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = highThreshold });

        (await Store.ListAsync("tenant-a")).Count.ShouldBe(2);
    }

    /// <summary>
    /// Two different condition sets stay two rules even when a path carries the
    /// characters a store might use to join the fields it compares.
    /// </summary>
    /// <remarks>
    /// A path is free text - a JSON property name may hold any character. A
    /// store that deduplicates on a joined text of path, operator, and value
    /// must not let one set's path read as another set's field boundary.
    /// Otherwise the second rule is merged into the first: the store keeps the
    /// first rule and hands it back as if the second had been saved.
    /// </remarks>
    [Fact]
    public async Task A_path_carrying_separator_characters_does_not_merge_two_condition_sets()
    {
        var single = new[]
        {
            new ToolArgumentCondition
            {
                Path = "amount\u001F0\u001F1\u001Ecurrency",
                Operator = ToolArgumentOperator.Equals,
                Value = TestData.State("2"),
            },
        };
        var pair = new[]
        {
            new ToolArgumentCondition { Path = "amount", Operator = ToolArgumentOperator.Equals, Value = TestData.State("1") },
            new ToolArgumentCondition { Path = "currency", Operator = ToolArgumentOperator.Equals, Value = TestData.State("2") },
        };

        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = single });
        var second = await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = pair });

        second.ArgumentConditions.Count.ShouldBe(2);
        (await Store.ListAsync("tenant-a")).Count.ShouldBe(2);
    }

    /// <summary>
    /// A list value written with different whitespace is the same condition, so
    /// the rule is not stored twice.
    /// </summary>
    [Fact]
    public async Task A_list_value_that_differs_only_in_whitespace_is_the_same_condition_set()
    {
        static ToolArgumentCondition[] Allowed(string json) =>
        [
            new ToolArgumentCondition { Path = "region", Operator = ToolArgumentOperator.In, Value = TestData.State(json) },
        ];

        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = Allowed("[\"eu\", \"us\"]") });
        await Store.AddAsync(Rule("tenant-a", "refund_order") with { AgentName = null, ArgumentConditions = Allowed("[\"eu\",\"us\"]") });

        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();
    }

    /// <summary>
    /// The source fingerprint of a rule is stored and returned as given, both on
    /// the record the add returns and on a later read.
    /// </summary>
    /// <remarks>
    /// The rule only approves a tool of the source it names. A store that drops
    /// the value hands back a rule without a source, which approves no remote
    /// tool, so every remembered approval for one would ask again forever.
    /// </remarks>
    [Fact]
    public async Task Source_fingerprint_round_trips()
    {
        var fingerprint = new string('A', 64);

        var added = await Store.AddAsync(Rule("tenant-a", "crm_lookup") with { SourceFingerprint = fingerprint });

        added.SourceFingerprint.ShouldBe(fingerprint);
        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem().SourceFingerprint.ShouldBe(fingerprint);
    }

    /// <summary>
    /// Two rules that differ only in their source fingerprint are two rules.
    /// </summary>
    /// <remarks>
    /// A server that moved to another endpoint keeps its tool names. If the
    /// fingerprint were not part of the identity, a rule for the new endpoint
    /// would come back as the old rule, which names the old endpoint and never
    /// approves the new one.
    /// </remarks>
    [Fact]
    public async Task Different_source_fingerprint_becomes_a_separate_rule()
    {
        await Store.AddAsync(Rule("tenant-a", "crm_lookup") with { AgentName = null, SourceFingerprint = new string('A', 64) });
        var second = await Store.AddAsync(Rule("tenant-a", "crm_lookup") with { AgentName = null, SourceFingerprint = new string('B', 64) });

        second.SourceFingerprint.ShouldBe(new string('B', 64));
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
/// An MCP server registration accepts a tool definition
/// from an external source; one tenant's server showing up in another
/// tenant's catalog is a security boundary. The record <strong>never carries
/// a secret</strong> -- it only stores the name of the configuration
/// key the value will be read from.
/// </remarks>
public abstract class McpServerStoreContract : TenantIsolationContract<IMcpServerStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.SaveAsync(Server(tenantId, name), cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.GetAsync(tenantId, (string)key) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

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

        // The Authorization header's key name travels in the map like any
        // other credential header; a store that stops persisting it breaks
        // every server whose only credential is a bearer token.
        loaded.HeaderConfigurationKeys["Authorization"].ShouldBe("Tracon:McpSecrets:GithubToken");
    }

    [Fact]
    public async Task Every_field_of_a_server_survives_a_round_trip()
    {
        // 🚨 The reader addresses the row by column POSITION. A column added or
        // removed in one place only shifts every field after it and the row
        // still reads: the wrong value lands in the wrong property. Each
        // field gets a value no other field shares (the two adjacent booleans
        // differ, so a swap of their positions cannot pass), and the SAVED record is
        // checked as well as the one read back (SQL Server returns the row
        // through its own OUTPUT list).
        var written = Server("tenant-a", "full") with
        {
            Description = "Every field is set.",
            Transport = McpTransportMode.Sse,
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Team"] = "platform" },
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = "Tracon:McpSecrets:GithubToken",
                ["X-Api-Key"] = "Tracon:McpSecrets:SearchKey",
            },
            Enabled = true,
            RequiresApproval = false,
            OAuthEnabled = true,
            OAuthClientId = "client-id-1",
            OAuthClientSecretConfigurationKey = "Tracon:McpSecrets:ClientSecret",
            OAuthScopes = "repo read:user",
        };

        var saved = await Store.SaveAsync(written);
        var loaded = await Store.GetAsync("tenant-a", "full");

        foreach (var server in new[] { saved, loaded.ShouldNotBeNull() })
        {
            server.TenantId.ShouldBe("tenant-a");
            server.Name.ShouldBe("full");
            server.Description.ShouldBe("Every field is set.");
            server.Endpoint.ShouldBe(written.Endpoint);
            server.Transport.ShouldBe(McpTransportMode.Sse);
            server.Headers.ShouldBe(written.Headers);
            server.HeaderConfigurationKeys.Count.ShouldBe(2);
            server.HeaderConfigurationKeys["Authorization"].ShouldBe("Tracon:McpSecrets:GithubToken");
            server.HeaderConfigurationKeys["X-Api-Key"].ShouldBe("Tracon:McpSecrets:SearchKey");
            server.Enabled.ShouldBeTrue();
            server.RequiresApproval.ShouldBeFalse();
            server.OAuthEnabled.ShouldBeTrue();
            server.OAuthClientId.ShouldBe("client-id-1");
            server.OAuthClientSecretConfigurationKey.ShouldBe("Tracon:McpSecrets:ClientSecret");
            server.OAuthScopes.ShouldBe("repo read:user");
            server.OAuthAuthorizationMode.ShouldBe(McpOAuthAuthorizationMode.AuthorizationCode);
        }
    }

    [Fact]
    public async Task Header_configuration_keys_are_preserved()
    {
        // The SAVED record is checked as well as the one read back: a store
        // that returns the written row from its own column list (SQL Server's
        // OUTPUT clause) can drop the field there while a later read has it.
        var saved = await Store.SaveAsync(Server("tenant-a", "search") with
        {
            Headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["X-Team"] = "platform" },
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Api-Key"] = "Tracon:McpSecrets:SearchKey",
                ["Cookie"] = "Tracon:McpSecrets:SearchSession",
            },
        });

        var loaded = await Store.GetAsync("tenant-a", "search");

        foreach (var server in new[] { saved, loaded.ShouldNotBeNull() })
        {
            server.Headers["X-Team"].ShouldBe("platform");
            server.HeaderConfigurationKeys.Count.ShouldBe(2);
            server.HeaderConfigurationKeys["X-Api-Key"].ShouldBe("Tracon:McpSecrets:SearchKey");
            server.HeaderConfigurationKeys["Cookie"].ShouldBe("Tracon:McpSecrets:SearchSession");
        }

        // An update replaces the map; an upsert that leaves the column out of
        // its UPDATE list keeps the old names.
        await Store.SaveAsync(loaded with { HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) });

        (await Store.GetAsync("tenant-a", "search"))!.HeaderConfigurationKeys.ShouldBeEmpty();
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
            HeaderConfigurationKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Authorization"] = "Tracon:McpSecrets:GithubToken",
            },
            Enabled = true,
        };
}
