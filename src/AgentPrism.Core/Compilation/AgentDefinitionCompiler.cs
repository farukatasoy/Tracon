using System.Security.Cryptography;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
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
    private readonly SkillScriptSupport? _scripts;
    private readonly CallableAgentResolver? _callableAgents;
    private readonly ITenantContext? _tenantContext;
    private readonly ModelBinding? _utilityModel;

    // MAAI001: Microsoft.Agents.AI.AgentFileStore "evaluation purposes only"
    // olarak isaretli. Bastirma tek bir dosyada toplanmistir (bu dosya, K-020
    // deseninin devami); MAF bu API'yi degistirirse yalniz burasi guncellenir.
#pragma warning disable MAAI001
    private readonly AgentFileStore? _fileStore;
#pragma warning restore MAAI001

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
    /// <param name="scripts">
    /// Skill script destegi. <see langword="null"/> ise hicbir script calistirilamaz;
    /// ozellik <c>UseSkillScripts</c> ile acilir.
    /// </param>
    /// <param name="callableAgents">
    /// Cagrilabilir alt agent'lari cozen cozucu. <see langword="null"/> ise hicbir
    /// agent baska bir agent'i cagiramaz.
    /// </param>
    /// <param name="tenantContext">
    /// Kiraci baglami. Alt cagrilarin kiraci degistirmedigi bununla dogrulanir.
    /// </param>
    /// <param name="utilityModel">
    /// Baglam sikistirmasindaki ozetleme icin varsayilan model. Bir agent
    /// kendi <c>CompactionSettings.SummarizationModel</c>'ini vermezse bu
    /// kullanilir; <see langword="null"/> ise agent'in kendi modeline duser.
    /// </param>
    /// <param name="fileStore">
    /// Bellek saglayicilarinin kullandigi dosya deposu. <see langword="null"/>
    /// ise <c>MemorySettings.EnableFileMemory</c>/<c>EnableTextSearch</c>
    /// isteyen bir tanim derleme hatasi alir.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
#pragma warning disable MAAI001 // AgentFileStore — bkz. _fileStore alanindaki gerekce.
    public AgentDefinitionCompiler(
        IModelProviderRegistry models,
        IToolRegistry tools,
        ILoggerFactory? loggerFactory = null,
        IServiceProvider? services = null,
        ChatHistoryProvider? chatHistoryProvider = null,
        AgentSkillCatalog? skills = null,
        SkillScriptSupport? scripts = null,
        CallableAgentResolver? callableAgents = null,
        ITenantContext? tenantContext = null,
        ModelBinding? utilityModel = null,
        AgentFileStore? fileStore = null)
