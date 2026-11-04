using System.Data.Common;

namespace Tracon;

/// <summary>Stores inbound trigger definitions in the SQL database.</summary>
/// <remarks>
/// The behavior contract is identical to <c>InMemoryInboundTriggerStore</c>
/// and is guarded by the shared contract tests.
/// </remarks>
internal sealed class SqlInboundTriggerStore : IInboundTriggerStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Creates a new inbound trigger store.</summary>
    /// <param name="context">The store context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public SqlInboundTriggerStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<InboundTrigger?> GetAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return await _context.ExecuteStatementAsync(
            _sql.SelectInboundTrigger,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                DbHelpers.Add(command, "name", name);
            },
            (command, token) => DbHelpers.ReadSingleAsync(command, ReadTrigger, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InboundTrigger>> ListAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        return await _context.ExecuteStatementAsync(
            _sql.SelectInboundTriggers,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
            },
            (command, token) => DbHelpers.ReadListAsync(command, ReadTrigger, token),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<InboundTrigger> UpsertAsync(InboundTrigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var id = trigger.Id == Guid.Empty ? TraconId.NewId() : trigger.Id;

        var written = await _context.ExecuteUpsertAsync(
            _sql.UpsertInboundTrigger,
            command =>
            {
                DbHelpers.Add(command, "id", id);
                DbHelpers.AddTenant(command, trigger.TenantId);
                DbHelpers.Add(command, "name", trigger.Name);
                DbHelpers.Add(command, "target_kind", (short)trigger.TargetKind);
                DbHelpers.Add(command, "target_name", trigger.TargetName);
                DbHelpers.Add(command, "signing_secret_configuration_name", trigger.SigningSecretConfigurationName);
                DbHelpers.Add(command, "payload_mode", (short)trigger.PayloadMode);
                Dialect.AddText(command, "payload_path", trigger.PayloadPath);
                DbHelpers.Add(command, "enabled", trigger.Enabled);
                Dialect.AddTimestamp(command, "created_at", trigger.CreatedAt);
                Dialect.AddTimestamp(command, "updated_at", trigger.UpdatedAt);
            },
            (command, token) => DbHelpers.ReadSingleAsync(
                command,
                static reader => new WrittenTrigger(reader.GetGuid(0), DbHelpers.GetTimestamp(reader, 1)),
                token),
            cancellationToken).ConfigureAwait(false)
            ?? throw new TraconException($"Failed to save the inbound trigger '{trigger.Name}'.");

        return trigger with { Id = written.Id, CreatedAt = written.CreatedAt };
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(
        string tenantId,
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return await _context.ExecuteStatementAsync(
            _sql.DeleteInboundTrigger,
            command =>
            {
                DbHelpers.AddTenant(command, tenantId);
                DbHelpers.Add(command, "name", name);
            },
            DbHelpers.ExecuteAsync,
            cancellationToken).ConfigureAwait(false) > 0;
    }

    private static InboundTrigger ReadTrigger(DbDataReader reader)
        => new()
        {
            Id = reader.GetGuid(0),
            TenantId = reader.GetString(1),
            Name = reader.GetString(2),
            TargetKind = (InboundTriggerTargetKind)reader.GetInt16(3),
            TargetName = reader.GetString(4),
            SigningSecretConfigurationName = reader.GetString(5),
            PayloadMode = (InboundTriggerPayloadMode)reader.GetInt16(6),
            PayloadPath = DbHelpers.GetNullableString(reader, 7),
            Enabled = reader.GetBoolean(8),
            CreatedAt = DbHelpers.GetTimestamp(reader, 9),
            UpdatedAt = DbHelpers.GetTimestamp(reader, 10),
        };

    private sealed record WrittenTrigger(Guid Id, DateTimeOffset CreatedAt);
}
