using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="WorkflowDefinition"/>'i calistirilabilir bir
/// <see cref="Workflow"/> grafina cevirir.
/// </summary>
/// <remarks>
/// <para>
/// Derleyici <strong>katalogdaki agent'lari</strong> baglar. Her agent
/// <see cref="ChildAgentInvoker"/> ile sarilir; boylece workflow icinde
/// calisan her agent, Faz 12'nin agac mekanizmasiyla workflow calistirmasinin
/// altina baglanir ve derinlik, butce, kiraci sinirlari kendiliginden uygulanir.
/// Bu sarmalayici Faz 12'de yazildi ve burada <em>yeniden kullaniliyor</em>:
/// alt calistirma kurallarini ikinci kez yazmak, ikisinin zamanla ayrismasi
/// demekti.
/// </para>
/// <para>
/// Sarmalayici agent'i <em>gec</em> cozer: bir agent tanimi degistiginde
/// derlenmis workflow bayatlamaz.
/// </para>
/// </remarks>
internal sealed class WorkflowDefinitionCompiler
{
    private readonly CallableAgentResolver _resolver;
    private readonly WorkflowAgentCache _agents;

    /// <summary>Yeni bir derleyici olusturur.</summary>
    /// <param name="resolver">Agent'lari katalogdan cozen cozucu.</param>
    /// <param name="agents">Kimligi kararli agent sarmalayicilarinin onbellegi.</param>
    /// <exception cref="ArgumentNullException">Bagimliliklardan biri <see langword="null"/> ise.</exception>
    public WorkflowDefinitionCompiler(CallableAgentResolver resolver, WorkflowAgentCache agents)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(agents);

