using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="AgentDefinition"/> tanimini calistirilabilir bir
/// <see cref="AIAgent"/> nesnesine donusturur.
/// </summary>
/// <remarks>
/// <para>Donusum su adimlardan gecer:</para>
/// <list type="number">
///   <item><description><see cref="ModelBinding"/> → <see cref="IModelProviderRegistry"/> → <see cref="IChatClient"/></description></item>
///   <item><description><see cref="AgentDefinition.ToolNames"/> → <see cref="IToolRegistry"/> → <see cref="AIFunction"/> listesi</description></item>
///   <item><description><see cref="AgentDefinition.Harness"/> dolu ise <c>AsHarnessAgent</c>, degilse <c>AsAIAgent</c></description></item>
/// </list>
/// <para>
/// Bilinmeyen bir tool adi <see cref="AgentPrismCompilationException"/> ile
/// sonuclanir. Sessizce atlanmaz: eksik tool ile calisan bir agent, kullanicinin
/// bekledigi isi yapmayan agent demektir.
/// </para>
/// </remarks>
public sealed class AgentDefinitionCompiler
{
    private readonly IModelProviderRegistry _models;
    private readonly IToolRegistry _tools;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IServiceProvider? _services;
    private readonly ChatHistoryProvider? _chatHistoryProvider;
    private readonly AgentSkillCatalog? _skills;

    /// <summary>Yeni bir derleyici olusturur.</summary>
    /// <param name="models">Model saglayici defteri.</param>
    /// <param name="tools">Tool defteri.</param>
    /// <param name="loggerFactory">Uretilen agent'lara verilecek gunlukleyici fabrikasi.</param>
    /// <param name="services">Uretilen agent'lara verilecek servis saglayici.</param>
    /// <param name="chatHistoryProvider">
    /// Uretilen agent'lara baglanacak sohbet gecmisi saglayicisi. <see langword="null"/> ise
    /// Microsoft Agent Framework'un bellek ici varsayilani kullanilir ve gecmis oturum
    /// durumunun icinde tasinir.
    /// </param>
    /// <param name="skills">Skill kaynaklarini cozen katalog.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public AgentDefinitionCompiler(
        IModelProviderRegistry models,
        IToolRegistry tools,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null,
        ChatHistoryProvider? chatHistoryProvider = null,
        AgentSkillCatalog? skills = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(tools);

