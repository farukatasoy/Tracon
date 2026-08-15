using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// <see cref="JobKind.ApprovalResume"/> islerini yurutur: kararlanmis bir
/// <see cref="PendingApproval"/> icin oturum gecmisindeki bekleyen tool
/// onay istegini yanitlar ve calistirmayi devam ettirir (Faz 55).
/// </summary>
/// <remarks>
/// <para>
/// Karar (onaylandi/reddedildi, kim, ne zaman) BU isleyiciden ONCE, HTTP
/// katmaninin <c>decide</c> ucunda <see cref="IPendingApprovalStore.DecideAsync"/>
/// ile yazilmis ve denetim izine islenmistir (K-089). Bu isleyici yalniz o
/// KARARI oturuma <c>ToolApprovalResponseContent</c> olarak besler; kendisi
/// hicbir karar VERMEZ ve denetim izine hicbir sey YAZMAZ.
/// </para>
/// <para>
/// 🚨 <c>AgentRunJobHandler</c>'in AKSINE, bu is <strong>yeni</strong> bir
/// <c>RunId</c> ile calisir: eski calistirma <see cref="RunStatus.AwaitingApproval"/>
/// ile kapanmis ve BIR DAHA DEGISMEMISTIR (K-014). Model onaylanmis tool'u
/// kullandiktan SONRA baska bir tool icin onay isterse (ardisik onay), yeni
/// calistirma da (kok oldugu icin) ayni sekilde <c>AwaitingApproval</c>'a
/// kapanir — zincir aynen devam eder.
/// </para>
/// </remarks>
internal sealed class ApprovalResumeJobHandler(
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    IRunStore runStore,
    IPendingApprovalStore approvalStore,
    Microsoft.Agents.AI.ChatHistoryProvider chatHistory,
    TimeProvider? timeProvider = null,
    ILogger<ApprovalResumeJobHandler>? logger = null) : IJobHandler
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    public JobKind Kind => JobKind.ApprovalResume;

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (newRunId, approvalId, agentName) = ParsePayload(context.Job.Payload);

        var approval = await approvalStore.GetAsync(approvalId, cancellationToken).ConfigureAwait(false);

        if (approval is null || approval.Status == ApprovalStatus.Pending)
        {
            await FailQueuedRunAsync(
                newRunId,
                context.Job.TenantId,
                new AgentPrismException(
                    $"'{approvalId}' kimlikli onay istegi bulunamadi veya henuz karara baglanmamis."),
                cancellationToken).ConfigureAwait(false);

            return;
        }

        Microsoft.Agents.AI.AIAgent agent;
        Microsoft.Agents.AI.AgentSession session;
        ChatMessage approvalMessage;

        try
        {
            agent = await catalog.ResolveAsync(agentName, cancellationToken).ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"'{agentName}' adinda bir agent bulunamadi. Is basarisiz olarak isaretlenecek.");

            session = await sessions
                .GetOrCreateSessionAsync(agent, approval.SessionId, cancellationToken)
                .ConfigureAwait(false);

            var request = await FindPendingRequestAsync(agent, session, chatHistory, approval.RequestId, cancellationToken)
                    .ConfigureAwait(false)
                ?? throw new AgentPrismException(
                    $"'{approval.RequestId}' kimlikli tool onay istegi oturum gecmisinde bulunamadi " +
                    "(muhtemelen oturum bu istekten sonra sifirlandi). Karar uygulanamaz.");

            approvalMessage = new ChatMessage(
                ChatRole.User,
                [request.CreateResponse(approval.Status == ApprovalStatus.Approved, reason: string.Empty)]);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Gerekce AgentRunJobHandler ile aynidir: RunRecordingAgent hic
            // devreye girmedi, 'runs' satirini BURADA Failed'e kapatmaliyiz.
            await FailQueuedRunAsync(newRunId, context.Job.TenantId, exception, cancellationToken)
                .ConfigureAwait(false);

            throw;
        }

        await agent.RunAsync(
            [approvalMessage],
            session,
            new AgentPrismRunOptions
            {
                RunId = newRunId,
                SessionId = approval.SessionId,
            },
            cancellationToken).ConfigureAwait(false);

        await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Oturum gecmisinde henuz yanitlanmamis, verilen kimlige sahip onay istegini arar.</summary>
    /// <remarks>
    /// <c>ToolApprovalResolver.CollectPendingRequests</c> (AgentPrism.AspNetCore)
    /// ile AYNI mantik, tek bir istek icin daraltilmis. Onay bir sonraki turun
    /// girdisidir ve istegin KENDISI yalnizca oturum gecmisinde yasar
    /// (<c>ToolApprovalRequestContent.CreateResponse</c>).
    /// </remarks>
    private static async ValueTask<ToolApprovalRequestContent?> FindPendingRequestAsync(
        Microsoft.Agents.AI.AIAgent agent,
        Microsoft.Agents.AI.AgentSession session,
        Microsoft.Agents.AI.ChatHistoryProvider chatHistory,
        string requestId,
        CancellationToken cancellationToken)
    {
        // MAAI001: InvokingContext kurucusu "for evaluation purposes only"
        // isaretlidir. Gerekce ChatHistoryReader ile aynidir: gecmisi okumanin
        // baska public yolu yoktur ve bu cagri yalnizca okur. Kullanim bu TEK
        // dosyada toplanir.
#pragma warning disable MAAI001
        var context = new Microsoft.Agents.AI.ChatHistoryProvider.InvokingContext(agent, session, []);
#pragma warning restore MAAI001

        var messages = await chatHistory.InvokingAsync(context, cancellationToken).ConfigureAwait(false);

        ToolApprovalRequestContent? found = null;

        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent request
                        when string.Equals(request.RequestId, requestId, StringComparison.Ordinal):
                        found = request;
                        break;

                    case ToolApprovalResponseContent response
                        when string.Equals(response.RequestId, requestId, StringComparison.Ordinal):
                        // Bu istek zaten yanitlanmis (ornegin ikinci bir
                        // surdurme denemesi); tekrar yanitlanmaz.
                        found = null;
                        break;
                }
            }
        }

        return found;
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
                    "Surdurulen calistirma Failed durumuna kapatilamadi: {RunId}.",
                    runId);
            }
        }
    }

    private static (Guid RunId, Guid ApprovalId, string AgentName) ParsePayload(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("runId", out var runIdElement) ||
            runIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(runIdElement.GetString(), out var runId))
        {
            throw new AgentPrismException("ApprovalResume isi yuku gecerli bir 'runId' alani tasimalidir.");
        }

        if (!payload.TryGetProperty("approvalId", out var approvalIdElement) ||
            approvalIdElement.ValueKind != JsonValueKind.String ||
            !Guid.TryParse(approvalIdElement.GetString(), out var approvalId))
        {
            throw new AgentPrismException("ApprovalResume isi yuku gecerli bir 'approvalId' alani tasimalidir.");
        }

        if (!payload.TryGetProperty("agentName", out var agentNameElement) ||
            agentNameElement.ValueKind != JsonValueKind.String ||
            agentNameElement.GetString() is not { Length: > 0 } agentName)
        {
            throw new AgentPrismException("ApprovalResume isi yuku gecerli bir 'agentName' alani tasimalidir.");
        }

        return (runId, approvalId, agentName);
    }
}
