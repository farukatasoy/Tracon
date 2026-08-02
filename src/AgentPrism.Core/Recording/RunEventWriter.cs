using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Tek bir calistirmanin olaylarini <see cref="IRunStore"/> icine yazar ve
/// sira numarasini uretir.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Sira numarasi tek bir yazicidan uretilir.</strong> Bu, olay sirasinin
/// deterministik olmasini saglar ve canli akis ile yeniden oynatmanin ayni
/// sonucu vermesini garanti eder.
/// </para>
/// <para>
/// <strong>Depo hatalari calistirmayi kesmez.</strong> Gozlemlenebilirlik,
/// islevselligi bozmamalidir. Her yazma hatasi loglanir ve yutulur; yazici
/// bir kez hata aldiktan sonra <see cref="IsDisabled"/> durumuna gecer ve
/// o calistirma icin daha fazla yazma denemez.
/// </para>
/// </remarks>
public sealed class RunEventWriter
{
    private readonly IRunStore _store;
    private readonly AgentPrismRunRecordingOptions _options;
    private readonly ILogger _logger;
    private long _sequence;

    /// <summary>Yeni bir yazici olusturur.</summary>
    /// <param name="store">Olaylarin yazilacagi depo.</param>
    /// <param name="options">Kayit ayrinti ayarlari.</param>
    /// <param name="logger">Yazma hatalarinin bildirilecegi gunlukleyici.</param>
    /// <param name="runId">Calistirma kimligi.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunEventWriter(IRunStore store, AgentPrismRunRecordingOptions options, ILogger logger, Guid runId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _store = store;
        _options = options;
        _logger = logger;
        RunId = runId;
    }

    /// <summary>Bu yazicinin yazdigi calistirmanin kimligi.</summary>
    public Guid RunId { get; }

    /// <summary>
    /// Yazici bir depo hatasi aldigi icin devre disi kaldi mi.
    /// Devre disi bir yazici sessizce hicbir sey yapmaz.
    /// </summary>
    public bool IsDisabled { get; private set; }

    /// <summary>Yazilmis olay sayisi.</summary>
    public long EventCount => Interlocked.Read(ref _sequence);

    /// <summary>Calistirma kaydini acar ve ilk olayi yazar.</summary>
    /// <param name="info">Baslangic bilgileri.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public async ValueTask StartAsync(RunStartInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);

        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.StartRunAsync(info, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable(ex, "calistirma kaydi acilamadi");
            return;
        }

        await AppendAsync(new RunEventDraft(RunEventType.RunStarted), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Akisa bir olay ekler ve sira numarasini atar.</summary>
    /// <param name="draft">Olay taslagi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Sira numarasi ve zaman damgasi atanmis olay. Depo hata verse veya yazici
    /// devre disi olsa bile olay <strong>uretilir</strong>.
    /// </returns>
    /// <remarks>
    /// Olayin geri dondurulmesi workflow calistirmasi icindir: akisli uc, ayni
    /// olayi hem depoya yazip hem istemciye gondermek zorundadir ve ikinci bir
    /// kez kurmak sira numarasini ikiye bolerdi. Devre disi bir yazicida da
    /// deger donmesi bilinclidir - gozlemlenebilirligin kapanmasi, istemciye
    /// akan yaniti kesmemelidir.
    /// </remarks>
    public async ValueTask<RunEvent> AppendAsync(RunEventDraft draft, CancellationToken cancellationToken = default)
    {
        var runEvent = new RunEvent
        {
            RunId = RunId,
            Sequence = Interlocked.Increment(ref _sequence) - 1,
            Type = draft.Type,
            Timestamp = DateTimeOffset.UtcNow,
            Text = Truncate(draft.Text),
            ToolName = draft.ToolName,
            ToolCallId = draft.ToolCallId,
            Payload = _options.RecordToolPayloads ? Truncate(draft.Payload) : null,
        };

        if (IsDisabled)
        {
            return runEvent;
        }

        try
        {
            await _store.AppendEventAsync(runEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable(ex, "calistirma olayi yazilamadi");
        }

        return runEvent;
    }

    /// <summary>
    /// Sonuclanmis bir tool cagrisini kaydeder.
    /// </summary>
    /// <param name="invocation">Cagri ozeti.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    /// <remarks>
    /// Olay akisindan ayridir: olaylar cagriyi <em>anlatir</em>, bu kayit onu
    /// <em>olcer</em>. Hatasi da olay yazimiyla ayni sekilde yutulur.
    /// </remarks>
    public async ValueTask RecordToolInvocationAsync(
        ToolInvocationRecord invocation,
        CancellationToken cancellationToken = default)
    {
        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.RecordToolInvocationAsync(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable(ex, "tool cagrisi kaydedilemedi");
        }
    }

    /// <summary>Calistirmayi sonlandirir.</summary>
    /// <param name="status">Son durum.</param>
    /// <param name="usage">Token kullanimi.</param>
    /// <param name="error">Hata bilgisi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public async ValueTask CompleteAsync(
        RunStatus status,
        RunUsage? usage = null,
        RunError? error = null,
        CancellationToken cancellationToken = default)
    {
        if (IsDisabled)
        {
            return;
        }

        var closingEvent = status switch
        {
            RunStatus.Completed => new RunEventDraft(RunEventType.RunCompleted),
            RunStatus.Failed => new RunEventDraft(RunEventType.RunFailed) { Text = error?.Message },
            _ => new RunEventDraft(RunEventType.RunFailed) { Text = "Calistirma iptal edildi." },
        };

        await AppendAsync(closingEvent, cancellationToken).ConfigureAwait(false);

        if (IsDisabled)
        {
            return;
        }

        try
        {
            await _store.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = RunId,
                    Status = status,
                    CompletedAt = DateTimeOffset.UtcNow,
                    EventCount = EventCount,
                    Usage = usage,
                    Error = error,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable(ex, "calistirma kaydi kapatilamadi");
        }
    }

    private string? Truncate(string? value)
    {
        if (value is null || _options.MaxPayloadLength <= 0 || value.Length <= _options.MaxPayloadLength)
        {
            return value;
        }

        return string.Concat(value.AsSpan(0, _options.MaxPayloadLength), "…[kirpildi]");
    }

    private void Disable(Exception exception, string what)
    {
        IsDisabled = true;

        _logger.LogWarning(
            exception,
            "AgentPrism calistirma kaydi devre disi birakildi ({Reason}). Calistirma {RunId} normal sekilde devam ediyor.",
            what,
            RunId);
    }
}

/// <summary>
/// Sira numarasi ve zaman damgasi atanmadan once bir olayin tasidigi bilgiler.
/// </summary>
/// <param name="Type">Olay tipi.</param>
public readonly record struct RunEventDraft(RunEventType Type)
{
    /// <summary>Metin icerik.</summary>
    public string? Text { get; init; }

    /// <summary>Tool adi.</summary>
    public string? ToolName { get; init; }

    /// <summary>Tool cagri kimligi.</summary>
    public string? ToolCallId { get; init; }

    /// <summary>Serbest JSON yuku.</summary>
    public string? Payload { get; init; }
}
