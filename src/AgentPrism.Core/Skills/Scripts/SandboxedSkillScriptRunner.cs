using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Skill script'lerini yalitilmis bir isletim sistemi surecinde calistiran,
/// AgentPrism'in tek script calistirma yoludur.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Microsoft Agent Framework hicbir script'i kendi basina calistirmaz.</strong>
/// <c>AgentFileSkillScriptRunner</c> bir cagri noktasidir; sandbox, zaman asimi,
/// kaynak siniri ve denetim izi tamamen AgentPrism'in sorumlulugundadir.
/// </para>
/// <para>
/// Her calistirma su kapilardan sirayla gecer. Biri kapaliysa script
/// <em>hic baslamaz</em>:
/// </para>
/// <list type="number">
///   <item><description>Ozellik acik mi (<see cref="AgentPrismSkillScriptOptions.Enabled"/>)</description></item>
///   <item><description>Kiraci icin gecerli bir <see cref="SkillScriptGrant"/> var mi</description></item>
///   <item><description>Uzanti yorumlayici beyaz listesinde mi</description></item>
///   <item><description>Argumanlar boyut ve sema denetiminden gecti mi</description></item>
///   <item><description>Denetim izi yazilabildi mi</description></item>
/// </list>
/// <para>
/// 🚨 Son madde Faz 9'un "gozlemlenebilirlik islevi bozmaz" kuralinin
/// <strong>bilincli istisnasidir</strong>: denetim izine yazilamayan bir script
/// calistirmasi, hicbir kaydi olmayan bir uzaktan kod calistirma olurdu.
/// </para>
/// <para>
/// MAF'in onay akisi devrede kalir: <c>DisableRunSkillScriptApproval</c>
/// ayarlanmaz, yani her script cagrisi once kullanicinin onayini bekler.
/// </para>
/// </remarks>
public sealed class SandboxedSkillScriptRunner : IDisposable
{
    private static readonly ActivitySource ActivitySource = new(AgentPrismDiagnostics.ActivitySourceName);

    private readonly IOptions<AgentPrismOptions> _options;
    private readonly ITenantContext _tenantContext;
    private readonly ISkillScriptGrantStore _grantStore;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<SandboxedSkillScriptRunner> _logger;
    private readonly IRunStore? _runStore;
    private readonly AgentPrismMetrics? _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly SkillScriptConcurrencyLimiter _limiter;

    /// <summary>Yeni bir calistirici olusturur.</summary>
    /// <param name="options">AgentPrism ayarlari.</param>
    /// <param name="tenantContext">Kiraci baglami.</param>
    /// <param name="grantStore">Izin kayitlari deposu.</param>
    /// <param name="auditLog">Denetim izi.</param>
    /// <param name="actorResolver">Aktor cozumleyici.</param>
    /// <param name="logger">Gunlukleyici.</param>
    /// <param name="runStore">Tool cagri kaydinin yazilacagi depo. <see langword="null"/> ise yazilmaz.</param>
    /// <param name="metrics">Metrik aletleri. <see langword="null"/> ise metrik yayilmaz.</param>
    /// <param name="timeProvider">Zaman kaynagi. <see langword="null"/> ise sistem saati.</param>
    /// <exception cref="ArgumentNullException">Zorunlu bagimliliklardan biri <see langword="null"/> ise.</exception>
    public SandboxedSkillScriptRunner(
        IOptions<AgentPrismOptions> options,
        ITenantContext tenantContext,
        ISkillScriptGrantStore grantStore,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<SandboxedSkillScriptRunner> logger,
        IRunStore? runStore = null,
        AgentPrismMetrics? metrics = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(grantStore);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _tenantContext = tenantContext;
        _grantStore = grantStore;
        _auditLog = auditLog;
        _actorResolver = actorResolver;
        _logger = logger;
        _runStore = runStore;
        _metrics = metrics;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _limiter = new SkillScriptConcurrencyLimiter(Options);
    }

    private AgentPrismSkillScriptOptions Options => _options.Value.Skills.Scripts;

