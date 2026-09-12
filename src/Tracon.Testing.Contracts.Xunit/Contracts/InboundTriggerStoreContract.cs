namespace Tracon.Testing.Contracts.Storage;

/// <summary>Behavior tests for the <see cref="IInboundTriggerStore"/> contract.</summary>
/// <remarks>
/// This contract never asserts a signing secret VALUE: the store carries
/// only the configuration key's NAME. It exists to prove tenant
/// isolation and round-tripping of the trigger definition.
/// </remarks>
public abstract class InboundTriggerStoreContract : TenantIsolationContract<IInboundTriggerStore>
{
    private const string Tenant = "test";

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name)
    {
        await Store.UpsertAsync(Trigger(tenantId, name));

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
    public async Task Saved_trigger_is_read_back()
    {
        await Store.UpsertAsync(Trigger(Tenant, "slack"));

        var trigger = await Store.GetAsync(Tenant, "slack");

        trigger.ShouldNotBeNull();
        trigger.TenantId.ShouldBe(Tenant);
        trigger.Name.ShouldBe("slack");
        trigger.TargetKind.ShouldBe(InboundTriggerTargetKind.Agent);
        trigger.TargetName.ShouldBe("demo");
        trigger.SigningSecretConfigurationName.ShouldBe("Tracon:TriggerSecrets:Test:Slack");
        trigger.PayloadMode.ShouldBe(InboundTriggerPayloadMode.WholeBody);
        trigger.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task Path_mode_and_path_round_trip()
    {
        await Store.UpsertAsync(Trigger(Tenant, "slack") with
        {
            PayloadMode = InboundTriggerPayloadMode.Path,
            PayloadPath = "event.text",
        });

        var trigger = await Store.GetAsync(Tenant, "slack");

        trigger.ShouldNotBeNull();
        trigger.PayloadMode.ShouldBe(InboundTriggerPayloadMode.Path);
        trigger.PayloadPath.ShouldBe("event.text");
    }

    [Fact]
    public async Task Workflow_target_kind_round_trips()
    {
        await Store.UpsertAsync(Trigger(Tenant, "slack") with
        {
            TargetKind = InboundTriggerTargetKind.Workflow,
            TargetName = "demo-workflow",
        });

        var trigger = await Store.GetAsync(Tenant, "slack");

        trigger.ShouldNotBeNull();
        trigger.TargetKind.ShouldBe(InboundTriggerTargetKind.Workflow);
        trigger.TargetName.ShouldBe("demo-workflow");
    }

    [Fact]
    public async Task Missing_trigger_returns_null()
        => (await Store.GetAsync(Tenant, "unknown")).ShouldBeNull();

    [Fact]
    public async Task Upsert_replaces_the_existing_trigger_for_the_same_tenant_and_name()
    {
        var first = await Store.UpsertAsync(Trigger(Tenant, "slack"));
        var second = await Store.UpsertAsync(Trigger(Tenant, "slack") with { TargetName = "other-agent", Enabled = false });

        second.Id.ShouldBe(first.Id);

        var trigger = await Store.GetAsync(Tenant, "slack");

        trigger.ShouldNotBeNull();
        trigger.TargetName.ShouldBe("other-agent");
        trigger.Enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Listing_returns_every_trigger_of_the_tenant()
    {
        await Store.UpsertAsync(Trigger(Tenant, "slack"));
        await Store.UpsertAsync(Trigger(Tenant, "github"));

        var triggers = await Store.ListAsync(Tenant);

        triggers.Count.ShouldBe(2);
        var names = triggers.Select(static trigger => trigger.Name).ToList();
        names.ShouldContain("slack", StringComparer.Ordinal);
        names.ShouldContain("github", StringComparer.Ordinal);
    }

    [Fact]
    public async Task DeleteAsync_returns_false_for_a_trigger_that_does_not_exist()
        => (await Store.DeleteAsync(Tenant, "missing")).ShouldBeFalse();

    private static InboundTrigger Trigger(string tenantId, string name)
    {
        var now = DateTimeOffset.UtcNow;

        return new InboundTrigger
        {
            TenantId = tenantId,
            Name = name,
            TargetKind = InboundTriggerTargetKind.Agent,
            TargetName = "demo",
            SigningSecretConfigurationName = "Tracon:TriggerSecrets:Test:Slack",
            PayloadMode = InboundTriggerPayloadMode.WholeBody,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
