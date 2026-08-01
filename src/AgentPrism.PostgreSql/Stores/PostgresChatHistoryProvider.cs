using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

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
public sealed class PostgresChatHistoryProvider : ChatHistoryProvider
{
    /// <summary>
    /// Konusma kimliginin oturum icinde saklandigi durum anahtari.
    /// </summary>
    /// <remarks>
    /// Bu deger <strong>kararlidir</strong>; degistirmek mevcut oturumlarin gecmisini koparir.
    /// </remarks>
    public const string SessionStateKey = "AgentPrism.ChatHistory";

    private readonly NpgsqlDataSource _dataSource;
    private readonly SqlQueries _sql;
    private readonly ITenantContext _tenantContext;
    private readonly int _commandTimeout;
    private readonly ProviderSessionState<ChatHistoryState> _sessionState;

    /// <summary>Yeni bir sohbet gecmisi saglayicisi olusturur.</summary>
    /// <param name="dataSource">Veri kaynagi.</param>
    /// <param name="options">PostgreSQL ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public PostgresChatHistoryProvider(
        NpgsqlDataSource dataSource,
        IOptions<AgentPrismPostgreSqlOptions> options,
        ITenantContext tenantContext)
        : base(
            provideOutputMessageFilter: null,
            storeInputRequestMessageFilter: null,
            storeInputResponseMessageFilter: null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);

        _dataSource = dataSource;
        _sql = new SqlQueries(options.Value.SchemaName);
        _tenantContext = tenantContext;
        _commandTimeout = options.Value.CommandTimeoutSeconds;

        _sessionState = new ProviderSessionState<ChatHistoryState>(
            stateInitializer: static _ => new ChatHistoryState { ConversationId = AgentPrismId.NewId() },
            stateKey: SessionStateKey,
            jsonSerializerOptions: AgentPrismJsonContext.Default.Options);
    }

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
        command.Parameters.AddWithValue("conversation_id", conversationId);
        command.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);

        var messages = await NpgsqlHelpers
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

        var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            await using (transaction.ConfigureAwait(false))
            {
                // Upsert konusma satirini kilitler; ayni konusmaya es zamanli yazan
                // ikinci islem sira numarasini okumak icin bu islemin bitmesini bekler.
                var upsert = new NpgsqlCommand(_sql.UpsertConversation, connection, transaction)
                {
                    CommandTimeout = _commandTimeout,
                };

                upsert.Parameters.AddWithValue("id", conversationId);
                upsert.Parameters.AddWithValue("tenant_id", _tenantContext.TenantId);
                upsert.Parameters.AddWithValue("agent_name", agentName);
                upsert.Parameters.AddWithValue("now", now.UtcDateTime);

                await NpgsqlHelpers.ExecuteAsync(upsert, cancellationToken).ConfigureAwait(false);

                var nextSequenceCommand = new NpgsqlCommand(_sql.SelectNextConversationSequence, connection, transaction)
                {
                    CommandTimeout = _commandTimeout,
                };

                nextSequenceCommand.Parameters.AddWithValue("conversation_id", conversationId);

                var scalar = await NpgsqlHelpers
                    .ExecuteScalarAsync(nextSequenceCommand, cancellationToken)
                    .ConfigureAwait(false);

                var sequence = scalar as long? ?? 0L;

                foreach (var message in messages)
                {
                    var insert = new NpgsqlCommand(_sql.InsertConversationItem, connection, transaction)
                    {
                        CommandTimeout = _commandTimeout,
                    };

                    insert.Parameters.AddWithValue("id", AgentPrismId.NewId());
                    insert.Parameters.AddWithValue("conversation_id", conversationId);
                    insert.Parameters.AddWithValue("seq", sequence++);
                    // `json`, `jsonb` degil: ChatMessage icerikleri polimorfiktir ve
                    // `$type` ayraci nesnenin ilk ozelligi olmalidir (karar K-027).
                    insert.Parameters.Add(new NpgsqlParameter("item", NpgsqlDbType.Json)
                    {
                        Value = JsonSerializer.Serialize(message, AgentPrismJsonContext.Default.ChatMessage),
                    });
                    insert.Parameters.AddWithValue("created_at", now.UtcDateTime);

                    await NpgsqlHelpers.ExecuteAsync(insert, cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private NpgsqlCommand CreateCommand(string sql)
    {
        var command = _dataSource.CreateCommand(sql);
        command.CommandTimeout = _commandTimeout;

        return command;
    }

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