    /// <summary>
    /// Diskteki bir skill script'ini calistirir. MAF'in
    /// <c>AgentFileSkillScriptRunner</c> delegesine baglanir.
    /// </summary>
    /// <param name="skill">Script'i tasiyan skill.</param>
    /// <param name="script">Calistirilacak script.</param>
    /// <param name="arguments">Modelin urettigi argumanlar.</param>
    /// <param name="serviceProvider">MAF'in verdigi servis saglayici. Kullanilmaz.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Modele donecek metin sonuc.</returns>
    /// <exception cref="AgentPrismException">Kapilardan biri kapaliysa.</exception>
    public async Task<object?> RunFileScriptAsync(
        AgentFileSkill skill,
        AgentFileSkillScript script,
        JsonElement? arguments,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentNullException.ThrowIfNull(script);

        _ = serviceProvider;

        var skillName = skill.Frontmatter.Name;

        // Span bu metodun govdesinde acilir: Activity.Current bir AsyncLocal'dir
        // ve yardimci metotta acilan span cagirana geri akmaz.
        using var activity = ActivitySource.StartActivity(
            AgentPrismDiagnostics.SkillScriptActivityName,
            ActivityKind.Internal);

        activity?.SetTag(AgentPrismDiagnostics.Tags.SkillName, skillName);
        activity?.SetTag(AgentPrismDiagnostics.Tags.ScriptName, script.Name);

        var extension = Path.GetExtension(script.FullPath).TrimStart('.');
        var workingDirectory = Path.GetDirectoryName(script.FullPath) ?? Environment.CurrentDirectory;

        return await ExecuteAsync(
                new ScriptRequest(
                    skillName,
                    script.Name,
                    extension,
                    script.ParametersSchema,
                    arguments,
                    Stored: false,
                    ScriptPath: script.FullPath,
                    WorkingDirectory: workingDirectory,
                    Content: null),
                activity,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Veritabaninda saklanan bir skill script'ini calistirir.
    /// </summary>
    /// <remarks>
    /// Bu yol yalnizca <see cref="AgentPrismSkillScriptOptions.AllowStoredScripts"/>
    /// acikken kullanilir. Icerik yalnizca calistirma suresince, yalniz sahibinin
    /// erisebildigi gecici bir dizine yazilir ve sonunda silinir.
    /// </remarks>
    /// <param name="skillName">Skill adi.</param>
    /// <param name="script">Script tanimi.</param>
    /// <param name="arguments">Modelin urettigi argumanlar.</param>
    /// <param name="cancellationToken">Iptal belirteci.</param>
    /// <returns>Modele donecek metin sonuc.</returns>
    /// <exception cref="AgentPrismException">Kapilardan biri kapaliysa.</exception>
    public async Task<object?> RunStoredScriptAsync(
        string skillName,
        AgentSkillScriptDefinition script,
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillName);
        ArgumentNullException.ThrowIfNull(script);

        if (!Options.AllowStoredScripts)
        {
            await DenyAsync(
                    skillName,
                    script.Name,
                    "Saklanan script'lerin calistirilmasi kapali. AllowStoredScripts acilmalidir.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using var activity = ActivitySource.StartActivity(
            AgentPrismDiagnostics.SkillScriptActivityName,
            ActivityKind.Internal);

        activity?.SetTag(AgentPrismDiagnostics.Tags.SkillName, skillName);
        activity?.SetTag(AgentPrismDiagnostics.Tags.ScriptName, script.Name);

        JsonElement? schema = null;

        if (script.ParametersSchema is { Length: > 0 } raw)
        {
            try
            {
                using var document = JsonDocument.Parse(raw);
                schema = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                await DenyAsync(
                        skillName,
                        script.Name,
                        "Script'in arguman semasi gecerli JSON degil.",
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        return await ExecuteAsync(
                new ScriptRequest(
                    skillName,
                    script.Name,
                    script.Extension.TrimStart('.'),
                    schema,
                    arguments,
                    Stored: true,
                    ScriptPath: null,
                    WorkingDirectory: null,
                    Content: script.Content),
                activity,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() => _limiter.Dispose();

    private async Task<object?> ExecuteAsync(
        ScriptRequest request,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        var options = Options;
        var tenantId = _tenantContext.TenantId;

        if (!options.Enabled)
        {
            await DenyAsync(
                    request.SkillName,
                    request.ScriptName,
                    "Skill script calistirma kapali. UseSkillScripts ile acilmalidir.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var grant = await _grantStore
            .FindActiveAsync(tenantId, request.SkillName, request.ScriptName, _timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        if (grant is null)
        {
            await DenyAsync(
                    request.SkillName,
                    request.ScriptName,
                    "Bu script icin gecerli bir calistirma izni yok.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!options.Interpreters.TryGetValue(request.Extension, out var interpreter))
        {
            await DenyAsync(
                    request.SkillName,
                    request.ScriptName,
                    $"'{request.Extension}' uzantisi yorumlayici beyaz listesinde degil.",
                    cancellationToken)
                .ConfigureAwait(false);
            interpreter = string.Empty;
        }

        var argumentsJson = request.Arguments?.GetRawText();

        if (argumentsJson is not null && Encoding.UTF8.GetByteCount(argumentsJson) > options.MaxArgumentBytes)
        {
            await DenyAsync(
                    request.SkillName,
                    request.ScriptName,
                    $"Argumanlar {options.MaxArgumentBytes} bayt sinirini asiyor.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (!SkillScriptArgumentValidator.TryValidate(request.Schema, request.Arguments, out var schemaError))
        {
            await DenyAsync(request.SkillName, request.ScriptName, schemaError!, cancellationToken)
                .ConfigureAwait(false);
        }

        // 🚨 Denetim izi calistirmadan ONCE ve zorunlu olarak yazilir. Yazilamazsa
        // calistirma reddedilir; kaydi olmayan bir kod calistirma kabul edilemez.
        await WriteAuditOrThrowAsync(
                tenantId,
                "script.run",
                $"{request.SkillName}/{request.ScriptName}",
                argumentsJson,
                cancellationToken)
            .ConfigureAwait(false);

        using var lease = await _limiter.AcquireAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var scratch = request.Stored ? Directory.CreateTempSubdirectory("agentprism-script-") : null;

        try
        {
            var scriptPath = request.ScriptPath;
            var workingDirectory = request.WorkingDirectory;

            if (scratch is not null)
            {
                scriptPath = Path.Combine(scratch.FullName, $"{request.ScriptName}.{request.Extension}");
                await File.WriteAllTextAsync(scriptPath, request.Content ?? string.Empty, cancellationToken)
                    .ConfigureAwait(false);
                workingDirectory = scratch.FullName;
            }

            var result = await SkillScriptProcessRunner.ExecuteAsync(
                    interpreter,
                    scriptPath!,
                    workingDirectory!,
                    argumentsJson,
                    options,
                    request.SkillName,
                    _timeProvider,
                    cancellationToken)
                .ConfigureAwait(false);

            activity?.SetTag(AgentPrismDiagnostics.Tags.ExitCode, result.ExitCode);
            activity?.SetTag(
                AgentPrismDiagnostics.Tags.DurationMs,
                result.Duration.TotalMilliseconds);
            activity?.SetStatus(result.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            _metrics?.RecordToolInvocation(
                AgentPrismDiagnostics.SkillScriptToolName,
                result.Succeeded,
                result.Duration);

            var text = Format(result);

            await RecordInvocationAsync(request, argumentsJson, result, text, cancellationToken)
                .ConfigureAwait(false);

            return text;
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    private static string Format(SkillScriptExecutionResult result)
    {
        var builder = new StringBuilder();

        if (result.TimedOut)
        {
            builder.AppendLine("Script zaman asimina ugradi ve surec agaci sonlandirildi.");
        }
        else
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"exit_code: {result.ExitCode}");
        }

        if (result.StandardOutput.Length > 0)
        {
            builder.AppendLine("stdout:").AppendLine(result.StandardOutput);
        }

        if (result.StandardError.Length > 0)
        {
            builder.AppendLine("stderr:").AppendLine(result.StandardError);
        }

        return builder.ToString();
    }

    private async ValueTask RecordInvocationAsync(
        ScriptRequest request,
        string? argumentsJson,
        SkillScriptExecutionResult result,
        string text,
        CancellationToken cancellationToken)
    {
        if (_runStore is null || AgentPrismRunContext.CurrentRunId is not { } runId)
        {
            return;
        }

        try
        {
            await _runStore.RecordToolInvocationAsync(
                new ToolInvocationRecord
                {
                    Id = AgentPrismId.NewId(),
                    RunId = runId,
                    ToolName = AgentPrismDiagnostics.SkillScriptToolName,
                    Source = $"skill:{request.SkillName}",
                    Arguments = argumentsJson,
                    Result = result.Succeeded ? text : null,
                    Duration = result.Duration,
                    Error = result.Succeeded
                        ? null
                        : result.TimedOut ? "Zaman asimi." : $"Cikis kodu {result.ExitCode}.",
                    CreatedAt = _timeProvider.GetUtcNow(),

                    // Suren calistirmanin kendi kiracisi; ambient kiraci DEGIL (K-355).
                    TenantId = AgentPrismRunContext.Current?.TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Calistirma kaydi gozlemlenebilirliktir; hatasi calistirmayi bozmaz.
            // Zorunlu olan denetim izidir ve o zaten yazilmistir.
            _logger.LogWarning(ex, "Skill script calistirmasi tool_invocations tablosuna yazilamadi.");
        }
    }

    /// <summary>Reddi denetim izine yazar ve calistirmayi durdurur.</summary>
    /// <remarks>Bu metot her zaman firlatir; donus tipi yalnizca cagri yerini kisaltir.</remarks>
    private async ValueTask DenyAsync(
        string skillName,
        string scriptName,
        string reason,
        CancellationToken cancellationToken)
    {
        await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                _tenantContext.TenantId,
                "script.denied",
                $"{skillName}/{scriptName}",
                before: null,
                // 'after' bir jsonb sutununa yazilir; ham metin gecirmek HER
                // reddi 22P02 ("invalid input syntax for type json") ile
                // sessizce dusuruyordu (HATA-K-skill-audit-json, 2026-08-15) -
                // denetim izi hicbir zaman olusmuyordu.
                after: JsonSerializer.Serialize(reason, AgentPrismCoreJsonContext.Default.String),
                cancellationToken)
            .ConfigureAwait(false);

        throw new AgentPrismException($"'{skillName}/{scriptName}' script'i calistirilmadi: {reason}");
    }

    private async ValueTask WriteAuditOrThrowAsync(
        string tenantId,
        string action,
        string entity,
        string? after,
        CancellationToken cancellationToken)
    {
        try
        {
            await _auditLog.WriteAsync(
                new AuditEntry
                {
                    Id = AgentPrismId.NewId(),
                    TenantId = tenantId,
                    Actor = _actorResolver.Resolve(),
                    Action = action,
                    Entity = entity,
                    After = AuditSecretFilter.Redact(after),
                    CreatedAt = _timeProvider.GetUtcNow(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "'{Entity}' script'i denetim izine yazilamadi; calistirma reddedildi.", entity);

            throw new AgentPrismException(
                $"'{entity}' script'i denetim izine yazilamadigi icin calistirilmadi.",
                ex);
        }
    }

    private static void TryDelete(DirectoryInfo? directory)
    {
        if (directory is null)
        {
            return;
        }

        try
        {
            directory.Delete(recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Silinemeyen gecici dizin calistirmayi gecersiz kilmaz.
        }
    }

    private sealed record ScriptRequest(
        string SkillName,
        string ScriptName,
        string Extension,
        JsonElement? Schema,
        JsonElement? Arguments,
        bool Stored,
        string? ScriptPath,
        string? WorkingDirectory,
        string? Content);
}