#pragma warning restore MAAI001
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(tools);

        _models = models;
        _tools = tools;
        _loggerFactory = loggerFactory;
        _services = services;
        _chatHistoryProvider = chatHistoryProvider;
        _skills = skills;
        _scripts = scripts;
        _callableAgents = callableAgents;
        _tenantContext = tenantContext;
        _utilityModel = utilityModel;
        _fileStore = fileStore;
    }

    /// <summary>Tanimi calistirilabilir bir agent'a donusturur.</summary>
    /// <param name="definition">Derlenecek tanim.</param>
    /// <returns>Calistirilabilir agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// Model saglayicisi bulunamazsa veya tanimda kayitli olmayan bir tool adi varsa.
    /// </exception>
    public AIAgent Compile(AgentDefinition definition) => Compile(definition, ResolvedCallableAgents.Empty);

    /// <summary>Tanimi, cozulmus alt agent'lariyla birlikte calistirilabilir bir agent'a donusturur.</summary>
    /// <param name="definition">Derlenecek tanim.</param>
    /// <param name="callableAgents">
    /// <see cref="ResolveCallableAgentsAsync"/> ile onceden cozulmus alt agent ozetleri.
    /// </param>
    /// <returns>Calistirilabilir agent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// Model saglayicisi bulunamazsa veya tanimda kayitli olmayan bir tool adi varsa.
    /// </exception>
    public AIAgent Compile(AgentDefinition definition, ResolvedCallableAgents callableAgents)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var chatClient = CreateChatClient(definition);
        var tools = ResolveTools(definition);
        var chatOptions = BuildChatOptions(definition, tools);

        return definition.Harness is null
            ? CompileChatAgent(definition, chatClient, chatOptions, callableAgents)
            : CompileHarnessAgent(definition, chatClient, chatOptions, callableAgents);
    }

    /// <summary>
    /// Tanimin cagirabilecegi alt agent'lari cozer ve onbellek parmak izini uretir.
    /// </summary>
    /// <param name="definition">Cozulecek agent tanimi.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Alt agent ozetleri ve parmak izi.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> <see langword="null"/> ise.</exception>
    /// <exception cref="AgentPrismCompilationException">
    /// Tanim alt agent cagirmak istiyor ancak ozellik kayitli degilse.
    /// </exception>
    internal async ValueTask<ResolvedCallableAgents> ResolveCallableAgentsAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.CallableAgentNames.Count == 0)
        {
            return ResolvedCallableAgents.Empty;
        }

        if (_callableAgents is null)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i baska agent'lari cagirmak istiyor ancak alt agent " +
                "cozucusu kayitli degil.")
            {
                AgentName = definition.Name,
            };
        }

        var infos = await _callableAgents
            .DescribeAsync(definition.CallableAgentNames, cancellationToken)
            .ConfigureAwait(false);

        return new ResolvedCallableAgents(infos, CreateCallableFingerprint(infos));
    }

    /// <summary>
    /// Alt agent listesinden onbellek parmak izi uretir.
    /// </summary>
    /// <remarks>
    /// Alt agent'in <em>aciklamasi</em> modele gonderilen talimat metnine gomulur.
    /// Parmak izi surumu tasimasaydi, bir alt agent'in aciklamasi guncellendiginde
    /// cagiran agent onbellekte eski metinle kalirdi ve degisiklik hicbir zaman
    /// etkili olmazdi.
    /// </remarks>
    private static string CreateCallableFingerprint(IReadOnlyList<CallableAgentInfo> infos)
    {
        var content = new StringBuilder();

        foreach (var info in infos)
        {
            content.Append('|').Append(info.Name).Append(':').Append(info.Version);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
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

    private IChatClient CreateChatClient(AgentDefinition definition) => CreateChatClient(definition, definition.Model);

    private IChatClient CreateChatClient(AgentDefinition definition, ModelBinding binding)
    {
        try
        {
            return _models.CreateChatClient(binding);
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

    // MAAI001: Microsoft.Agents.AI.Compaction.* ve AgentFileStore "evaluation
    // purposes only" olarak isaretli. Baglam sikistirma/bellek kurulumu tek
    // bir blokta toplanmistir; MAF bu API'leri degistirirse yalniz burasi
    // guncellenir. Gerekce: docs/KARARLAR.md (K-020 ile ayni desen).
#pragma warning disable MAAI001
    private const int DefaultMinimumPreservedTurns = 2;
    private const int DefaultMinimumPreservedGroups = 4;
    private const int DefaultContextWindowMaxOutputTokens = 4096;

    // ContextWindow ve Pipeline stratejileri disariya bir CompactionTrigger
    // parametresi actmaz (kendi ic tetikleyicilerini kendileri kurar/tasir).
    // Bu durumlarda ObservedCompactionStrategy'nin kendi tetikleyicisi olarak
    // kullanilir: gercek gating tamamen ic stratejiye/stratejilere birakilir.
    private static readonly CompactionTrigger AlwaysTrigger = static _ => true;

    /// <summary>
    /// Tanimin sikistirma ayarlarindan calistirilabilir bir
    /// <see cref="CompactionStrategy"/> kurar.
    /// </summary>
    /// <returns>
    /// <see langword="null"/> — tanim sikistirma istemiyor
    /// (<see cref="AgentDefinition.Compaction"/> bos veya
    /// <see cref="CompactionStrategyKind.None"/>).
    /// </returns>
    /// <exception cref="AgentPrismCompilationException">
    /// Secilen strateji bir tetikleyici gerektirir ama hicbiri verilmemisse,
    /// veya <see cref="CompactionStrategyKind.ContextWindow"/> icin
    /// <see cref="CompactionSettings.MaxContextWindowTokens"/> eksikse.
    /// </exception>
    // internal (private degil): Pipeline'in sabit sirasi gibi yapisal kararlar
    // dogrudan test edilebilsin diye. CompactionProvider bu stratejiyi disariya
    // sizdirmaz, dolayisiyla testler baska bir yoldan erisemez.
    internal ObservedCompactionStrategy? BuildCompactionStrategy(AgentDefinition definition)
    {
        var settings = definition.Compaction;

        if (settings is null || settings.Strategy == CompactionStrategyKind.None)
        {
            return null;
        }

        var trigger = BuildTrigger(settings);
        var requiresTrigger = settings.Strategy is not (CompactionStrategyKind.ContextWindow);

        if (requiresTrigger && trigger is null)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i '{settings.Strategy}' sikistirma stratejisini secti " +
                "ancak hicbir tetikleyici vermedi (TriggerTokens/TriggerMessages/TriggerTurns'ten en az biri gerekir).")
            {
                AgentName = definition.Name,
            };
        }

        CompactionStrategy inner = settings.Strategy switch
        {
            CompactionStrategyKind.SlidingWindow => new SlidingWindowCompactionStrategy(
                trigger!, settings.MinimumPreservedTurns ?? DefaultMinimumPreservedTurns, target: null),

            CompactionStrategyKind.Truncation => new TruncationCompactionStrategy(
                trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),

            CompactionStrategyKind.ToolResult => new ToolResultCompactionStrategy(
                trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),

            CompactionStrategyKind.Summarization => new SummarizationCompactionStrategy(
                ResolveSummarizationChatClient(definition),
                trigger!,
                settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups,
                settings.SummarizationPrompt,
                target: null),

            CompactionStrategyKind.ContextWindow => BuildContextWindowStrategy(definition, settings),

            // Sira sabittir ve dokumante edilmistir: ToolResult -> SlidingWindow -> Summarization.
            // Serbest sira, arayuzde anlasilmasi zor bir yapilandirma yuzeyi uretir.
            CompactionStrategyKind.Pipeline => new PipelineCompactionStrategy(
            [
                new ToolResultCompactionStrategy(
                    trigger!, settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups, target: null),
                new SlidingWindowCompactionStrategy(
                    trigger!, settings.MinimumPreservedTurns ?? DefaultMinimumPreservedTurns, target: null),
                new SummarizationCompactionStrategy(
                    ResolveSummarizationChatClient(definition),
                    trigger!,
                    settings.MinimumPreservedGroups ?? DefaultMinimumPreservedGroups,
                    settings.SummarizationPrompt,
                    target: null),
            ]),

            _ => throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i bilinmeyen bir sikistirma stratejisi secti: '{settings.Strategy}'.")
            {
                AgentName = definition.Name,
            },
        };

        return new ObservedCompactionStrategy(inner, trigger ?? AlwaysTrigger, target: null);
    }

    /// <summary>
    /// Ayarlanmis tetikleyici alanlarindan tek bir <see cref="CompactionTrigger"/> kurar.
    /// Birden fazlasi doluysa herhangi biri gerceklestiginde tetiklenecek sekilde birlestirir.
    /// </summary>
    /// <returns>Hicbir tetikleyici alani dolu degilse <see langword="null"/>.</returns>
    private static CompactionTrigger? BuildTrigger(CompactionSettings settings)
    {
        List<CompactionTrigger>? triggers = null;

        if (settings.TriggerTokens is { } tokens)
        {
            (triggers ??= []).Add(CompactionTriggers.TokensExceed(tokens));
        }

        if (settings.TriggerMessages is { } messages)
        {
            (triggers ??= []).Add(CompactionTriggers.MessagesExceed(messages));
        }

        if (settings.TriggerTurns is { } turns)
        {
            (triggers ??= []).Add(CompactionTriggers.TurnsExceed(turns));
        }

        return triggers switch
        {
            null => null,
            { Count: 1 } single => single[0],
            _ => CompactionTriggers.Any([.. triggers]),
        };
    }

    private static ContextWindowCompactionStrategy BuildContextWindowStrategy(AgentDefinition definition, CompactionSettings settings)
    {
        if (settings.MaxContextWindowTokens is not { } maxContextWindowTokens)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i ContextWindow sikistirma stratejisini secti ancak " +
                $"{nameof(CompactionSettings.MaxContextWindowTokens)} vermedi.")
            {
                AgentName = definition.Name,
            };
        }

        var maxOutputTokens = settings.MaxOutputTokens
            ?? definition.Model.MaxOutputTokens
            ?? DefaultContextWindowMaxOutputTokens;

        // toolEvictionThreshold/truncationThreshold bu fazda CompactionSettings'te
        // acilmiyor (dokumanin §13.1 sekli bunlari icermiyor) — makul sabit
        // degerler kullanilir. Ihtiyac cikarsa ayri bir alan olarak eklenir.
        return new ContextWindowCompactionStrategy(
            maxContextWindowTokens,
            maxOutputTokens,
            toolEvictionThreshold: 0.5,
            truncationThreshold: 0.7);
    }

    /// <summary>
    /// Ozetleme cagrisinda kullanilacak modeli cozer ve token izleyicisiyle sarar.
    /// </summary>
    /// <remarks>
    /// Sira: agent'in kendi <see cref="CompactionSettings.SummarizationModel"/>'i
    /// → uygulama genelindeki yardimci model → agent'in kendi modeli.
    /// </remarks>
    private CompactionUsageTrackingChatClient ResolveSummarizationChatClient(AgentDefinition definition)
    {
        var binding = definition.Compaction?.SummarizationModel ?? _utilityModel ?? definition.Model;

        return new CompactionUsageTrackingChatClient(CreateChatClient(definition, binding));
    }

    /// <summary>Tanimin bellek ayarlarindan <see cref="AIContextProvider"/> listesi kurar.</summary>
    private List<AIContextProvider> CreateMemoryProviders(AgentDefinition definition)
    {
        var memory = definition.Memory;

        if (memory is null)
        {
            return [];
        }

        var providers = new List<AIContextProvider>();

        if (memory.EnableFileMemory)
        {
            providers.Add(new FileMemoryProvider(RequireFileStore(definition), stateInitializer: null, options: null));
        }

        if (memory.EnableTodo)
        {
            providers.Add(new TodoProvider(new TodoProviderOptions()));
        }

        if (CreateTextSearchProvider(definition) is { } textSearch)
        {
            providers.Add(textSearch);
        }

        return providers;
    }

    private TextSearchProvider? CreateTextSearchProvider(AgentDefinition definition)
    {
        if (definition.Memory is not { EnableTextSearch: true })
        {
            return null;
        }

        var fileStore = RequireFileStore(definition);

        return new TextSearchProvider(
            (query, cancellationToken) => SearchFileStoreAsync(fileStore, query, cancellationToken),
            new TextSearchProviderOptions(),
            _loggerFactory);
    }

    private AgentFileStore RequireFileStore(AgentDefinition definition)
    {
        if (_fileStore is null)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i bir bellek saglayicisi istiyor ancak dosya deposu kayitli degil.")
            {
                AgentName = definition.Name,
            };
        }

        return _fileStore;
    }

    /// <summary>
    /// <see cref="AgentFileStore.SearchAsync"/> sonuclarini
    /// <see cref="TextSearchProvider.TextSearchResult"/>'a esler.
    /// </summary>
    /// <remarks>
    /// Hangi somut <see cref="AgentFileStore"/> kayitliysa (bu faz: bellek ici)
    /// onun uzerinde arar; kalici bir depo kayit edilirse kod degismeden
    /// kalici aramaya doner.
    /// </remarks>
    private static async Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchFileStoreAsync(
        AgentFileStore fileStore,
        string query,
        CancellationToken cancellationToken)
    {
        var matches = await fileStore
            .SearchAsync("/", regexPattern: query, globPattern: null, recursive: true, cancellationToken)
            .ConfigureAwait(false);

        return matches.Select(static match => new TextSearchProvider.TextSearchResult
        {
            Text = match.Snippet,
            SourceName = match.FileName,
            SourceLink = match.FileName,
        });
    }
