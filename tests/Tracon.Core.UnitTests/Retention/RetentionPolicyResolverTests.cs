using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Retention;

/// <summary>
/// Tests for how <see cref="RetentionPolicyResolver"/> merges the database
/// policy with the configuration default.
/// </summary>
public sealed class RetentionPolicyResolverTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task Nothing_is_returned_when_there_is_no_policy_and_configuration_is_disabled()
    {
        var resolver = Build(new TraconRetentionOptions { Enabled = false });

        var resolved = await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldBeNull();
    }

    [Fact]
    public async Task The_configuration_default_is_used_when_there_is_no_policy()
    {
        var options = new TraconRetentionOptions { Enabled = true };
        var resolver = Build(options);

        var resolved = await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.Target.ShouldBe(RetentionTargets.RunEvents);
        resolved.MaxAgeDays.ShouldBe(options.RunEvents.MaxAgeDays!.Value);
    }

    [Fact]
    public async Task User_data_targets_default_to_disabled_even_when_configuration_is_enabled()
    {
        // For Sessions/Conversations, RetentionTargetOptions.MaxAgeDays defaults to
        // null — Enabled=true alone is NOT enough; MaxAgeDays must also be set
        // explicitly (user data, default DISABLED).
        var resolver = Build(new TraconRetentionOptions { Enabled = true });

        (await resolver.ResolveAsync(Tenant, RetentionTargets.Sessions)).ShouldBeNull();
        (await resolver.ResolveAsync(Tenant, RetentionTargets.Conversations)).ShouldBeNull();
    }

    [Fact]
    public async Task A_configured_age_for_a_user_data_target_applies_like_the_age_of_any_other_target()
    {
        // Off by default is the absence of an age, not an exemption from configuration: the resolver does not read the
        // list of user data targets, so an age set in configuration takes effect once retention is enabled.
        var options = new TraconRetentionOptions { Enabled = true };
        options.Sessions.MaxAgeDays = 90;
        options.Conversations.MaxAgeDays = 365;
        var resolver = Build(options);

        (await resolver.ResolveAsync(Tenant, RetentionTargets.Sessions)).ShouldNotBeNull().MaxAgeDays.ShouldBe(90);
        (await resolver.ResolveAsync(Tenant, RetentionTargets.Conversations)).ShouldNotBeNull().MaxAgeDays.ShouldBe(365);
    }

    [Fact]
    public async Task The_database_policy_wins_without_ever_looking_at_configuration()
    {
        var options = new TraconRetentionOptions { Enabled = true };
        var store = new InMemoryRetentionPolicyStore();
        var now = DateTimeOffset.UtcNow;

        // The database has a record that is EXPLICITLY disabled; nothing should
        // be deleted even though configuration is enabled.
        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = 999,
            Enabled = false,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var resolver = new RetentionPolicyResolver(store, new StaticOptionsMonitor<TraconRetentionOptions>(options));

        (await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents)).ShouldBeNull();
    }

    [Fact]
    public async Task A_tenant_specific_policy_takes_precedence_over_the_global_wildcard()
    {
        var store = new InMemoryRetentionPolicyStore();
        var now = DateTimeOffset.UtcNow;

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = "*",
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = 30,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = 7,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var resolver = new RetentionPolicyResolver(
            store,
            new StaticOptionsMonitor<TraconRetentionOptions>(new TraconRetentionOptions()));

        var resolved = await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.MaxAgeDays.ShouldBe(7);
    }

    [Fact]
    public async Task A_policy_with_only_MaxRows_set_resolves_correctly()
    {
        // 🚨 Gap closed: MaxAgeDays EMPTY, only MaxRows set.
        // ResolveAsync used to treat such a record as "nothing will be deleted".
        var store = new InMemoryRetentionPolicyStore();
        var now = DateTimeOffset.UtcNow;

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = null,
            MaxRows = 100,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var resolver = new RetentionPolicyResolver(
            store,
            new StaticOptionsMonitor<TraconRetentionOptions>(new TraconRetentionOptions()));

        var resolved = await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents);

        resolved.ShouldNotBeNull();
        resolved.MaxAgeDays.ShouldBeNull();
        resolved.MaxRows.ShouldBe(100);
    }

    [Fact]
    public async Task A_policy_with_both_fields_empty_returns_nothing()
    {
        var store = new InMemoryRetentionPolicyStore();
        var now = DateTimeOffset.UtcNow;

        await store.SavePolicyAsync(new RetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = Tenant,
            Target = RetentionTargets.RunEvents,
            MaxAgeDays = null,
            MaxRows = null,
            Enabled = true,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var resolver = new RetentionPolicyResolver(
            store,
            new StaticOptionsMonitor<TraconRetentionOptions>(new TraconRetentionOptions()));

        (await resolver.ResolveAsync(Tenant, RetentionTargets.RunEvents)).ShouldBeNull();
    }

    private static RetentionPolicyResolver Build(TraconRetentionOptions options)
        => new(new InMemoryRetentionPolicyStore(), new StaticOptionsMonitor<TraconRetentionOptions>(options));
}
