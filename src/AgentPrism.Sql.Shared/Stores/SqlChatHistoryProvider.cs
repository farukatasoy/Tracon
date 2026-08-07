using System.Data.Common;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgentPrism;

/// <summary>
/// Sohbet gecmisini PostgreSQL'de saklayan Microsoft Agent Framework saglayicisi.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Bu saglayicinin ornegi tum oturumlar arasinda paylasilir.</strong> Microsoft
/// Agent Framework'un acik uyarisi budur: saglayici agent'a baglanir ve ayni ornek her
/// oturumda kullanilir. Bu yuzden oturuma ozgu hicbir bilgi alan olarak tutulamaz.
/// Konusma kimligi <see cref="ProviderSessionState{TState}"/> ile oturumun kendi
/// durumunda tasinir; saglayici yalnizca veri kaynagi referansini tutar.
/// </para>
/// <para>
/// Mesajlar <c>conversation_items</c> tablosunda sirali olarak saklanir. Gecmisin
/// oturum durumunun icinde degil ayri bir tabloda yasamasi iki fayda saglar:
/// oturum satiri kucuk kalir ve gecmis SQL ile sorgulanabilir.
/// </para>
/// </remarks>
internal sealed class SqlChatHistoryProvider : ChatHistoryProvider
{
    /// <summary>
    /// Konusma kimliginin oturum icinde saklandigi durum anahtari.
    /// </summary>
    /// <remarks>
    /// Bu deger <strong>kararlidir</strong>; degistirmek mevcut oturumlarin gecmisini koparir.
    /// Anahtar <see cref="AgentPrismSessionStateKeys.ChatHistory"/> ile paylasilir:
    /// konusma dallandirmasi ayni durumu okuyup yeni oturuma yazar (Faz 47).
    /// </remarks>
    public const string SessionStateKey = AgentPrismSessionStateKeys.ChatHistory;

    private readonly SqlStoreContext _context;
    private readonly SqlQueriesBase _sql;
    private readonly ITenantContext _tenantContext;
    private readonly ProviderSessionState<ChatHistoryState> _sessionState;

    /// <summary>Yeni bir sohbet gecmisi saglayicisi olusturur.</summary>
    /// <param name="context">Depo baglami.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SqlChatHistoryProvider(
        SqlStoreContext context,
        ITenantContext tenantContext)
        : base(
            provideOutputMessageFilter: null,
            storeInputRequestMessageFilter: null,
            storeInputResponseMessageFilter: null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _context = context;
        _sql = context.Sql;
        _tenantContext = tenantContext;

        _sessionState = new ProviderSessionState<ChatHistoryState>(
            stateInitializer: static _ => new ChatHistoryState { ConversationId = AgentPrismId.NewId() },
            stateKey: SessionStateKey,
            jsonSerializerOptions: AgentPrismJsonContext.Default.Options);
    }

    /// <summary>Saglayiciya ozgu davranislarin kapisi.</summary>
    private SqlDialect Dialect => _context.Dialect;

    /// <inheritdoc />
    public override IReadOnlyList<string> StateKeys => [SessionStateKey];

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var conversationId = GetConversationId(context.Session);

        var command = CreateCommand(_sql.SelectConversationItems);
        DbHelpers.Add(command, "conversation_id", conversationId);
        DbHelpers.Add(command, "tenant_id", _tenantContext.TenantId);

        var messages = await DbHelpers
            .ReadListAsync(command, static reader => reader.GetString(0), cancellationToken)
            .ConfigureAwait(false);

        var history = new List<ChatMessage>(messages.Count);

        foreach (var json in messages)
        {
            if (JsonSerializer.Deserialize(json, AgentPrismJsonContext.Default.ChatMessage) is { } message)
            {
                history.Add(message);
            }
        }

        return history;
    }

    /// <inheritdoc />
    protected override async ValueTask StoreChatHistoryAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<ChatMessage>(context.RequestMessages);

        if (context.ResponseMessages is { } responses)
        {
            messages.AddRange(responses);
        }

        if (messages.Count == 0)
        {
            return;
        }

        var conversationId = GetConversationId(context.Session);
        var agentName = context.Agent.Name ?? context.Agent.Id;
        var now = DateTimeOffset.UtcNow;

        var connection = await _context.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                // Upsert konusma satirini kilitler; ayni konusmaya es zamanli yazan
                // ikinci islem sira numarasini okumak icin bu islemin bitmesini bekler.
                var upsert = _context.CreateCommand(_sql.UpsertConversation, connection, transaction);

                DbHelpers.Add(upsert, "id", conversationId);
                DbHelpers.Add(upsert, "tenant_id", _tenantContext.TenantId);
                DbHelpers.Add(upsert, "agent_name", agentName);
                Dialect.AddTimestamp(upsert, "now", now);

                await DbHelpers.ExecuteAsync(upsert, cancellationToken).ConfigureAwait(false);

                var nextSequenceCommand = _context.CreateCommand(_sql.SelectNextConversationSequence, connection, transaction);

                DbHelpers.Add(nextSequenceCommand, "conversation_id", conversationId);

                var scalar = await DbHelpers
                    .ExecuteScalarAsync(nextSequenceCommand, cancellationToken)
                    .ConfigureAwait(false);

                var sequence = scalar as long? ?? 0L;

                foreach (var message in messages)
                {
                    var insert = _context.CreateCommand(_sql.InsertConversationItem, connection, transaction);

                    DbHelpers.Add(insert, "id", AgentPrismId.NewId());
                    DbHelpers.Add(insert, "conversation_id", conversationId);
                    DbHelpers.Add(insert, "seq", sequence++);
                    // `json`, `jsonb` degil: ChatMessage icerikleri polimorfiktir ve
                    // `$type` ayraci nesnenin ilk ozelligi olmalidir (karar K-027).
                    Dialect.AddJson(insert, "item", JsonSerializer.Serialize(message, AgentPrismJsonContext.Default.ChatMessage));
                    Dialect.AddTimestamp(insert, "created_at", now);

                    await DbHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private DbCommand CreateCommand(string sql) => _context.CreateCommand(sql);

    /// <summary>
    /// Oturumun konusma kimligini getirir; yoksa uretir ve oturuma yazar.
    /// </summary>
    /// <remarks>
    /// Durum her erisimde geri yazilir. Boylece kimlik, oturum serilestirildiginde
    /// kalicilasir ve geri yuklenen oturum ayni konusmaya devam eder.
    /// </remarks>
    private Guid GetConversationId(AgentSession? session)
    {
        if (session is null)
        {
            throw new AgentPrismException(
                "Sohbet gecmisi bir oturum olmadan saklanamaz. " +
                "Agent'i calistirirken bir AgentSession verin.");
        }

        var state = _sessionState.GetOrInitializeState(session);
        _sessionState.SaveState(session, state);

        return state.ConversationId;
    }
}
