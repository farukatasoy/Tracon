using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace AgentPrism;

/// <summary>Ekleri PostgreSQL'de saklayan tenant-yalitimli depo.</summary>
/// <remarks>
/// <see cref="IAttachmentStorage"/> kayitliysa icerik orada yasar ve
/// <c>attachments.content</c> <c>NULL</c> kalir, <c>external_uri</c> dolar.
/// Kayitli degilse icerik dogrudan <c>bytea</c> sutununda tasinir.
/// Gerekce: <c>docs/14-COK-MODLULUK.md</c>, bolum 14.1 ve 14.3.
/// </remarks>
public sealed class PostgresAttachmentStore : IAttachmentStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly int _commandTimeout;
    private readonly IAttachmentStorage? _storage;

    /// <summary>Yeni bir ek deposu olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="storage">Kayitliysa icerigin yazilacagi harici depo.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresAttachmentStore(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        IAttachmentStorage? storage = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _commandTimeout = options.Value.CommandTimeoutSeconds;
        _storage = storage;
    }

    /// <inheritdoc />
    public async ValueTask<AttachmentDescriptor> SaveAsync(
        AttachmentContent content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var id = AgentPrismId.NewId();
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
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("tenant_id", content.TenantId);
        command.Parameters.AddWithValue("session_id", (object?)content.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("run_id", (object?)content.RunId ?? DBNull.Value);
        command.Parameters.AddWithValue("file_name", content.FileName);
        command.Parameters.AddWithValue("media_type", content.MediaType);
        command.Parameters.AddWithValue("byte_size", content.Data.Length);
        command.Parameters.AddWithValue("sha256", sha256);
        command.Parameters.Add(new NpgsqlParameter("content", NpgsqlDbType.Bytea)
        {
            Value = (object?)dbContent ?? DBNull.Value,
        });
        command.Parameters.AddWithValue("external_uri", (object?)externalUri?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("created_by", (object?)content.CreatedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at", now.UtcDateTime);

        await NpgsqlHelpers.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);

        return await NpgsqlHelpers.ReadSingleAsync(command, ReadDescriptor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<Stream?> OpenReadAsync(
        string tenantId,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.SelectAttachmentContent);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);

        var row = await NpgsqlHelpers.ReadSingleAsync(command, ReadContentRow, cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        if (row.ExternalUri is { } externalUri && _storage is not null)
        {
            return await _storage.ReadAsync(new Uri(externalUri, UriKind.RelativeOrAbsolute), cancellationToken)
                .ConfigureAwait(false);
        }

        return row.Content is { } bytes ? new MemoryStream(bytes, writable: false) : null;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<AttachmentDescriptor>> ListAsync(
        AttachmentQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var command = CreateCommand(_sql.SelectAttachments);
        command.Parameters.AddWithValue("tenant_id", query.TenantId);
        command.Parameters.AddWithValue("session_id", (object?)query.SessionId ?? DBNull.Value);
        command.Parameters.AddWithValue("skip", query.Skip);
        command.Parameters.AddWithValue("take", query.Take);

        return await NpgsqlHelpers.ReadListAsync(command, ReadDescriptor, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteAsync(string tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);

        var command = CreateCommand(_sql.DeleteAttachment);
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("id", id);

        var deleted = await NpgsqlHelpers.ReadListAsync(
            command,
            static reader => NpgsqlHelpers.GetNullableString(reader, 0),
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
        command.Parameters.AddWithValue("tenant_id", tenantId);
        command.Parameters.AddWithValue("session_id", sessionId);

        var deleted = await NpgsqlHelpers.ReadListAsync(
            command,
            static reader => NpgsqlHelpers.GetNullableString(reader, 0),
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

    private static AttachmentDescriptor ReadDescriptor(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TenantId = reader.GetString(1),
        SessionId = NpgsqlHelpers.GetNullableString(reader, 2),
        RunId = reader.IsDBNull(3) ? null : reader.GetGuid(3),
        FileName = reader.GetString(4),
        MediaType = reader.GetString(5),
        ByteSize = reader.GetInt64(6),
        Sha256 = reader.GetString(7),
        CreatedBy = NpgsqlHelpers.GetNullableString(reader, 8),
        CreatedAt = NpgsqlHelpers.GetTimestamp(reader, 9),
    };

    private static ContentRow ReadContentRow(NpgsqlDataReader reader) => new(
        reader.IsDBNull(0) ? null : (byte[])reader.GetValue(0),
        NpgsqlHelpers.GetNullableString(reader, 1));

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;
        return command;
    }

    private sealed record ContentRow(byte[]? Content, string? ExternalUri);
}
