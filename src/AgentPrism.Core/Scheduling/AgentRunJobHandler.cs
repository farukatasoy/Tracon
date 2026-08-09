using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// <see cref="JobKind.AgentRun"/> islerini yurutur: kuyruga alinmis
/// (<c>Prefer: respond-async</c>, Faz 46) tek bir agent calistirmasini
/// kuyruktan kosar.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AgentBatchJobHandler"/> ile ayni desen izlenir: agent
/// <see cref="IAgentCatalog.ResolveAsync(string, CancellationToken)"/> ile
/// cozulur, cozulen agent zaten kayit dekoratoruyle sarilidir, calistirma
/// normal bir <c>runs</c> satiri olarak <see cref="IRunStore"/>'a yazilir.
/// </para>
/// <para>
/// 🚨 Kimlik, HTTP katmaninin istemciye <c>Location</c> ile bildirdigi kimlikle
/// AYNI verilir (<see cref="AgentPrismRunOptions.RunId"/>): boylece <c>202</c>
/// yanitinin sozu, is kuyruktan gercekten kosulduktan sonra da GECERLI kalir.
/// Kira dolup is yeniden kiralanirsa (yeniden deneme) bu metot AYNI kimlikle
/// ikinci kez cagrilir; depo <c>StartRunAsync</c>'i bir UPSERT olarak ele alir
/// (bkz. <c>RunStartInfo.Status</c>) — yeni bir <c>runs</c> satiri ACILMAZ.
/// </para>
/// <para>
/// 🚨 Cagri <c>SuspendOnApproval = true</c> verir (Faz 55): yanit onay bekleyen
/// bir tool cagrisi tasirsa <see cref="RunRecordingAgent"/> calistirmayi
/// <see cref="RunStatus.AwaitingApproval"/> ile kapatir (senkron yoldaki
/// <see cref="RunStatus.Completed"/> davranisindan BILEREK farklidir — burada
/// canli bir istemci yoktur). Bu isleyici o durumda her istek icin bir
/// <see cref="PendingApproval"/> satiri yazar; karar
/// <c>POST /api/approvals/{id}/decide</c> ile verilir ve YENI bir calistirma
/// kuyruga dusurur (bkz. <c>ApprovalResumeJobHandler</c>).
/// </para>
/// </remarks>
internal sealed class AgentRunJobHandler(
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    IRunStore runStore,
    IPendingApprovalStore approvalStore,
    IOptions<AgentPrismOptions> options,
    IOptionsMonitor<AgentPrismApprovalOptions>? approvalOptionsMonitor = null,
    IWebhookPublisher? webhookPublisher = null,
    TimeProvider? timeProvider = null,
    ILogger<AgentRunJobHandler>? logger = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public JobKind Kind => JobKind.AgentRun;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (runId, message, sessionId) = ParsePayload(context.Job.Payload);

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession? session;

        try
        {
            agent = await catalog.ResolveAsync(context.Job.TargetName, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"'{context.Job.TargetName}' adinda bir agent bulunamadi. Is basarisiz olarak isaretlenecek.");

            session = string.IsNullOrWhiteSpace(sessionId)
                ? null
                : await sessions.GetOrCreateSessionAsync(agent, sessionId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 🚨 Agent hic cozulemedi: RunRecordingAgent hic devreye girmedi, bu
            // yuzden 202'nin sozunu verdigi 'runs' satirini (Queued) BURADA
            // Failed'e biz kapatmaliyiz -- aksi halde istemci GET /api/runs/{id}
            // ile sonsuza dek 'Queued' gorur. Gercek calistirma hatalari (agent
            // cozuldukten SONRA) bu bloga girmez; RunRecordingAgent onlari
            // zaten Failed olarak kapatir (bkz. RunRecordingAgent.CompleteAsync).
            await FailQueuedRunAsync(runId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        List<ChatMessage> messages = [new(ChatRole.User, message)];

        var response = await agent.RunAsync(
            messages,
            session,
            new AgentPrismRunOptions { RunId = runId, SuspendOnApproval = true },
            cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
        }

        var pendingRequests = CollectPendingApprovalRequests(response.Messages);

        if (pendingRequests.Count == 0)
        {
            return;
        }

        if (session is null)
        {
            // Onay bir sonraki turun girdisidir ve YALNIZ oturum gecmisinden
            // cozulebilir; oturumsuz bir calistirmada bekleyen istek asla
            // yanitlanamaz. Senkron yolun ayni kisiti icin bkz. AgentEndpoints.RunAsync
            // ("Onay icin oturum gerekli"). RunRecordingAgent bu calistirmayi
            // zaten AwaitingApproval ile kapatmisti; burada Failed'e DUZELTILIR.
            await FailQueuedRunAsync(
                runId,
                context.Job.TenantId,
                new AgentPrismException(
                    "Kuyruga alinan calistirma onay istedi ama 'sessionId' verilmemisti; " +
                    "onay bir sonraki turun girdisidir ve oturumsuz cozulemez."),
                cancellationToken).ConfigureAwait(false);

            return;
        }

        var expiration = approvalOptionsMonitor?.CurrentValue.DefaultExpiration ?? TimeSpan.FromHours(24);
        var now = _clock.GetUtcNow();

        foreach (var request in pendingRequests)
        {
            var approval = new PendingApproval
            {
                Id = AgentPrismId.NewId(),
                TenantId = context.Job.TenantId,
                RunId = runId,
                SessionId = sessionId!,
                RequestId = request.RequestId,
                ToolName = request.ToolCall is FunctionCallContent call ? call.Name : "unknown",
                Arguments = options.Value.RunRecording.RecordToolPayloads && request.ToolCall is FunctionCallContent argsCall
                    ? FormatArguments(argsCall)
                    : null,
                Status = ApprovalStatus.Pending,
                ExpiresAt = now + expiration,
                CreatedAt = now,
            };

            await approvalStore.CreateAsync(approval, cancellationToken).ConfigureAwait(false);

            if (webhookPublisher is not null)
            {
                await webhookPublisher.PublishAsync(
                    context.Job.TenantId,
                    WebhookEvents.ApprovalPending,
                    new WebhookEventPayload
                    {
                        OccurredAt = now,
                        Approval = new WebhookApprovalSummary
                        {
                            RequestId = approval.Id.ToString(),
                            RunId = approval.RunId.ToString(),
                            ToolName = approval.ToolName,
                        },
                    },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static List<ToolApprovalRequestContent> CollectPendingApprovalRequests(
        IEnumerable<ChatMessage> messages)
    {
        List<ToolApprovalRequestContent>? requests = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is ToolApprovalRequestContent request)
                {
                    (requests ??= []).Add(request);
                }
            }
        }

        return requests ?? [];
    }

    private static string? FormatArguments(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return null;
        }

        // RunRecordingAgent.FormatArguments ile AYNI gerekce: AOT uyumlu
        // kalmak icin elle bicimlendirilir, yansimaya dayanan JSON
        // serilestirme kullanilmaz.
        return string.Join(", ", call.Arguments.Select(static pair => $"{pair.Key}={pair.Value}"));
    }

    private async ValueTask FailQueuedRunAsync(
        Guid runId,
        string tenantId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        try
        {
            await runStore.CompleteRunAsync(
                new RunCompletion
                {
                    RunId = runId,
                    Status = RunStatus.Failed,
                    CompletedAt = _clock.GetUtcNow(),
                    Error = new RunError
                    {
                        Type = exception.GetType().Name,
                        Message = exception.Message,
                    },

                    // Is'in kendi kiracisi; kuyruga dusuren taraf yazmisti (K-355).
                    TenantId = tenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception storeException) when (storeException is not OperationCanceledException)
        {
            if (logger is not null && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning(
                    storeException,
                    "Kuyruktaki calistirma Failed durumuna kapatilamadi: {RunId}.",
                    runId);
            }
        }
    }

    private static (Guid RunId, string Message, string? SessionId) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("runId", out var runIdElement) ||
            runIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(runIdElement.GetString(), out var runId))
        {
            throw new AgentPrismException("AgentRun isi yuku gecerli bir 'runId' alani tasimalidir.");
        }

        if (!payload.TryGetProperty("message", out var messageElement) ||
            messageElement.ValueKind != JsonValueKind.String ||
            messageElement.GetString() is not { Length: > 0 } message)
        {
            throw new AgentPrismException("AgentRun isi yuku gecerli bir 'message' alani tasimalidir.");
        }

        var sessionId = payload.TryGetProperty("sessionId", out var sessionElement) &&
            sessionElement.ValueKind == JsonValueKind.String
            ? sessionElement.GetString()
            : null;

        return (runId, message, sessionId);
    }
}
