namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="ISkillScriptGrantStore"/> contract.
/// </summary>
/// <remarks>
/// Every implementation must pass the same scenarios; in particular, the
/// "the narrower grant wins over the broader one" rule and the fact that a
/// revoked grant never comes back must behave identically.
/// </remarks>
public abstract class SkillScriptGrantContract : TenantIsolationContract<ISkillScriptGrantStore>
{
    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        await Store.GrantAsync(Grant(tenantId, name), cancellationToken);
        return name;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
        => await Store.FindActiveAsync(tenantId, (string)key, "any", DateTimeOffset.UtcNow) is not null;

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
        => (await Store.ListAsync(tenantId, cancellationToken)).Count;

    /// <inheritdoc />
    /// <remarks>A grant is not deleted, it is revoked.</remarks>
    protected override async ValueTask<bool?> TryDeleteAsync(string tenantId, object key)
        => await Store.RevokeAsync(tenantId, (string)key, scriptName: null);

    [Fact]
    public async Task Skill_wide_grant_covers_every_script()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        var found = await Store.FindActiveAsync("tenant-a", "invoice", "any-script", DateTimeOffset.UtcNow);

        found.ShouldNotBeNull();
        found.ScriptName.ShouldBeNull();
    }

    [Fact]
    public async Task Script_specific_grant_wins_over_the_skill_wide_grant()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));
        await Store.GrantAsync(Grant("tenant-a", "invoice", "total"));

        var found = await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow);

        found.ShouldNotBeNull();
        string.Equals(found.ScriptName, "total", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task Grant_does_not_leak_across_tenants()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        var found = await Store.FindActiveAsync("tenant-b", "invoice", "total", DateTimeOffset.UtcNow);

        found.ShouldBeNull();
    }

    [Fact]
    public async Task Expired_grant_is_not_returned()
    {
        var now = DateTimeOffset.UtcNow;
        await Store.GrantAsync(Grant("tenant-a", "invoice") with { ExpiresAt = now.AddMinutes(5) });

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", now)).ShouldNotBeNull();
        (await Store.FindActiveAsync("tenant-a", "invoice", "total", now.AddMinutes(10))).ShouldBeNull();
    }

    [Fact]
    public async Task Revoked_grant_is_invalid()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        (await Store.RevokeAsync("tenant-a", "invoice", null)).ShouldBeTrue();

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldBeNull();
        (await Store.RevokeAsync("tenant-a", "invoice", null)).ShouldBeFalse();
    }

    [Fact]
    public async Task Granting_the_same_grant_twice_leaves_a_single_record()
    {
        // script_name can be NULL, so uniqueness is built with COALESCE;
        // otherwise PostgreSQL treats NULLs as distinct and duplicate rows
        // would accumulate.
        await Store.GrantAsync(Grant("tenant-a", "invoice"));
        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        var all = await Store.ListAsync("tenant-a");

        all.Count(grant => grant.ScriptName is null).ShouldBe(1);
    }

    [Fact]
    public async Task Revoked_grant_can_be_granted_again()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice"));
        await Store.RevokeAsync("tenant-a", "invoice", null);

        await Store.GrantAsync(Grant("tenant-a", "invoice"));

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldNotBeNull();
    }

    private static SkillScriptGrant Grant(string tenantId, string skillName, string? scriptName = null) => new()
    {
        TenantId = tenantId,
        SkillName = skillName,
        ScriptName = scriptName,
        GrantedBy = "admin@example.com",
        GrantedAt = DateTimeOffset.UtcNow,
    };
}
