using Microsoft.Extensions.Logging.Abstractions;

namespace Tracon.Core.UnitTests.Audit;

/// <summary>
/// Granting the right to run a skill script grants the right to run code on the
/// server, so both <c>script.grant</c> and <c>script.revoke</c> are fail-closed: the
/// audit row is written BEFORE the store is touched, and a failed write leaves the
/// permission exactly as it was.
/// </summary>
/// <remarks>
/// The ordering is the whole point and it is invisible to a compiler. Swapping the two
/// statements in either method still compiles, still passes every architecture test —
/// and hands out a permission to execute code with no record of who granted it.
/// </remarks>
public sealed class AuditingSkillScriptGrantStoreTests
{
    private const string Tenant = "tenant-a";

    [Fact]
    public async Task A_grant_is_not_persisted_when_its_audit_entry_cannot_be_written()
    {
        var inner = new RecordingGrantStore();
        var store = Create(inner, new ThrowingAuditLog());

        await Should.ThrowAsync<TraconException>(async () =>
            await store.GrantAsync(Grant(), TestContext.Current.CancellationToken));

        inner.Grants.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_revoke_is_not_applied_when_its_audit_entry_cannot_be_written()
    {
        var inner = new RecordingGrantStore();
        var store = Create(inner, new ThrowingAuditLog());

        await Should.ThrowAsync<TraconException>(async () =>
            await store.RevokeAsync(Tenant, "notes", "build", TestContext.Current.CancellationToken));

        inner.Revokes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_healthy_audit_log_lets_both_operations_through_and_records_them()
    {
        var inner = new RecordingGrantStore();
        var audit = new CapturingAuditLog();
        var store = Create(inner, audit);

        await store.GrantAsync(Grant(), TestContext.Current.CancellationToken);
        await store.RevokeAsync(Tenant, "notes", "build", TestContext.Current.CancellationToken);

        inner.Grants.Count.ShouldBe(1);
        inner.Revokes.Count.ShouldBe(1);
        audit.Entries.Select(entry => entry.Action).ShouldBe(["script.grant", "script.revoke"]);
        audit.Entries.ShouldAllBe(entry => entry.Entity == "notes/build");
    }

    /// <summary>
    /// A request cancelled between the two writes keeps the safe half of the order:
    /// the audit row records an attempt and no permission exists. The reverse - a
    /// persisted grant with no record - cannot come out of a cancelled request.
    /// </summary>
    [Fact]
    public async Task A_grant_cancelled_after_its_audit_entry_leaves_no_permission()
    {
        using var cancellation = new CancellationTokenSource();
        var inner = new RecordingGrantStore();
        var audit = new CancellingAuditLog(cancellation);
        var store = Create(inner, audit);

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await store.GrantAsync(Grant(), cancellation.Token));

        audit.Entries.Select(entry => entry.Action).ShouldBe(["script.grant"]);
        inner.Grants.ShouldBeEmpty();
    }

    private static AuditingSkillScriptGrantStore Create(ISkillScriptGrantStore inner, IAuditLog auditLog)
        => new(
            inner,
            auditLog,
            new NullAuditActorResolver(),
            NullLogger<AuditingSkillScriptGrantStore>.Instance,
            metrics: null);

    private static SkillScriptGrant Grant()
        => new()
        {
            TenantId = Tenant,
            SkillName = "notes",
            ScriptName = "build",
            GrantedBy = "operator",
            GrantedAt = DateTimeOffset.UnixEpoch,
        };

    private sealed class RecordingGrantStore : ISkillScriptGrantStore
    {
        public List<SkillScriptGrant> Grants { get; } = [];

        public List<string> Revokes { get; } = [];

        public ValueTask<IReadOnlyList<SkillScriptGrant>> ListAsync(
            string tenantId,
            CancellationToken cancellationToken = default)
            => new((IReadOnlyList<SkillScriptGrant>)Grants);

        public ValueTask<SkillScriptGrant?> FindActiveAsync(
            string tenantId,
            string skillName,
            string scriptName,
            DateTimeOffset instant,
            CancellationToken cancellationToken = default)
            => new((SkillScriptGrant?)null);

        public ValueTask<SkillScriptGrant> GrantAsync(
            SkillScriptGrant grant,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Grants.Add(grant);

            return new ValueTask<SkillScriptGrant>(grant);
        }

        public ValueTask<bool> RevokeAsync(
            string tenantId,
            string skillName,
            string? scriptName,
            CancellationToken cancellationToken = default)
        {
            Revokes.Add($"{skillName}/{scriptName}");

            return new ValueTask<bool>(true);
        }
    }

    private sealed class NullAuditActorResolver : IAuditActorResolver
    {
        public string? Resolve() => "operator";
    }

    private sealed class ThrowingAuditLog : IAuditLog
    {
        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("audit store is unreachable");

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Array.Empty<AuditEntry>());

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by these tests");
    }

    /// <summary>Records the entry, then cancels the request that wrote it.</summary>
    private sealed class CancellingAuditLog(CancellationTokenSource cancellation) : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public async ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            await cancellation.CancelAsync();
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Entries);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by these tests");
    }

    private sealed class CapturingAuditLog : IAuditLog
    {
        public List<AuditEntry> Entries { get; } = [];

        public ValueTask WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);

            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => new(Entries);

        public ValueTask<AuditChainVerification> VerifyChainAsync(AuditChainQuery query, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("not exercised by these tests");
    }
}
