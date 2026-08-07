using System.Data.Common;

namespace AgentPrism;

/// <summary>Konusmalari kopyalayarak dallandiran SQL deposu (Faz 47).</summary>
/// <remarks>
/// <para>
/// Kopyalama tek bir islemde (transaction) yapilir: dal konusmasi ve ogeleri ya
/// birlikte olusur ya hic olusmaz. Yarim bir dal, gecmisi eksik bir oturum
/// demektir ve sessizce yanlis cevaplar uretirdi.
/// </para>
/// <para>
/// 🚨 Ogeler <c>INSERT … SELECT</c> ile <strong>tek ifadede</strong> degil,
/// okunup satir satir yazilir. Gerekce olculdu: yeni oge kimligi her satirda
/// yeni bir uuid v7 olmalidir (K-015) ve uc diyalektin hicbirinde ortak bir
/// uuid v7 uretici yoktur (PostgreSQL <c>gen_random_uuid()</c> v4 uretir,
/// SQL Server <c>NEWID()</c> siralanamaz, SQLite'in hicbir yerlesigi yoktur).
/// Saglayiciya ozgu uc ayri ifade yazmak yerine kimlik uygulamada uretilir;
/// yazma tek islem icinde kaldigi icin dayaniklilik degismez.
/// </para>
/// </remarks>
internal sealed class SqlConversationBranchStore : IConversationBranchStore
{
    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;

    /// <summary>Yeni bir SQL dallandirma deposu olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> <see langword="null"/> ise.</exception>
    public SqlConversationBranchStore(SqlStoreContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _sql = context.Sql;
    }

    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public async ValueTask<ConversationBranch?> BranchAsync(
        string tenantId,
        Guid parentConversationId,
        long? upToSequence,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var newConversationId = AgentPrismId.NewId();
        var now = DateTimeOffset.UtcNow;

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                var branchPoint = await ReadBranchPointAsync(
                    connection,
                    transaction,
                    parentConversationId,
                    upToSequence,
                    cancellationToken).ConfigureAwait(false);

                // Konusma satiri kaynaktan kopyalanir. Kaynak yoksa veya baska
                // bir kiraciya aitse SELECT bos doner ve hicbir satir yazilmaz;
                // etkilenen satir sayisi bunu tek sorguda soyler.
                var insert = _context.CreateCommand(_sql.InsertBranchConversation, connection, transaction);
                Dialect.AddUuid(insert, "id", newConversationId);
                Dialect.AddUuid(insert, "parent_conversation_id", parentConversationId);
                DbHelpers.Add(insert, "tenant_id", tenantId);
                Dialect.AddTimestamp(insert, "now", now);
                Dialect.AddInt64(insert, "branch_from_seq", branchPoint.LastSequence);

                if (await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false) == 0)
                {
                    return null;
                }

                var copied = await CopyItemsAsync(
                    connection,
                    transaction,
                    parentConversationId,
                    newConversationId,
                    upToSequence,
                    cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                return new ConversationBranch(newConversationId, branchPoint.LastSequence, copied);
            }
        }
    }

    private async ValueTask<BranchPoint> ReadBranchPointAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid parentConversationId,
        long? upToSequence,
        CancellationToken cancellationToken)
    {
        var command = _context.CreateCommand(_sql.SelectConversationBranchPoint, connection, transaction);
        Dialect.AddUuid(command, "conversation_id", parentConversationId);
        Dialect.AddInt64(command, "up_to_sequence", upToSequence);

        var point = await DbHelpers
            .ReadSingleAsync(
                command,
                static reader => new BranchPoint(reader.GetInt64(0), reader.GetInt64(1)),
                cancellationToken)
            .ConfigureAwait(false);

        // Toplam sorgusu her zaman bir satir doner; yine de savunmaci bir
        // varsayilan birakiyoruz (bos konusma: -1 ve 0).
        return point ?? new BranchPoint(-1, 0);
    }

    private async ValueTask<int> CopyItemsAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid parentConversationId,
        Guid newConversationId,
        long? upToSequence,
        CancellationToken cancellationToken)
    {
        var select = _context.CreateCommand(_sql.SelectConversationItemsForBranch, connection, transaction);
        Dialect.AddUuid(select, "conversation_id", parentConversationId);
        Dialect.AddInt64(select, "up_to_sequence", upToSequence);

        var items = await DbHelpers
            .ReadListAsync(
                select,
                static reader => new CopiedItem(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    DbHelpers.GetTimestamp(reader, 2)),
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in items)
        {
            var insert = _context.CreateCommand(_sql.InsertConversationItem, connection, transaction);
            Dialect.AddUuid(insert, "id", AgentPrismId.NewId());
            Dialect.AddUuid(insert, "conversation_id", newConversationId);
            Dialect.AddInt64(insert, "seq", item.Sequence);

            // 🚨 Metin AYNEN tasinir; yeniden serilestirilmez. Bir tur
            // deserialize/serialize, `$type` ayracinin yerini degistirebilir
            // ve dalin gecmisi okunamaz hale gelirdi (K-027).
            Dialect.AddJson(insert, "item", item.Item);
            Dialect.AddTimestamp(insert, "created_at", item.CreatedAt);

            await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);
        }

        return items.Count;
    }

    private sealed record BranchPoint(long LastSequence, long ItemCount);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly record struct CopiedItem(long Sequence, string Item, DateTimeOffset CreatedAt);
}
