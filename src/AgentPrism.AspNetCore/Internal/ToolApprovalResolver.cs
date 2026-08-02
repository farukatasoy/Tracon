using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Arayuzden gelen onay kararlarini Microsoft Agent Framework'un bekledigi
/// yanit iceriklerine cevirir ve "bir daha sorma" kurallarini kaydeder.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Onay bir sonraki turun girdisidir.</strong> MAF, bekleyen bir tool
/// cagrisini <c>ToolApprovalRequestContent</c> olarak yanitta dondurur ve karari
/// bir sonraki calistirmanin mesajlarinda <c>ToolApprovalResponseContent</c>
/// olarak bekler. Bu yuzden ayri bir "devam et" ucu yoktur; karar calistirma
/// isteginin govdesinde tasinir.
/// </para>
/// <para>
/// <strong>"Bir daha sorma" MAF'in kendi bicimiyle degil, bizim depomuzda
/// tutulur.</strong> MAF <c>CreateAlwaysApproveToolResponse</c> sunar ancak o
/// karari kalicilastiracak bir yer sunmaz; kural surec bellegiyle sinirli kalir,
/// kiraciya baglanamaz ve arayuzden geri alinamazdi. Kural
/// <see cref="IToolApprovalRuleStore"/> icine yazilir ve sonraki calistirmalarda
/// <see cref="ToolApprovalRuleEvaluator"/> tarafindan uygulanir.
/// Gerekce: <c>docs/KARARLAR.md</c>, karar K-061.
/// </para>
/// </remarks>
internal static class ToolApprovalResolver
{
    /// <summary>
    /// Kararlari oturum gecmisindeki bekleyen isteklerle eslestirir ve
    /// gonderilecek mesaji uretir.
    /// </summary>
    /// <param name="decisions">Arayuzden gelen kararlar.</param>
    /// <param name="agent">Cozulmus agent.</param>
    /// <param name="agentName">Agent adi. Kalici kural bu ada baglanir.</param>
    /// <param name="session">Acik oturum.</param>
    /// <param name="chatHistory">Sohbet gecmisi saglayicisi.</param>
    /// <param name="rules">Kalici kural deposu.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>
    /// Onay yanitlarini tasiyan mesaj; eslesen bekleyen istek yoksa
    /// <see langword="null"/>.
    /// </returns>
    public static async ValueTask<ChatMessage?> BuildResponseMessageAsync(
        IReadOnlyList<ToolApprovalDecision> decisions,
        AIAgent agent,
        string agentName,
        AgentSession session,
        ChatHistoryProvider chatHistory,
        IToolApprovalRuleStore rules,
        ITenantContext tenantContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var history = await ChatHistoryReader
            .ReadAsync(agent, session, chatHistory, cancellationToken)
            .ConfigureAwait(false);

        var pending = CollectPendingRequests(history);

        if (pending.Count == 0)
        {
            return null;
        }

        var contents = new List<AIContent>(decisions.Count);

        foreach (var decision in decisions)
        {
            if (!pending.TryGetValue(decision.RequestId, out var request))
            {
                logger.LogWarning(
                    "'{RequestId}' kimlikli bekleyen bir onay istegi bulunamadi; karar yok sayildi.",
                    decision.RequestId);

                continue;
            }

            contents.Add(request.CreateResponse(decision.Approved, decision.Reason ?? string.Empty));

            if (decision is { Approved: true, Remember: true })
            {
                await RememberAsync(decision, request, agentName, rules, tenantContext, logger, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return contents.Count == 0 ? null : new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>Oturum gecmisindeki yanitlanmamis onay isteklerini toplar.</summary>
    /// <remarks>
    /// Yanitlanmis istekler elenir: ayni istege ikinci bir yanit gondermek
    /// modele celiskili girdi verirdi.
    /// </remarks>
    private static Dictionary<string, ToolApprovalRequestContent> CollectPendingRequests(
        IReadOnlyList<ChatMessage> history)
    {
        var requests = new Dictionary<string, ToolApprovalRequestContent>(StringComparer.Ordinal);
        var answered = new HashSet<string>(StringComparer.Ordinal);

        foreach (var message in history)
        {
            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case ToolApprovalRequestContent request:
                        requests[request.RequestId] = request;
                        break;

                    case ToolApprovalResponseContent response:
                        answered.Add(response.RequestId);
                        break;

                    default:
                        break;
                }
            }
        }

        foreach (var requestId in answered)
        {
            requests.Remove(requestId);
        }

        return requests;
    }

    private static async ValueTask RememberAsync(
        ToolApprovalDecision decision,
        ToolApprovalRequestContent request,
        string agentName,
        IToolApprovalRuleStore rules,
        ITenantContext tenantContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (request.ToolCall is not FunctionCallContent call)
        {
            logger.LogWarning(
                "'{RequestId}' istegi bir fonksiyon cagrisi tasimiyor; kalici onay kurali yazilamadi.",
                decision.RequestId);

            return;
        }

        try
        {
            await rules.AddAsync(
                new ToolApprovalRule
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = tenantContext.TenantId,
                    AgentName = agentName,
                    ToolName = call.Name,
                    ArgumentsHash = decision.RememberArgumentsOnly
                        ? ToolApprovalRuleEvaluator.ComputeArgumentsHash(call.Arguments)
                        : null,
                    CreatedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kural yazilamamasi calistirmayi kesmez: bu turun onayi zaten
            // verildi, yalnizca bir sonraki turda tekrar sorulur.
            logger.LogWarning(
                ex,
                "'{ToolName}' icin kalici onay kurali yazilamadi; onay bir sonraki cagrida yeniden sorulacak.",
                call.Name);
        }
    }
}
