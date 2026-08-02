using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Sardigi agent'in her calistirmasini <see cref="IRunStore"/> icine olay olarak
/// yazar, metriklerini yayar ve span'lerini toplar.
/// </summary>
/// <remarks>
/// <para>
/// Bu bir Microsoft Agent Framework middleware'i <em>degil</em>, bir
/// <see cref="DelegatingAIAgent"/> sarmalayicisidir. Sebep: MAF middleware zinciri
/// agent'a ozgudur ve <c>HarnessAgent</c> kendi ic dekoratorlerini ekler.
/// Dis sarmalayici, harness dahil <strong>her</strong> agent tipinde ayni sekilde calisir.
/// </para>
/// <para>
/// Tool cagrilari, MAF'in urettigi <see cref="FunctionCallContent"/> ve
/// <see cref="FunctionResultContent"/> iceriklerinden okunur; ayri bir kanca gerekmez.
/// </para>
/// <para>
/// <strong>Kok span burada baslar.</strong> Sarmalayici en distaki dekoratordur
/// (<c>Order = 0</c>), bu yuzden acilan <c>agentprism.run</c> span'i ic
/// sarmalayicilarin ve model cagrilarinin span'lerini cocuk olarak toplar.
/// Calistirma kimligi ile trace kimligi ancak boyle birbirine baglanabilir.
/// </para>
/// </remarks>
public sealed class RunRecordingAgent : DelegatingAIAgent
{
    private static readonly ActivitySource ActivitySource = new(AgentPrismDiagnostics.ActivitySourceName);

    private readonly IRunStore _runStore;
    private readonly ITenantContext _tenantContext;
    private readonly AgentPrismRunRecordingOptions _options;
    private readonly ILogger<RunRecordingAgent> _logger;
    private readonly AgentPrismMetrics? _metrics;
    private readonly RunTraceCollector? _traceCollector;
    private readonly TimeProvider _timeProvider;
    private readonly string? _modelId;

