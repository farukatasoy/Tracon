using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Skill script calistirmasinin derleme yolundaki tutamagi: MAF'in dosya
/// tabanli skill kaynagini kurar ve saklanan script'lerin calistirma
/// delegesini uretir.
/// </summary>
/// <remarks>
/// <para>
/// Bu tip yalnizca <c>UseSkillScripts</c> cagrildiginda kaydedilir. Kayitli
/// degilse derleyici script destegi olmadan calisir; bu, ozelligin
/// <strong>varsayilan olarak kapali</strong> olmasinin somut karsiligidir.
/// </para>
/// <para>
/// Iki kaynak ayri tutulur:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <strong>Diskteki skill'ler</strong> — MAF'in <c>AgentFileSkillsSource</c>
///     tipi tarar. Icerigi yazan kisi uygulamayi dagitan kisidir.
///   </description></item>
///   <item><description>
///     <strong>Veritabanindaki skill'ler</strong> — AgentPrism'in kendi
///     kaynagindan gecer. MAF'in dosya kaynagini veritabani verisi icin
///     kullanmak kiraci yalitimini ve cache parmak izini atlardi.
///   </description></item>
/// </list>
/// </remarks>
public sealed class SkillScriptSupport
{
    private readonly SandboxedSkillScriptRunner _runner;
    private readonly IOptions<AgentPrismOptions> _options;
    private readonly ILoggerFactory? _loggerFactory;

    /// <summary>Yeni bir script destegi olusturur.</summary>
    /// <param name="runner">Yalitilmis calistirici.</param>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <param name="loggerFactory">Gunlukleyici fabrikasi.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SkillScriptSupport(
        SandboxedSkillScriptRunner runner,
        IOptions<AgentPrismOptions> options,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(options);

        _runner = runner;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    private AgentPrismSkillScriptOptions Options => _options.Value.Skills.Scripts;

    /// <summary>Saklanan script'ler modele gorunur mu.</summary>
    internal bool StoredScriptsEnabled => Options.Enabled && Options.AllowStoredScripts;

    /// <summary>Diskteki skill koklerini tarayan MAF kaynagini uretir.</summary>
    /// <returns>Kok tanimlanmadiysa <see langword="null"/>.</returns>
    internal AgentFileSkillsSource? CreateFileSource()
    {
        var options = Options;

        if (!options.Enabled || options.SkillRoots.Count == 0)
        {
            return null;
        }

        var fileOptions = new AgentFileSkillsSourceOptions
        {
            SearchDepth = options.SearchDepth,

            // Uzanti beyaz listesi MAF tarafinda da uygulanir. Yorumlayici
            // sozlugunde karsiligi olmayan bir uzanti zaten calistirilamaz;
            // burada da elenmesi, calistirilamayacak bir script'in modele hic
            // gorunmemesini saglar.
            AllowedScriptExtensions = options.Interpreters.Keys
                .Select(static extension => "." + extension.TrimStart('.'))
                .ToArray(),
        };

        return new AgentFileSkillsSource(
            options.SkillRoots.ToArray(),
            RunFileScriptAsync,
            fileOptions,
            _loggerFactory);
    }

    /// <summary>Saklanan bir script'i calistiran delegeyi uretir.</summary>
    /// <param name="skillName">Skill adi.</param>
    /// <param name="script">Script tanimi.</param>
    /// <returns>MAF'in cagiracagi delege.</returns>
    /// <remarks>
    /// Parametrenin varsayilan degeri (K-400) kasitlidir: MAF'in ureteci
    /// arguman semasinda bu alani "required" isaretliyor (nullable olsa
    /// bile) ve modelin JSON `null` gondermesini "deger eksik" olarak
    /// reddediyor (<c>Microsoft.Agents.AI.AgentSkillsProvider</c>,
    /// <c>Throw.ArgumentException</c>) — argumansiz bir script icin bu,
    /// gercek calistirmayi TAMAMEN engeller. Varsayilan bos dize, MAF'in
    /// alani "required degil" olarak yayinlamasini saglar; govde zaten
    /// bos/null'i ayni bicimde ele alir.
    /// </remarks>
    internal Func<string, CancellationToken, Task<object?>> CreateStoredScriptDelegate(
        string skillName,
        AgentSkillScriptDefinition script)
    {
        return RunStoredScript;

        Task<object?> RunStoredScript(string arguments = "", CancellationToken cancellationToken = default)
        {
            JsonElement? parsed = null;

            if (arguments is { Length: > 0 })
            {
                try
                {
                    using var document = JsonDocument.Parse(arguments);
                    parsed = document.RootElement.Clone();
                }
                catch (JsonException ex)
                {
                    throw new AgentPrismException(
                        $"'{skillName}/{script.Name}' script'ine gecerli olmayan JSON argumani verildi.",
                        ex);
                }
            }

            return _runner.RunStoredScriptAsync(skillName, script, parsed, cancellationToken);
        }
    }

    private Task<object?> RunFileScriptAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
        => _runner.RunFileScriptAsync(skill, script, arguments, serviceProvider, cancellationToken);
}