#pragma warning restore MAAI001

    private ChatClientAgent CompileChatAgent(
        AgentDefinition definition,
        IChatClient chatClient,
        ChatOptions chatOptions,
        ResolvedCallableAgents callableAgents)
    {
        var options = new ChatClientAgentOptions
        {
            Id = definition.Name,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions,
            ChatHistoryProvider = _chatHistoryProvider,
        };

        var providers = new List<AIContextProvider>(2);

        if (definition.SkillNames.Count > 0)
        {
            providers.Add(CreateSkillsProvider(definition));
        }

        if (CreateBackgroundAgentsProvider(definition, callableAgents) is { } backgroundAgents)
        {
            providers.Add(backgroundAgents);
        }

        if (BuildCompactionStrategy(definition) is { } compactionStrategy)
        {
            // MAAI001: CompactionProvider "evaluation purposes only" — gerekce
            // BuildCompactionStrategy'nin ustundeki bloktakiyle aynidir.
#pragma warning disable MAAI001
            providers.Add(new CompactionProvider(compactionStrategy, stateKey: null, _loggerFactory));
#pragma warning restore MAAI001
        }

        providers.AddRange(CreateMemoryProviders(definition));

        if (providers.Count > 0)
        {
            options.AIContextProviders = providers;
        }

        return chatClient.AsAIAgent(options, _loggerFactory, _services);
    }

    /// <summary>
    /// Alt agent cagrisini saglayan baglam saglayicisini kurar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Microsoft Agent Framework'un <see cref="BackgroundAgentsProvider"/> tipi bir
    /// <see cref="AIContextProvider"/>'dir; harness gerektirmez. Duz agent yolu bu
    /// yuzden birinci sinif destege sahiptir - K-053'te belgelenen harness kusuru
    /// bu ozelligi de vuracakti.
    /// </para>
    /// <para>
    /// Her alt agent <see cref="ChildAgentInvoker"/> ile sarilir. Saglayici alt
    /// agent'i <c>options = null</c> ile cagirir (Faz 12'de olculdu); agac bilgisi
    /// yalnizca sarmalayici tarafindan eklenebilir.
    /// </para>
    /// </remarks>
    // MAAI001: BackgroundAgentsProvider "evaluation purposes only" olarak isaretli.
    // Bastirma bilincli bir karardir ve K-020 ile ayni gerekceye dayanir: alt agent
    // kurulumu tek bir metotta toplanmistir, MAF bu API'yi degistirirse yalnizca
    // burasi guncellenir. Gerekce: docs/KARARLAR.md, karar K-097.
