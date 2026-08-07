using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism;

/// <summary>
/// Katalogdaki bir agent'i A2A sunucusuna baglamak icin geciktirilmis (lazy) bir
/// <see cref="AIAgent"/> sarmalayicisi.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddA2AServer(services, AIAgent agent, ...)</c> BIR NESNE ORNEGI ister ve bu
/// cagri <see cref="IServiceProvider"/> daha kurulmadan (<c>Build()</c> ONCESI,
/// <c>UseA2A()</c> icinde) yapilmalidir (bolum 50.5) — ama katalogdaki gercek
/// agent yalniz kurulmus bir kap uzerinden cozulebilir. Bu sinif iki zamanlamayi
/// <see cref="CallableAgentResolver"/>'in kullandigi ayni "gec cozum" deseniyle
/// uzlastirir: <see cref="AttachServices"/> <c>MapAgentPrismA2A()</c> tarafindan
/// (uygulama <c>Build()</c> olduktan sonra) BIR KEZ cagrilir, gercek cozum ise
/// HER cagrida yapilir.
/// </para>
/// <para>
/// <see cref="ChildAgentInvoker"/>'dan FARKLIDIR: o bir agent'in BASKA bir
/// agent'i cagirmasini modeller ve ambient bir UST kapsam (derinlik, butce,
/// kiraci) bekler. Bu sinif ise HER ZAMAN yeni bir KOK calistirma baslatir —
/// dis cagiranin boyle bir ust kapsami yoktur.
/// </para>
/// </remarks>
internal sealed class ExternalAgentProxy : AIAgent
{
    private readonly string _agentName;
    private IServiceProvider? _services;

    /// <summary>Yeni bir gec-cozumlu vekil olusturur.</summary>
    /// <param name="agentName">Katalogdaki hedef agent'in adi.</param>
    public ExternalAgentProxy(string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        _agentName = agentName;
    }

    /// <inheritdoc />
    public override string Name => _agentName;

    /// <summary>
    /// Her cagriya kopyalanacak butce sablonu. Nesnenin kendisi PAYLASILMAZ;
    /// her <c>RunCoreAsync</c> cagrisi kendi <see cref="AgentRunBudget"/>
    /// ornegini uretir.
    /// </summary>
    public AgentRunBudget BudgetTemplate { get; set; } = new() { MaxDepth = 1 };

    /// <summary>
    /// Uygulama <c>Build()</c> olduktan sonra kok servis saglayiciyi baglar.
    /// </summary>
    /// <param name="services">Uygulamanin servis saglayicisi.</param>
    /// <remarks>
    /// <c>MapAgentPrismA2A()</c> tarafindan agent basina BIR KEZ cagrilir. Bu
    /// asamaya kadar cagrilan yol yoktur cunku A2A sunucusu henuz HTTP'ye
    /// baglanmamistir.
    /// </remarks>
    internal void AttachServices(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> CreateSessionCoreAsync(CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<AgentSession> DeserializeSessionCoreAsync(
        JsonElement serializedState,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .DeserializeSessionAsync(serializedState, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async ValueTask<JsonElement> SerializeSessionCoreAsync(
        AgentSession session,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);

        return await agent
            .SerializeSessionAsync(session, jsonSerializerOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var runOptions = CreateRunOptions();

        var response = await agent.RunAsync(messages, session, runOptions, cancellationToken).ConfigureAwait(false);

        RefuseIfApprovalPending(ChildRunApproval.Describe(response.Messages));

        await ExternalCallAudit.WriteAsync(_services!, "a2a", _agentName, runOptions.RunId!.Value, cancellationToken)
            .ConfigureAwait(false);

        return response;
    }

    /// <inheritdoc />
    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var agent = await ResolveAsync(cancellationToken).ConfigureAwait(false);
        var runOptions = CreateRunOptions();
        string? pendingApproval = null;

        await foreach (var update in agent.RunStreamingAsync(messages, session, runOptions, cancellationToken).ConfigureAwait(false))
        {
            pendingApproval ??= ChildRunApproval.Describe(update.Contents);

            yield return update;
        }

        RefuseIfApprovalPending(pendingApproval);

        await ExternalCallAudit.WriteAsync(_services!, "a2a", _agentName, runOptions.RunId!.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private void RefuseIfApprovalPending(string? pending)
    {
        if (pending is null)
        {
            return;
        }

        // Defans katmani: acilis denetimi (ExternalSurfaceGuard) onayli tool
        // tasiyan bir agent'in disa acilmasini zaten engeller, ama tanim
        // SONRADAN guncellenip onayli bir tool eklenebilir. K-103'un ayni
        // sinirinin calisma anindaki ikinci uygulamasi.
        throw new AgentPrismExternalCallException(
            $"'{_agentName}' agent'i tamamlanamadi: '{pending}' tool'u kullanici onayi istiyor. " +
            "Dis cagiran bir agent onay isteğine cevap veremez.")
        {
            AgentName = _agentName,
            Protocol = "a2a",
        };
    }

    private AgentPrismRunOptions CreateRunOptions()
        => new()
        {
            RunId = AgentPrismId.NewId(),

            // Ayni sablon degerleriyle YENI bir butce. Nesnenin kendisini
            // paylasmak, tum A2A cagrilarinin omur boyu tek bir butceyi
            // tuketmesine yol acardi; her dis cagri kendi agacinin koku olmali.
            Budget = new AgentRunBudget
            {
                MaxDepth = BudgetTemplate.MaxDepth,
                MaxTotalTokens = BudgetTemplate.MaxTotalTokens,
                MaxTotalRuns = BudgetTemplate.MaxTotalRuns,
            },
        };

    private async ValueTask<AIAgent> ResolveAsync(CancellationToken cancellationToken)
    {
        var services = _services ?? throw new InvalidOperationException(
            $"'{_agentName}' A2A vekili bir servis saglayiciya baglanmamis. " +
            "MapAgentPrismA2A() cagrisi eksik olabilir.");

        var catalog = services.GetRequiredService<IAgentCatalog>();
        var agent = await catalog.ResolveAsync(_agentName, cancellationToken).ConfigureAwait(false);

        return agent ?? throw new AgentPrismExternalCallException($"'{_agentName}' adinda bir agent katalogda yok.")
        {
            AgentName = _agentName,
            Protocol = "a2a",
        };
    }
}
