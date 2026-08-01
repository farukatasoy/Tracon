using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Sardigi agent'in her calistirmasini <see cref="IRunStore"/> icine olay olarak yazar.
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
/// </remarks>
public sealed class RunRecordingAgent : DelegatingAIAgent
{
    private readonly IRunStore _runStore;
    private readonly ITenantContext _tenantContext;
    private readonly AgentPrismRunRecordingOptions _options;
    private readonly ILogger<RunRecordingAgent> _logger;

    /// <summary>Yeni bir kayit sarmalayicisi olusturur.</summary>
    /// <param name="innerAgent">Sarmalanan agent.</param>
    /// <param name="runStore">Olaylarin yazilacagi depo.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="options">Kayit ayrinti ayarlari.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public RunRecordingAgent(
        AIAgent innerAgent,
        IRunStore runStore,
        ITenantContext tenantContext,
        AgentPrismRunRecordingOptions options,
        ILogger<RunRecordingAgent> logger)
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

        var writer = await BeginRunAsync(session, isStreaming: false, cancellationToken).ConfigureAwait(false);

        try
        {
            var response = await base.RunCoreAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

            foreach (var message in response.Messages)
            {
                await WriteContentsAsync(writer, message.Contents, cancellationToken).ConfigureAwait(false);
            }

            await writer.AppendAsync(
                new RunEventDraft(RunEventType.MessageCompleted) { Text = response.Text },
                cancellationToken).ConfigureAwait(false);

            await writer.CompleteAsync(
                RunStatus.Completed,
                ToRunUsage(response.Usage),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return response;
        }
        catch (OperationCanceledException)
        {
            await writer.CompleteAsync(RunStatus.Canceled, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await writer.CompleteAsync(RunStatus.Failed, error: ToRunError(ex), cancellationToken: CancellationToken.None).ConfigureAwait(false);
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

        var writer = await BeginRunAsync(session, isStreaming: true, cancellationToken).ConfigureAwait(false);
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
                    await writer.CompleteAsync(RunStatus.Canceled, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
                catch (Exception ex)
                {
                    await writer.CompleteAsync(RunStatus.Failed, error: ToRunError(ex), cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usageContent)
                    {
                        usage = usageContent.Details;
                    }
                }

                await WriteContentsAsync(writer, update.Contents, cancellationToken).ConfigureAwait(false);

                yield return update;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        await writer.CompleteAsync(RunStatus.Completed, ToRunUsage(usage), cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<RunEventWriter> BeginRunAsync(
        AgentSession? session,
        bool isStreaming,
        CancellationToken cancellationToken)
    {
        var writer = new RunEventWriter(_runStore, _options, _logger, AgentPrismId.NewId());

        await writer.StartAsync(
            new RunStartInfo
            {
                RunId = writer.RunId,
                AgentName = Name ?? InnerAgent.Id,
                StartedAt = DateTimeOffset.UtcNow,
                TenantId = _tenantContext.TenantId,
                SessionId = session is null ? null : GetSessionId(session),
                IsStreaming = isStreaming,
            },
            cancellationToken).ConfigureAwait(false);

        return writer;
    }

    private async ValueTask WriteContentsAsync(
        RunEventWriter writer,
        IEnumerable<AIContent> contents,
        CancellationToken cancellationToken)
    {
        foreach (var content in contents)
        {
            switch (content)
            {
                case TextContent text when _options.RecordMessageDeltas && !string.IsNullOrEmpty(text.Text):
                    await writer.AppendAsync(
                        new RunEventDraft(RunEventType.MessageDelta) { Text = text.Text },
                        cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionCallContent call:
                    await writer.AppendAsync(
                        new RunEventDraft(RunEventType.ToolInvoking)
                        {
                            ToolName = call.Name,
                            ToolCallId = call.CallId,
                            Payload = FormatArguments(call),
                        },
                        cancellationToken).ConfigureAwait(false);
                    break;

                case FunctionResultContent result:
                    await writer.AppendAsync(
                        new RunEventDraft(result.Exception is null ? RunEventType.ToolInvoked : RunEventType.ToolFailed)
                        {
                            ToolCallId = result.CallId,
                            Text = result.Exception?.Message,
                            Payload = result.Result?.ToString(),
                        },
                        cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    break;
            }
        }
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
}
