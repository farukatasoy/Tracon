using System.Data.Common;

namespace Tracon;

/// <summary>Tenant-isolated store for attachments in the SQL database.</summary>
/// <remarks>
/// When <see cref="IAttachmentStorage"/> is registered, the content lives there,
/// <c>attachments.content</c> stays <c>NULL</c>, and <c>external_uri</c> is filled
/// in. When it is not registered, the content is carried directly in the
/// <c>bytea</c> column.
/// </remarks>
internal sealed class SqlAttachmentStore : IAttachmentStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly IAttachmentStorage? _storage;

    /// <summary>Creates a new attachment store.</summary>
    /// <param name="context">The store context.</param>
    /// <param name="storage">The external store the content is written to when registered.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public SqlAttachmentStore(
        SqlStoreContext context,
        IAttachmentStorage? storage = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
        _storage = storage;
    }

    /// <summary>The gateway for provider-specific behavior.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<AttachmentDescriptor> SaveAsync(
        AttachmentContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var id = TraconId.NewId();
        var now = DateTimeOffset.UtcNow;
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content.Data.Span));

        byte[]? dbContent = null;
        Uri? externalUri = null;

        if (_storage is not null)
        {
            using var stream = new MemoryStream(content.Data.ToArray(), writable: false);
            externalUri = await _storage
                .WriteAsync(content.TenantId, id, stream, content.MediaType, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            dbContent = content.Data.ToArray();
        }

        var command = CreateCommand(_sql.InsertAttachment);
        DbHelpers.Add(command, "id", id);
        DbHelpers.Add(command, "tenant_id", content.TenantId);
        Dialect.AddText(command, "session_id", content.SessionId);
        Dialect.AddUuid(command, "run_id", content.RunId);
        DbHelpers.Add(command, "file_name", content.FileName);
        DbHelpers.Add(command, "media_type", content.MediaType);
        DbHelpers.Add(command, "byte_size", content.Data.Length);
        DbHelpers.Add(command, "sha256", sha256);
        // The hash above is computed on PLAINTEXT before this call, on
        // purpose: it identifies the file's actual content and must not
        // change depending on whether protection is on.
        Dialect.AddBinary(command, "content", ProtectedValue.WriteBytes(_context, ProtectedColumn.AttachmentContent, dbContent));
        Dialect.AddText(command, "external_uri", externalUri?.ToString());
        Dialect.AddText(command, "created_by", content.CreatedBy);
        Dialect.AddTimestamp(command, "created_at", now);

        await DbHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return new AttachmentDescriptor
        {
            Id = id,
            TenantId = content.TenantId,
            SessionId = content.SessionId,
            RunId = content.RunId,
            FileName = content.FileName,
            MediaType = content.MediaType,
            ByteSize = content.Data.Length,
            Sha256 = sha256,
            CreatedBy = content.CreatedBy,
            CreatedAt = now,
        };
    }

    /// <inheritdoc />
    public async ValueTask<AttachmentDescriptor?> GetAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectAttachment);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "id", id);

        return await DbHelpers.ReadSingleAsync(command, ReadDescriptor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Stream?> OpenReadAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectAttachmentContent);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "id", id);

        var row = await DbHelpers.ReadSingleAsync(command, ReadContentRow, cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        if (row.ExternalUri is { } externalUri && _storage is not null)
        {
            return await _storage.ReadAsync(new Uri(externalUri, UriKind.RelativeOrAbsolute), cancellationToken)
                .ConfigureAwait(false);
        }

        return row.Content is { } bytes ? new MemoryStream(ProtectedValue.ReadBytes(_context, bytes)!, writable: false) : null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(
        AttachmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectAttachments);
        DbHelpers.Add(command, "tenant_id", query.TenantId);
        Dialect.AddText(command, "session_id", query.SessionId);
        DbHelpers.Add(command, "skip", query.Skip);
        DbHelpers.Add(command, "take", query.Take);

        return await DbHelpers.ReadListAsync(command, ReadDescriptor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.DeleteAttachment);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "id", id);

        var deleted = await DbHelpers.ReadListAsync(
            command,
            static reader => DbHelpers.GetNullableString(reader, 0),
            cancellationToken).ConfigureAwait(false);

        if (deleted.Count == 0)
        {
            return false;
        }

        if (deleted[0] is { } externalUri && _storage is not null)
        {
            await _storage.DeleteAsync(new Uri(externalUri, UriKind.RelativeOrAbsolute), cancellationToken)
                .ConfigureAwait(false);
        }

        return true;
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteBySessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(sessionId);

        var command = CreateCommand(_sql.DeleteAttachmentsBySession);
        DbHelpers.Add(command, "tenant_id", tenantId);
        DbHelpers.Add(command, "session_id", sessionId);

        var deleted = await DbHelpers.ReadListAsync(
            command,
            static reader => DbHelpers.GetNullableString(reader, 0),
            cancellationToken).ConfigureAwait(false);

        if (_storage is not null)
        {
            foreach (var externalUri in deleted)
            {
                if (externalUri is { } uri)
                {
                    await _storage.DeleteAsync(new Uri(uri, UriKind.RelativeOrAbsolute), cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        return deleted.Count;
    }

    private static AttachmentDescriptor ReadDescriptor(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TenantId = reader.GetString(1),
        SessionId = DbHelpers.GetNullableString(reader, 2),
        RunId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
        FileName = reader.GetString(4),
        MediaType = reader.GetString(5),
        ByteSize = reader.GetInt64(6),
        Sha256 = reader.GetString(7),
        CreatedBy = DbHelpers.GetNullableString(reader, 8),
        CreatedAt = DbHelpers.GetTimestamp(reader, 9),
    };

    private static ContentRow ReadContentRow(DbDataReader reader) => new(
        reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0),
        DbHelpers.GetNullableString(reader, 1));

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    private sealed record ContentRow(byte[]? Content, string? ExternalUri);
}