        _models = models;
        _tools = tools;
        _loggerFactory = loggerFactory;
        _services = services;
        _chatHistoryProvider = chatHistoryProvider;
        _skills = skills;
    }

    /// <summary>Tanimi calistirilabilir bir agent'a donusturur.</summary>
    /// <param name="definition">Derlenecek tanim.</param>
    /// <returns>Calistirilabilir agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// Model saglayicisi bulunamazsa veya tanimda kayitli olmayan bir tool adi varsa.
    /// </exception>
    public AIAgent Compile(AgentDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var chatClient = CreateChatClient(definition);
        var tools = ResolveTools(definition);
        var chatOptions = BuildChatOptions(definition, tools);

        return definition.Harness is null
            ? CompileChatAgent(definition, chatClient, chatOptions)
            : CompileHarnessAgent(definition, chatClient, chatOptions);
    }

    /// <summary>Tanimin skill'lerini dogrular ve cache anahtarini uretir.</summary>
    /// <param name="definition">Dogrulanacak agent tanimi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Skill parmak izi.</returns>
    internal ValueTask<ResolvedAgentSkills> ResolveSkillsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.SkillNames.Count == 0)
        {
            return ValueTask.FromResult(ResolvedAgentSkills.Empty);
        }

        if (_skills is null)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i skill kullaniyor ancak skill katalogu kayitli degil.")
            {
                AgentName = definition.Name,
            };
        }

        return _skills.ResolveAsync(definition, cancellationToken);
    }

    private IChatClient CreateChatClient(AgentDefinition definition)
    {
        try
        {
            return _models.CreateChatClient(definition.Model);
        }
        catch (AgentPrismException ex)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i derlenemedi: {ex.Message}",
                ex)
            {
                AgentName = definition.Name,
            };
        }
    }

    private List<AITool> ResolveTools(AgentDefinition definition)
    {
        var tools = new List<AITool>(definition.ToolNames.Count);
        List<string>? missing = null;

        foreach (var toolName in definition.ToolNames)
        {
            if (_tools.TryGet(toolName, out var tool))
            {
                tools.Add(tool);
            }
            else
            {
                (missing ??= []).Add(toolName);
            }
        }

        if (missing is not null)
        {
            var registered = _tools.List();
            var available = registered.Count == 0
                ? "hic tool kayitli degil"
                : string.Join(", ", registered.Select(static descriptor => descriptor.Name));

            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i su tool'lara isaret ediyor ancak bunlar kodda kayitli degil: " +
                $"{string.Join(", ", missing)}. Kayitli tool'lar: {available}. " +
                "Tool'lar yalnizca kodda tanimlanir; `builder.AddAgentPrism().AddTool(...)` ile kaydedin.")
            {
                AgentName = definition.Name,
            };
        }

        return tools;
    }

    private static ChatOptions BuildChatOptions(AgentDefinition definition, List<AITool> tools)
    {
        var options = new ChatOptions
        {
            Instructions = definition.Instructions,
            ModelId = definition.Model.Model,
            Temperature = definition.Model.Temperature,
            TopP = definition.Model.TopP,
            MaxOutputTokens = definition.Model.MaxOutputTokens,
        };

        if (tools.Count > 0)
        {
            options.Tools = tools;
        }

        if (ParseReasoningEffort(definition) is { } effort)
        {
            options.Reasoning = new ReasoningOptions { Effort = effort };
        }

        return options;
    }

    /// <summary>
    /// <see cref="ModelBinding.ReasoningEffort"/> degerini
    /// <see cref="Microsoft.Extensions.AI.ReasoningEffort"/> degerine cevirir.
    /// </summary>
    /// <remarks>
    /// Gecersiz deger sessizce yok sayilmaz. Akil yurutme cabasi hem maliyeti hem
    /// gecikmeyi degistirir; yanlis yazilmis bir deger fark edilmeden calisirsa
    /// kullanici bekledigi davranisi alamaz ve sebebini goremez.
    /// Modelin bu ayari destekleyip desteklemedigine saglayici karar verir.
    /// </remarks>
    private static ReasoningEffort? ParseReasoningEffort(AgentDefinition definition)
    {
        var value = definition.Model.ReasoningEffort;

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<ReasoningEffort>(value, ignoreCase: true, out var effort)
            && Enum.IsDefined(effort))
        {
            return effort;
        }

        throw new AgentPrismCompilationException(
            $"'{definition.Name}' agent'inin akil yurutme cabasi degeri taninmiyor: '{value}'. " +
            $"Gecerli degerler: {string.Join(", ", Enum.GetNames<ReasoningEffort>())}.")
        {
            AgentName = definition.Name,
        };
    }

    private ChatClientAgent CompileChatAgent(AgentDefinition definition, IChatClient chatClient, ChatOptions chatOptions)
    {
        var options = new ChatClientAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = _chatHistoryProvider,
        };

        if (definition.SkillNames.Count > 0)
        {
            options.AIContextProviders = [CreateSkillsProvider(definition)];
        }

        return chatClient.AsAIAgent(options, _loggerFactory, _services);
    }

    private HarnessAgent CompileHarnessAgent(AgentDefinition definition, IChatClient chatClient, ChatOptions chatOptions)
    {
        var harness = definition.Harness!;

        // MAAI001: Microsoft Agent Framework'un harness secenekleri "evaluation purposes only"
        // olarak isaretli ve ileride degisebilir. Bastirma bilincli bir karardir:
        // harness kullanimi tek bir dosyada toplanmistir, boylece MAF bu API'yi
        // degistirirse yalnizca burasi guncellenir.
        // Gerekce: docs/KARARLAR.md, karar K-020.
#pragma warning disable MAAI001
        var options = new HarnessAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = _chatHistoryProvider,
            HarnessInstructions = harness.HarnessInstructions,
            MaxContextWindowTokens = harness.MaxContextWindowTokens,
            MaxOutputTokens = harness.MaxOutputTokens,
            MaximumIterationsPerRequest = harness.MaximumIterationsPerRequest,
            DisableCompaction = harness.DisableCompaction,
            DisableTodoProvider = harness.DisableTodoProvider,
            DisableFileMemory = harness.DisableFileMemory,
            DisableWebSearch = harness.DisableWebSearch,
            DisableToolAutoApproval = harness.DisableToolAutoApproval,
            DisableAgentSkillsProvider = harness.DisableAgentSkillsProvider,
            DisableAgentModeProvider = harness.DisableAgentModeProvider,

            // Harness kendi ic span'lerini uretir. Kaynak adi verilmezse bunlar
            // MAF'in kendi kaynagina gider ve AgentPrism'in span deposu onlari
            // hic gormez; waterfall gorunumunde harness adimlari eksik kalirdi.
            OpenTelemetrySourceName = AgentPrismDiagnostics.ActivitySourceName,

            // FileAccessStore ve BackgroundAgents BILEREK atanmiyor. Ikisi de
            // yalnizca deger atandiginda etkinlesir; atanmamis olmalari dosya
            // erisiminin ve arka plan agent'larinin kapali olmasi demektir.
            // Gerekce: docs/KARARLAR.md, karar K-062.
        };

        if (definition.SkillNames.Count > 0)
        {
            options.AgentSkillsSource = CreateSkillsSource(definition);
        }

        return chatClient.AsHarnessAgent(options, _loggerFactory, _services);
#pragma warning restore MAAI001
    }

    private AgentSkillsProvider CreateSkillsProvider(AgentDefinition definition)
        => new(CreateSkillsSource(definition), new AgentSkillsProviderOptions(), _loggerFactory, ownsSource: true);

    private DeduplicatingAgentSkillsSource CreateSkillsSource(AgentDefinition definition)
    {
        if (_skills is null)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i skill kullaniyor ancak skill katalogu kayitli degil.")
            {
                AgentName = definition.Name,
            };
        }

        AgentSkillsSource source = new AggregatingAgentSkillsSource([
            new AgentPrismSkillsSource(_skills, definition),
        ]);
        source = new FilteringAgentSkillsSource(
            source,
            (skill, _) => definition.SkillNames.Contains(skill.Frontmatter.Name, StringComparer.Ordinal),
            _loggerFactory);
        source = new CachingAgentSkillsSource(
            source,
            new CachingAgentSkillsSourceOptions
            {
                CacheIsolationKeySelector = _ => _skills.TenantId,
            });
        return new DeduplicatingAgentSkillsSource(source, _loggerFactory);
    }
}
