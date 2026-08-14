using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Bir <see cref="AgentDefinition"/>'i kaydetmeden ve hicbir model cagirmadan
/// derler.
/// </summary>
/// <remarks>
/// <para>
/// Denetimin tek degeri, gercek derleme yolunun (<see cref="AgentDefinitionCompiler"/>)
/// yapacaginin aynisini yapmasidir. Derleyici ilk hatada istisna firlattigi icin
/// bu tip her denetimi <strong>kendi sirasiyla ve bagimsiz</strong> yurutur, tum
/// bulgulari toplar ve gercek derlemeyi (<see cref="AgentDefinitionCompiler.Compile(AgentDefinition)"/>)
/// yalnizca hicbir bagimsiz denetim hata bulmadiginda, kalan yapisal hatalari
/// (akil yurutme cabasi, sikistirma/bellek ayarlari gibi) yakalamak icin en sona
/// birakir.
/// </para>
/// <para>
/// Derlenen <see cref="Microsoft.Agents.AI.AIAgent"/> kullanilmaz ve atilir; hicbir
/// <c>runs</c> satiri acilmaz, hicbir token harcanmaz.
/// </para>
/// </remarks>
public sealed class AgentDefinitionValidator
{
    private readonly IModelProviderRegistry _models;
    private readonly IToolRegistry _tools;
    private readonly AgentSkillCatalog _skills;
    private readonly IAgentCatalog _catalog;
    private readonly AgentDefinitionCompiler _compiler;
    private readonly IOptions<AgentPrismOptions> _options;
    private readonly IMcpToolRefresher? _mcpRefresher;

