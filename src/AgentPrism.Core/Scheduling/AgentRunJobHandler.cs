using System.Text.Json;
using Microsoft.Extensions.Logging;

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
/// </remarks>
internal sealed class AgentRunJobHandler(
    IAgentCatalog catalog,
    AgentSessionManager sessions,
    IRunStore runStore,
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
            await FailQueuedRunAsync(runId, exception, cancellationToken).ConfigureAwait(false);

            throw;
        }

        List<Microsoft.Extensions.AI.ChatMessage> messages =
        [
            new(Microsoft.Extensions.AI.ChatRole.User, message),
        ];

        await agent.RunAsync(
            messages,
            session,
            new AgentPrismRunOptions { RunId = runId },
            cancellationToken).ConfigureAwait(false);

        if (session is not null)
        {
            await sessions.SaveSessionAsync(agent, session, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask FailQueuedRunAsync(Guid runId, Exception exception, CancellationToken cancellationToken)
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