#pragma warning disable MAAI001
    private BackgroundAgentsProvider? CreateBackgroundAgentsProvider(
        AgentDefinition definition,
        ResolvedCallableAgents callableAgents)
        => CreateChildAgents(definition, callableAgents) is { } children
            ? new BackgroundAgentsProvider(children, new BackgroundAgentsProviderOptions())
            : null;
#pragma warning restore MAAI001

    /// <summary>Cagrilabilir alt agent'lari sarmalayicilariyla birlikte kurar.</summary>
    /// <returns>Sarilmis alt agent'lar; tanim alt agent cagirmiyorsa <see langword="null"/>.</returns>
    private List<AIAgent>? CreateChildAgents(AgentDefinition definition, ResolvedCallableAgents callableAgents)
    {
        if (callableAgents.Agents.Count == 0)
        {
            return null;
        }

        if (_callableAgents is null || _tenantContext is null)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i baska agent'lari cagirmak istiyor ancak alt agent " +
                "cozucusu kayitli degil.")
            {
                AgentName = definition.Name,
            };
        }

        var logger = _loggerFactory?.CreateLogger<ChildAgentInvoker>()
            ?? (ILogger)Microsoft.Extensions.Logging.Abstractions.NullLogger<ChildAgentInvoker>.Instance;

        var children = new List<AIAgent>(callableAgents.Agents.Count);

        foreach (var info in callableAgents.Agents)
        {
            children.Add(new ChildAgentInvoker(_callableAgents, _tenantContext, logger, definition.Name, info));
        }

        return children;
    }

    private HarnessAgent CompileHarnessAgent(
        AgentDefinition definition,
        IChatClient chatClient,
        ChatOptions chatOptions,
        ResolvedCallableAgents callableAgents)
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

            // FileAccessStore BILEREK atanmiyor: yalnizca deger atandiginda
            // etkinlesir, atanmamis olmasi dosya erisiminin kapali olmasi demektir.
            // Gerekce: docs/KARARLAR.md, karar K-062.
            //
            // BackgroundAgents ise Faz 12'de acildi ve ayni kurala uyar: tanim
            // hicbir agent adi tasimiyorsa deger atanmaz ve ozellik kapalidir.
        };

        if (definition.SkillNames.Count > 0)
        {
            options.AgentSkillsSource = CreateSkillsSource(definition);
        }

        if (CreateChildAgents(definition, callableAgents) is { } children)
        {
            options.BackgroundAgents = children;
        }

        // Cakisma denetimi: kullanici hem sikistirma/bellek istemis hem
        // harness'ta ayni yetenegi kapatmissa hangisinin kazandigi sessizce
        // belirsiz kalmamali (K1 sifir surpriz).
        if (definition.Compaction is { Strategy: not CompactionStrategyKind.None })
        {
            if (harness.DisableCompaction)
            {
                throw new AgentPrismCompilationException(
                    $"'{definition.Name}' agent'i sikistirma istiyor ancak " +
                    $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableCompaction)} kapatilmis.")
                {
                    AgentName = definition.Name,
                };
            }

            options.CompactionStrategy = BuildCompactionStrategy(definition);
        }

        if (definition.Memory is { EnableFileMemory: true })
        {
            if (harness.DisableFileMemory)
            {
                throw new AgentPrismCompilationException(
                    $"'{definition.Name}' agent'i dosya bellegi istiyor ancak " +
                    $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableFileMemory)} kapatilmis.")
                {
                    AgentName = definition.Name,
                };
            }

            options.FileMemoryStore = RequireFileStore(definition);
        }

        if (definition.Memory is { EnableTodo: true } && harness.DisableTodoProvider)
        {
            throw new AgentPrismCompilationException(
                $"'{definition.Name}' agent'i todo takibi istiyor ancak " +
                $"{nameof(HarnessSettings)}.{nameof(HarnessSettings.DisableTodoProvider)} kapatilmis.")
            {
                AgentName = definition.Name,
            };
        }

        // EnableTodo == true ve DisableTodoProvider == false ise ek bir sey
        // YAPILMAZ: harness todo takibini varsayilan olarak zaten acik tutar.

        if (CreateTextSearchProvider(definition) is { } textSearch)
        {
            options.AIContextProviders = [textSearch];
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

        AgentSkillsSource source = new AggregatingAgentSkillsSource(CreateInnerSources(definition));
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

    /// <summary>Veritabani ve disk kaynaklarini birlestirir.</summary>
    /// <remarks>
    /// Disk kaynagi ancak <c>UseSkillScripts</c> ile bir kok tanimlandiginda
    /// eklenir. Sirasi onemlidir: veritabani kaynagi once gelir, boylece ayni
    /// adda bir skill varsa <c>DeduplicatingAgentSkillsSource</c> veritabani
    /// kaydini korur ve kiraci yalitimi bozulmaz.
    /// </remarks>
    private List<AgentSkillsSource> CreateInnerSources(AgentDefinition definition)
    {
        var sources = new List<AgentSkillsSource>(2)
        {
            new AgentPrismSkillsSource(_skills!, definition, _scripts),
        };

        if (_scripts?.CreateFileSource() is { } fileSource)
        {
            sources.Add(fileSource);
        }

        return sources;
    }
}

/// <summary>
/// Bir tanimin cagirabilecegi alt agent'larin cozulmus hali ve onbellek parmak izi.
/// </summary>
/// <param name="Agents">Alt agent ozetleri.</param>
/// <param name="Fingerprint">
/// Alt agent adlarindan ve surumlerinden turetilmis parmak izi. Derlenmis agent
/// onbelleginin anahtarina girer.
/// </param>
public readonly record struct ResolvedCallableAgents(
    IReadOnlyList<CallableAgentInfo> Agents,
    string Fingerprint)
{
    /// <summary>Alt agent cagirmayan bir tanimin sonucu.</summary>
    public static ResolvedCallableAgents Empty { get; } = new([], string.Empty);
}
