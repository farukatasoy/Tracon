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

    /// <summary>A grant pins the content it authorizes; every read returns the pinned hash.</summary>
    [Fact]
    public async Task Content_hash_round_trips()
    {
        var hash = new string('A', 63) + "1";

        var saved = await Store.GrantAsync(Grant("tenant-a", "invoice", "total") with { ContentHash = hash });

        saved.ContentHash.ShouldBe(hash);
        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldNotBeNull().ContentHash.ShouldBe(hash);
        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem().ContentHash.ShouldBe(hash);
    }

    /// <summary>
    /// A grant that pins nothing stays that way: it authorizes scripts on disk only,
    /// and a store must not invent a value for it.
    /// </summary>
    [Fact]
    public async Task Null_content_hash_round_trips()
    {
        var saved = await Store.GrantAsync(Grant("tenant-a", "invoice"));

        saved.ContentHash.ShouldBeNull();
        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldNotBeNull().ContentHash.ShouldBeNull();
        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem().ContentHash.ShouldBeNull();
    }

    /// <summary>
    /// Granting the same skill and script again pins the NEW content. An upsert whose
    /// update branch forgot the hash would keep authorizing the previous content.
    /// </summary>
    [Fact]
    public async Task Granting_again_replaces_the_content_hash()
    {
        var first = new string('A', 64);
        var second = new string('B', 64);

        await Store.GrantAsync(Grant("tenant-a", "invoice", "total") with { ContentHash = first });
        await Store.GrantAsync(Grant("tenant-a", "invoice", "total") with { ContentHash = second });

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldNotBeNull().ContentHash.ShouldBe(second);
        (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem().ContentHash.ShouldBe(second);

        await Store.GrantAsync(Grant("tenant-a", "invoice", "total"));

        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldNotBeNull().ContentHash.ShouldBeNull();
    }

    /// <summary>
    /// A grant is never deleted: revoking stamps the revocation time and the record
    /// stays listed, so who granted what and when it was withdrawn stays answerable.
    /// </summary>
    [Fact]
    public async Task Revoked_grant_stays_listed()
    {
        await Store.GrantAsync(Grant("tenant-a", "invoice") with { ContentHash = new string('C', 64) });

        (await Store.RevokeAsync("tenant-a", "invoice", null)).ShouldBeTrue();

        var listed = (await Store.ListAsync("tenant-a")).ShouldHaveSingleItem();
        listed.RevokedAt.ShouldNotBeNull();
        listed.ContentHash.ShouldBe(new string('C', 64));
    }

    /// <summary>
    /// Grants and revocations of one key racing each other never leave two records
    /// for the key, and a grant written after the race is the one in force.
    /// </summary>
    [Fact]
    public async Task Racing_grants_and_revocations_leave_one_record_for_the_key()
    {
        var operations = Enumerable.Range(0, 24).Select(index => index % 2 == 0
            ? Store.GrantAsync(Grant("tenant-a", "invoice", "total") with { ContentHash = new string((char)('A' + (index % 6)), 64) }).AsTask()
            : (Task)Store.RevokeAsync("tenant-a", "invoice", "total").AsTask());

        await Task.WhenAll(operations);

        var final = new string('F', 64);
        await Store.GrantAsync(Grant("tenant-a", "invoice", "total") with { ContentHash = final });

        var listed = (await Store.ListAsync("tenant-a")).Where(static grant => string.Equals(grant.ScriptName, "total", StringComparison.Ordinal)).ToList();
        var record = listed.ShouldHaveSingleItem();
        record.RevokedAt.ShouldBeNull();
        record.ContentHash.ShouldBe(final);
        (await Store.FindActiveAsync("tenant-a", "invoice", "total", DateTimeOffset.UtcNow)).ShouldNotBeNull().ContentHash.ShouldBe(final);
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