        _resolver = resolver;
        _agents = agents;
    }

    /// <summary>Tanimi calistirilabilir bir grafa cevirir.</summary>
    /// <param name="definition">Derlenecek tanim.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Kurulmus graf.</returns>
    /// <exception cref="AgentPrismException">
    /// Tanim gecersizse veya bir agent adi katalogda bulunamiyorsa.
    /// </exception>
    public async ValueTask<Workflow> CompileAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Yapisal denetim AgentPrism.Core'dadir: HTTP katmani da ayni kurallari
        // kayit aninda uygular ve iki yerde ayri ayri yazilsaydi ayrisirlardi.
        WorkflowDefinitionValidator.Require(definition);

        var participants = await BindAsync(definition.Name, definition.AgentNames, cancellationToken)
            .ConfigureAwait(false);

        return definition.Kind switch
        {
            WorkflowKind.Sequential => BuildSequential(definition, participants),
            WorkflowKind.Concurrent => BuildConcurrent(definition, participants),
            WorkflowKind.Handoff => BuildHandoff(definition, participants),
            WorkflowKind.GroupChat => BuildGroupChat(definition, participants),
            WorkflowKind.Magentic => await BuildMagenticAsync(definition, participants, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new AgentPrismException(
                $"'{definition.Name}' workflow'u bilinmeyen bir desen kullaniyor: '{definition.Kind}'."),
        };
    }

    private static Workflow BuildSequential(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
        => AgentWorkflowBuilder.BuildSequential(definition.Name, participants);

    private static Workflow BuildConcurrent(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
        => AgentWorkflowBuilder.BuildConcurrent(definition.Name, participants, aggregator: null);

    private static Workflow BuildHandoff(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
    {
        var builder = AgentWorkflowBuilder
            .CreateHandoffBuilderWith(participants[0])
            .AddParticipants(participants.Skip(1))
            .WithName(definition.Name);

        // Ilk agent digerlerinin HEPSINE devredebilir. Daha dar bir graf
        // (kimden kime) tanimlanabilir olsaydi tanim modeli de kenar listesi
        // tasimak zorunda kalirdi; Faz 15 bunu bilerek yapmiyor ve serbest
        // grafi kod tarafinda birakiyor.
        builder = builder.WithHandoffs(participants[0], participants.Skip(1));

        if (definition.Description is { Length: > 0 } description)
        {
            builder = builder.WithDescription(description);
        }

        if (definition.HandoffInstructions is { Length: > 0 } instructions)
        {
            builder = builder.WithHandoffInstructions(instructions);
        }

        // Faz 15'te insan araya girmez; devretme zinciri kendiliginden ilerler.
        // Tur siniri verilmezse MAF ilk devretmeden sonra durur ve kullanici
        // "workflow yarim kaldi" gorur.
        builder = builder.WithAutonomousMode(definition.MaxIterations ?? DefaultAutonomousTurns);

        return builder.Build();
    }

    private static Workflow BuildGroupChat(WorkflowDefinition definition, IReadOnlyList<AIAgent> participants)
    {
        var maxIterations = definition.MaxIterations ?? DefaultGroupChatIterations;

        var builder = AgentWorkflowBuilder
            .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)
            {
                MaximumIterationCount = maxIterations,
            })
            .AddParticipants(participants)
            .WithName(definition.Name);

        if (definition.Description is { Length: > 0 } description)
        {
            builder = builder.WithDescription(description);
        }

        return builder.Build();
    }

    private async ValueTask<Workflow> BuildMagenticAsync(
        WorkflowDefinition definition,
        IReadOnlyList<AIAgent> participants,
        CancellationToken cancellationToken)
    {
        var manager = await BindOneAsync(definition.Name, definition.ManagerAgentName!, cancellationToken)
            .ConfigureAwait(false);

        var builder = AgentWorkflowBuilder
            .CreateMagenticBuilderWith(manager)
            .AddParticipants(participants)
            .WithName(definition.Name)

            // 🚨 Plan onayi KAPATILIR. Acik birakildiginda MAF ilk super-step'in
            // sonunda bir RequestInfoEvent yayinlar ve yurutme PendingRequests
            // durumunda kalir (Faz 15'te olculdu). Bu bir human-in-the-loop
            // akisidir ve yanit verme yolu Faz 16'nin konusudur; simdi acik
            // birakmak her Magentic calistirmasini yarim birakirdi.
            .RequirePlanSignoff(false);

        if (definition.MaxIterations is { } rounds)
        {
            builder = builder.WithMaxRounds(rounds);
        }

        if (definition.Description is { Length: > 0 } description)
        {
            builder = builder.WithDescription(description);
        }

        return builder.Build();
    }

    /// <summary>Devretme zincirinin kendiliginden ilerledigi varsayilan tur sayisi.</summary>
    private const int DefaultAutonomousTurns = 8;

    /// <summary>Grup sohbetinin varsayilan tur siniri.</summary>
    private const int DefaultGroupChatIterations = 8;

    private async ValueTask<IReadOnlyList<AIAgent>> BindAsync(
        string workflowName,
        IReadOnlyList<string> agentNames,
        CancellationToken cancellationToken)
    {
        var described = await _resolver.DescribeAsync(agentNames, cancellationToken).ConfigureAwait(false);
        var catalog = await _resolver.ListAsync(cancellationToken).ConfigureAwait(false);
        var known = new HashSet<string>(catalog.Select(static descriptor => descriptor.Name), StringComparer.Ordinal);
        var agents = new List<AIAgent>(described.Count);

        foreach (var info in described)
        {
            if (!known.Contains(info.Name))
            {
                throw new AgentPrismException(
                    $"'{workflowName}' workflow'u '{info.Name}' agent'ini kullaniyor ancak boyle bir agent " +
                    "katalogda yok. Once agent'i tanimlayin, sonra workflow'u kaydedin.");
            }

            agents.Add(Wrap(workflowName, info));
        }

        return agents;
    }

    private async ValueTask<AIAgent> BindOneAsync(
        string workflowName,
        string agentName,
        CancellationToken cancellationToken)
    {
        var bound = await BindAsync(workflowName, [agentName], cancellationToken).ConfigureAwait(false);

        return bound[0];
    }

    /// <summary>
    /// Agent'i onbellekten alir; boylece ayni workflow her derlendiginde ayni
    /// executor kimligi uretilir ve kontrol noktalari uyumlu kalir.
    /// </summary>
    private ChildAgentInvoker Wrap(string workflowName, CallableAgentInfo info)
        => _agents.Get(workflowName, info.Name, info.Description);
}