    /// <summary>Yeni bir kayit sarmalayicisi olusturur.</summary>
    /// <param name="innerAgent">Sarmalanan agent.</param>
    /// <param name="runStore">Olaylarin yazilacagi depo.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">Kayit ayrinti ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="metrics">Metrik aletleri. <see langword="null"/> ise metrik yayilmaz.</param>
    /// <param name="traceCollector">Span toplayici. <see langword="null"/> ise span yazilmaz.</param>
    /// <param name="modelId">Agent'in bagli oldugu model. Bilinmiyorsa <see langword="null"/>.</param>
    /// <param name="timeProvider">Zaman kaynagi. <see langword="null"/> ise sistem saati kullanilir.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunRecordingAgent(
        AIAgent innerAgent,
        IRunStore runStore,
        ITenantContext tenantContext,
        AgentPrismRunRecordingOptions options,
        ILogger<RunRecordingAgent> logger,
        AgentPrismMetrics? metrics = null,
        RunTraceCollector? traceCollector = null,
        string? modelId = null,
        TimeProvider? timeProvider = null)
        : base(innerAgent)
    {
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _runStore = runStore;
        _tenantContext = tenantContext;
        _options = options;
        _logger = logger;
        _metrics = metrics;
        _traceCollector = traceCollector;
        _modelId = modelId;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);
        }

        // 🚨 Kok span BU METODUN GOVDESINDE baslatilmalidir. Activity.Current bir
        // AsyncLocal'dir; bir async yardimci metodun icinde yapilan atama cagirana
        // GERI AKMAZ. Olculdu: span yardimci metotta acildiginda ic span'ler
        // (invoke_agent, chat) kok span'in cocugu degil, kardesi oluyordu.
        var start = PrepareRun(session, options, isStreaming: false);
        var scope = await BeginRunAsync(start, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                await WriteContentsAsync(scope, message.Contents, cancellationToken).ConfigureAwait(false);
            }

            await scope.Writer.AppendAsync(
                new RunEventDraft(RunEventType.MessageCompleted) { Text = response.Text },
                cancellationToken).ConfigureAwait(false);

            await CompleteAsync(scope, RunStatus.Completed, ToRunUsage(response.Usage), null, cancellationToken)
                .ConfigureAwait(false);

            return response;
        }
        catch (OperationCanceledException)
        {
            await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex), CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            await foreach (var passthrough in base.RunCoreStreamingAsync(messages, session, options, cancellationToken).ConfigureAwait(false))
            {
                yield return passthrough;
            }

            yield break;
        }

        // Kok span burada baslar; gerekcesi RunCoreAsync icindeki nota bakiniz.
        var start = PrepareRun(session, options, isStreaming: true);
        var scope = await BeginRunAsync(start, cancellationToken).ConfigureAwait(false);
        UsageDetails? usage = null;
        var enumerator = base.RunCoreStreamingAsync(messages, session, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                AgentResponseUpdate update;

                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }

                    update = enumerator.Current;
                }
                catch (OperationCanceledException)
                {
                    await CompleteAsync(scope, RunStatus.Canceled, null, null, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await CompleteAsync(scope, RunStatus.Failed, null, ToRunError(ex), CancellationToken.None)
                        .ConfigureAwait(false);
                    throw;
                }

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        usage = usageContent.Details;
                    }
                }

                await WriteContentsAsync(scope, update.Contents, cancellationToken).ConfigureAwait(false);

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        await CompleteAsync(scope, RunStatus.Completed, ToRunUsage(usage), null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calistirmanin kimligini cozer ve kok span'i acar.
    /// </summary>
    /// <remarks>
    /// <strong>Bu metot es zamanlidir ve oyle kalmalidir.</strong>
    /// <see cref="Activity.Current"/> bir <c>AsyncLocal</c>'dir: bir async metodun
    /// icinde yapilan atama, o metot dondugunde cagirana geri akmaz. Span burada
    /// acilip cagiran metodun kendi govdesinde tutulmazsa, ic sarmalayicilarin
    /// ve model cagrilarinin span'leri kok span'in <em>cocugu</em> degil
    /// <em>kardesi</em> olur ve waterfall gorunumu yanlis bir hiyerarsi cizer.
    /// </remarks>
    private RunStart PrepareRun(AgentSession? session, AgentRunOptions? options, bool isStreaming)
    {
        // Cagiran kimligi verdiyse o kullanilir. Akisli bir uc, ilk cerceveyi
        // yazmadan once kimligi bilmek zorundadir; kendi urettigi kimligi buraya
        // gecerek istemciye dogru kimligi bildirebilir.
        var runId = (options as AgentPrismRunOptions)?.RunId ?? AgentPrismId.NewId();
        var agentName = Name ?? InnerAgent.Id;
        var tenantId = _tenantContext.TenantId;
        var sessionId = session is null ? null : GetSessionId(session);

        var activity = ActivitySource.StartActivity(AgentPrismDiagnostics.RunActivityName, ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag(AgentPrismDiagnostics.Tags.RunId, runId);
            activity.SetTag(AgentPrismDiagnostics.Tags.AgentName, agentName);
            activity.SetTag(AgentPrismDiagnostics.Tags.TenantId, tenantId);
            activity.SetTag(AgentPrismDiagnostics.Tags.Streaming, isStreaming);

            if (sessionId is not null)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.SessionId, sessionId);
            }

            if (_modelId is not null)
            {
                activity.SetTag(AgentPrismDiagnostics.Tags.ModelId, _modelId);
            }

            _traceCollector?.BeginRun(activity.TraceId.ToString());
        }

        return new RunStart(runId, agentName, tenantId, sessionId, isStreaming, activity);
    }

    private async ValueTask<RunScope> BeginRunAsync(RunStart start, CancellationToken cancellationToken)
    {
        var writer = new RunEventWriter(_runStore, _options, _logger, start.RunId);

        await writer.StartAsync(
            new RunStartInfo
            {
                RunId = start.RunId,
                AgentName = start.AgentName,
                StartedAt = _timeProvider.GetUtcNow(),
                TenantId = start.TenantId,
                SessionId = start.SessionId,
                ModelId = _modelId,
                IsStreaming = start.IsStreaming,
            },
            cancellationToken).ConfigureAwait(false);

        return new RunScope(
            writer,
            start.Activity,
            new ToolInvocationTracker(start.RunId, start.IsStreaming, _timeProvider),
            start.AgentName,
            start.TenantId,
            _timeProvider.GetTimestamp());
    }

    private async ValueTask CompleteAsync(
        RunScope scope,
        RunStatus status,
        RunUsage? usage,
        RunError? error,
        CancellationToken cancellationToken)
    {
        // Sonuc gelmeden biten cagrilar acikca kapatilir; aksi halde arayuzde
        // "basladi ama bitmedi" gorunen bir tool karti kalirdi.
        foreach (var unfinished in scope.Tools.DrainUnfinished("Calistirma tool sonucu gelmeden sonlandi."))
        {
            await scope.Writer.RecordToolInvocationAsync(unfinished, cancellationToken).ConfigureAwait(false);
            _metrics?.RecordToolInvocation(unfinished.ToolName, succeeded: false, unfinished.Duration);
        }

        await scope.Writer.CompleteAsync(status, usage, error, cancellationToken).ConfigureAwait(false);

        var elapsed = _timeProvider.GetElapsedTime(scope.StartedAt);

        _metrics?.RecordRun(scope.AgentName, status, scope.TenantId, _modelId, elapsed, usage);

        if (scope.Activity is null)
        {
            return;
        }

        scope.Activity.SetTag(AgentPrismDiagnostics.Tags.Status, status.ToString());

        if (error is not null)
        {
            scope.Activity.SetStatus(ActivityStatusCode.Error, error.Message);
        }

        // Span toplayiciya gecmeden ONCE durdurulur: durdurma ActivityStopped
        // olayini tetikler ve kok span'in kendisi de tampona girer.
        scope.Activity.Stop();

        if (_traceCollector is not null)
        {
            await _traceCollector.CompleteRunAsync(
                scope.Activity.TraceId.ToString(),
                scope.Writer.RunId,
                scope.TenantId,
                status,
                cancellationToken).ConfigureAwait(false);
        }

        scope.Activity.Dispose();
    }

    private async ValueTask WriteContentsAsync(
        RunScope scope,
        IEnumerable<AIContent> contents,
        CancellationToken cancellationToken)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when _options.RecordMessageDeltas && !string.IsNullOrEmpty(text.Text):
                    await scope.Writer.AppendAsync(
                        new RunEventDraft(RunEventType.MessageDelta) { Text = text.Text },
                        cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionCallContent call:
                    await WriteToolCallAsync(scope, call, cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionResultContent result:
                    await WriteToolResultAsync(scope, result, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    break;
            }
        }
    }

    private static async ValueTask WriteToolCallAsync(
        RunScope scope,
        FunctionCallContent call,
        CancellationToken cancellationToken)
    {
        var arguments = FormatArguments(call);

        scope.Tools.OnCall(call, source: null, arguments);

        await scope.Writer.AppendAsync(
            new RunEventDraft(RunEventType.ToolInvoking)
            {
                ToolName = call.Name,
                ToolCallId = call.CallId,
                Payload = arguments,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WriteToolResultAsync(
        RunScope scope,
        FunctionResultContent result,
        CancellationToken cancellationToken)
    {
        var record = scope.Tools.OnResult(result);

        await scope.Writer.AppendAsync(
            new RunEventDraft(result.Exception is null ? RunEventType.ToolInvoked : RunEventType.ToolFailed)
            {
                ToolName = record.ToolName,
                ToolCallId = result.CallId,
                Text = result.Exception?.Message,
                Payload = result.Result?.ToString(),
            },
            cancellationToken).ConfigureAwait(false);

        await scope.Writer.RecordToolInvocationAsync(record, cancellationToken).ConfigureAwait(false);

        _metrics?.RecordToolInvocation(record.ToolName, record.Succeeded, record.Duration);
    }

    private static string? FormatArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return null;
        }

        // Argumanlari AOT uyumlu kalmak icin elle bicimlendiriyoruz;
        // yansimaya dayanan JSON serilestirme kullanilmiyor.
        return string.Join(", ", call.Arguments.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    // Oturum kimligi AgentSessionManager tarafindan oturuma damgalanir. Damga yoksa
    // oturum AgentPrism disinda acilmis demektir; kayda yer tutucu bir deger yazmak
    // yerine bos birakilir.
    private static string? GetSessionId(AgentSession session) => AgentSessionIdentity.GetId(session);

    private static RunUsage? ToRunUsage(UsageDetails? usage)
        => usage is null
            ? null
            : new RunUsage
            {
                InputTokens = usage.InputTokenCount,
                OutputTokens = usage.OutputTokenCount,
                TotalTokens = usage.TotalTokenCount,
            };

    private static RunError ToRunError(Exception exception)
        => new()
        {
            Type = exception.GetType().FullName ?? exception.GetType().Name,
            Message = exception.Message,
        };

    /// <summary>Kok span acildiktan sonra, depo yazimindan once bilinenler.</summary>
    private sealed record RunStart(
        Guid RunId,
        string AgentName,
        string TenantId,
        string? SessionId,
        bool IsStreaming,
        Activity? Activity);

    /// <summary>Tek bir calistirmanin kayit durumu.</summary>
    private sealed record RunScope(
        RunEventWriter Writer,
        Activity? Activity,
        ToolInvocationTracker Tools,
        string AgentName,
        string TenantId,
        long StartedAt);
}