    /// <summary>Yeni bir dogrulayici olusturur.</summary>
    /// <param name="models">Model saglayici defteri.</param>
    /// <param name="tools">Tool defteri.</param>
    /// <param name="skills">Skill katalogu.</param>
    /// <param name="catalog">Agent katalogu — cagri grafigi denetimi icin.</param>
    /// <param name="compiler">Bagimsiz denetimler temizken calistirilan gercek derleyici.</param>
    /// <param name="options">
    /// <see cref="AgentPrismValidationOptions.McpTimeout"/>'un okundugu ayarlar.
    /// </param>
    /// <param name="mcpRefresher">
    /// Kayitliysa, eksik bir tool adi bulundugunda MCP tool listesini taze
    /// cekmek icin kullanilir. <see langword="null"/> ise <c>AgentPrism.Mcp</c>
    /// kayitli degildir; eksik tool adlari dogrudan hata olarak raporlanir.
    /// </param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public AgentDefinitionValidator(
        IModelProviderRegistry models,
        IToolRegistry tools,
        AgentSkillCatalog skills,
        IAgentCatalog catalog,
        AgentDefinitionCompiler compiler,
        IOptions<AgentPrismOptions> options,
        IMcpToolRefresher? mcpRefresher = null)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentNullException.ThrowIfNull(options);

        _models = models;
        _tools = tools;
        _skills = skills;
        _catalog = catalog;
        _compiler = compiler;
        _options = options;
        _mcpRefresher = mcpRefresher;
    }

    /// <summary>Bir tanimi kaydetmeden ve model cagirmadan derler.</summary>
    /// <param name="definition">Denetlenecek tanim.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Bulunan tum mesajlari tasiyan rapor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> <see langword="null"/> ise.</exception>
    public async ValueTask<AgentValidationReport> ValidateAsync(
        AgentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var messages = new List<ValidationMessage>();
        var inconclusive = false;

        CheckModel(definition, messages);

        await CheckToolsAsync(
            definition,
            messages,
            _options.Value.Validation.McpTimeout,
            () => inconclusive = true,
            cancellationToken).ConfigureAwait(false);

        await CheckSkillsAsync(definition, messages, cancellationToken).ConfigureAwait(false);
        await CheckCallGraphAsync(definition, messages, cancellationToken).ConfigureAwait(false);

        // Inconclusive iken derlemeyi de calistirmak anlamsizdir: erisilemeyen
        // MCP sunucusunun arkasindaki tool gercek derlemede de cozulemez ve
        // "unknown_tool"un yerine yaniltici bir "compilation_error" gecerdi.
        if (!HasError(messages) && !inconclusive)
        {
            await CheckStructureAsync(definition, messages, cancellationToken).ConfigureAwait(false);
        }

        return new AgentValidationReport
        {
            Valid = !HasError(messages),
            Inconclusive = inconclusive,
            Messages = messages,
        };
    }

    private static bool HasError(List<ValidationMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Severity == ValidationSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }

    private void CheckModel(AgentDefinition definition, List<ValidationMessage> messages)
    {
        var providers = _models.List();
        var registered = false;

        foreach (var provider in providers)
        {
            if (string.Equals(provider.Name, definition.Model.Provider, StringComparison.Ordinal))
            {
                registered = true;
                break;
            }
        }

        if (!registered)
        {
            var known = providers.Count == 0
                ? "hic saglayici kayitli degil"
                : string.Join(", ", providers.Select(static provider => provider.Name));

            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "unknown_model",
                Message = $"'{definition.Model.Provider}' adinda bir model saglayicisi kayitli degil. " +
                          $"Kayitli saglayicilar: {known}.",
                Path = "model.provider",
            });

            return;
        }

        try
        {
            // Gercek yolla ayni cagri: model istemcisi kurulur, hicbir istek gitmez.
            _ = _models.CreateChatClient(definition.Model);
        }
        catch (AgentPrismException ex)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "invalid_setting",
                Message = ex.Message,
                Path = "model.providerSettings",
            });
        }
    }

    private async ValueTask CheckToolsAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        TimeSpan mcpTimeout,
        Action markInconclusive,
        CancellationToken cancellationToken)
    {
        var missing = FindMissingTools(definition);

        if (missing.Count == 0)
        {
            return;
        }

        if (_mcpRefresher is not null)
        {
            if (await TryRefreshMcpAsync(mcpTimeout, cancellationToken).ConfigureAwait(false))
            {
                missing = FindMissingTools(definition);
            }
            else
            {
                // MCP sunucusuna ulasilamadi ile tool adi yanlis aynı sey degildir.
                // Eksik kalan adlarin GERCEKTEN bilinmeyen mi yoksa erisilemeyen
                // sunucunun ARKASINDA mi oldugu bilinemez; bu yuzden onlar icin
                // unknown_tool YAZILMAZ, yalniz sonuc Inconclusive isaretlenir.
                markInconclusive();
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Code = "mcp_unreachable",
                    Message = "Eksik tool adlari icin MCP sunucularindan taze liste cekilemedi " +
                              "(zaman asimi veya baglanti hatasi). Sonuc kesin degil.",
                });

                return;
            }
        }

        foreach (var (index, name) in missing)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "unknown_tool",
                Message = $"'{definition.Name}' agent'i '{name}' adli bir tool'a isaret ediyor ancak " +
                          "bu kodda kayitli degil.",
                Path = $"toolNames[{index}]",
            });
        }
    }

    private List<(int Index, string Name)> FindMissingTools(AgentDefinition definition)
    {
        List<(int, string)>? missing = null;

        for (var index = 0; index < definition.ToolNames.Count; index++)
        {
            var name = definition.ToolNames[index];

            if (!_tools.TryGet(name, out _))
            {
                (missing ??= []).Add((index, name));
            }
        }

        return missing ?? [];
    }

    private async ValueTask<bool> TryRefreshMcpAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);

            var outcome = await _mcpRefresher!.RefreshAsync(cts.Token).ConfigureAwait(false);

            // 🚨 HATA-006 / MT-CORE-006: bir MCP sunucusuna baglanti REDDEDILDIGINDE
            // (aktif "connection refused") McpToolCatalog istisna FIRLATMAZ — o
            // sunucunun tool'lari listeden duser, tazeleme "basarili" doner. Yalniz
            // ZAMAN ASIMINDA (asagidaki catch) bir istisna yukselir. Ikisi de ayni
            // sekilde ele alinmalidir: HadUnreachableServers bu ayrimi kapatir.
            return !outcome.HadUnreachableServers;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // MCP tasima katmani cesitli istisna turleri (HTTP, JSON-RPC) atabilir.
            // Buradaki tek gerekli garanti: ulasilamayan bir sunucu dogrulamayi
            // hic bir zaman kirmaz, yalnizca Inconclusive yapar.
            return false;
        }
    }

    private async ValueTask CheckSkillsAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index < definition.SkillNames.Count; index++)
        {
            var name = definition.SkillNames[index];

            if (!await _skills.ExistsAsync(name, cancellationToken).ConfigureAwait(false))
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Error,
                    Code = "unknown_skill",
                    Message = $"'{definition.Name}' agent'i '{name}' skill'ine isaret ediyor ancak skill bulunamadi.",
                    Path = $"skillNames[{index}]",
                });
            }
        }
    }

    private async ValueTask CheckCallGraphAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        if (definition.CallableAgentNames.Count == 0)
        {
            return;
        }

        var descriptors = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);

        if (AgentCallGraph.ValidateDetailed(definition.Name, definition.CallableAgentNames, descriptors) is { } problem)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = problem.Code,
                Message = problem.Message,
                Path = "callableAgentNames",
            });
        }
    }

    private async ValueTask CheckStructureAsync(
        AgentDefinition definition,
        List<ValidationMessage> messages,
        CancellationToken cancellationToken)
    {
        try
        {
            var callable = await _compiler.ResolveCallableAgentsAsync(definition, cancellationToken).ConfigureAwait(false);

            _ = _compiler.Compile(definition, callable);
        }
        catch (AgentPrismCompilationException ex)
        {
            messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "compilation_error",
                Message = ex.Message,
            });
        }
    }
}
