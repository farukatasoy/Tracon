using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Tracon's single script execution path: it decides whether a skill script may
/// run and starts it as a separate process.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not an operating-system sandbox.</strong> The script process
/// runs under the same operating-system user as Tracon and can read what that
/// user can read, reach the network it can reach, and use the CPU and memory it
/// can use. Isolation is the host's job: a container, an unprivileged user, a
/// restricted network. What this class adds are execution gates, a timeout that
/// kills the process tree, output limits, a concurrency limit, and the audit trail.
/// </para>
/// <para>
/// <strong>Microsoft Agent Framework never runs a script on its own.</strong>
/// <c>AgentFileSkillScriptRunner</c> is a call point; the gates, timeout,
/// resource limits, and audit trail are entirely Tracon's responsibility.
/// </para>
/// <para>
/// Every run passes through these gates in order. If one is closed, the
/// script <em>never starts at all</em>:
/// </para>
/// <list type="number">
///   <item><description>Is the feature enabled (<see cref="TraconSkillScriptOptions.Enabled"/>)</description></item>
///   <item><description>
///   Is there a valid <see cref="SkillScriptGrant"/> for the tenant - and, for a
///   stored or code-defined script, does it pin the content that is about to run
///   (<see cref="SkillScriptGrant.ContentHash"/>)
///   </description></item>
///   <item><description>Is the extension on the interpreter allow-list</description></item>
///   <item><description>Did the arguments pass size and schema validation</description></item>
///   <item><description>Was the audit trail entry written</description></item>
/// </list>
/// <para>
/// The last item is a <strong>deliberate exception</strong> to the
/// "observability does not break functionality" rule: a script run that
/// cannot be written to the audit trail would be a remote code execution
/// with no record at all.
/// </para>
/// <para>
/// MAF's approval flow stays active: <c>DisableRunSkillScriptApproval</c> is
/// not set, meaning every script call waits for user approval first.
/// </para>
/// </remarks>
internal sealed class SandboxedSkillScriptRunner : IDisposable
{
    private static readonly ActivitySource ActivitySource = new(TraconDiagnostics.ActivitySourceName);

    private readonly IOptions<TraconOptions> _options;
    private readonly ITenantContext _tenantContext;
    private readonly ISkillScriptGrantStore _grantStore;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ILogger<SandboxedSkillScriptRunner> _logger;
    private readonly IRunStore? _runStore;
    private readonly TraconMetrics? _metrics;
    private readonly TimeProvider _timeProvider;
    private readonly SkillScriptConcurrencyLimiter _limiter;

    /// <summary>Creates a new runner.</summary>
    /// <param name="options">The Tracon settings.</param>
    /// <param name="tenantContext">The tenant context.</param>
    /// <param name="grantStore">The grant record store.</param>
    /// <param name="auditLog">The audit trail.</param>
    /// <param name="actorResolver">The actor resolver.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="runStore">The store the tool call record is written to. Not written if <see langword="null"/>.</param>
    /// <param name="metrics">The metrics instruments. No metrics are emitted if <see langword="null"/>.</param>
    /// <param name="timeProvider">The time source. The system clock is used if <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">One of the required dependencies is <see langword="null"/>.</exception>
    internal SandboxedSkillScriptRunner(
        IOptions<TraconOptions> options,
        ITenantContext tenantContext,
        ISkillScriptGrantStore grantStore,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ILogger<SandboxedSkillScriptRunner> logger,
        IRunStore? runStore = null,
        TraconMetrics? metrics = null,
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

    private TraconSkillScriptOptions Options => _options.Value.Skills.Scripts;

    /// <summary>
    /// Runs a skill script from disk. Wired to MAF's
    /// <c>AgentFileSkillScriptRunner</c> delegate.
    /// </summary>
    /// <param name="skill">The skill that carries the script.</param>
    /// <param name="script">The script to run.</param>
    /// <param name="arguments">The arguments produced by the model.</param>
    /// <param name="serviceProvider">The service provider given by MAF. Not used.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text result to return to the model.</returns>
    /// <exception cref="TraconException">One of the gates is closed.</exception>
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

        return await RunFileScriptAsync(
                skill.Frontmatter.Name,
                script.Name,
                script.FullPath,
                script.ParametersSchema,
                arguments,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Runs a script read from disk, from its parts.</summary>
    /// <remarks>
    /// The file path takes MAF's file skill types, which cannot be constructed
    /// outside MAF; this overload is the same path with plain values, so the gate
    /// order can be tested with a real file. A file script is not content-pinned:
    /// its content is part of the deployment, and the skill roots must be
    /// read-only to the process.
    /// </remarks>
    internal async Task<object?> RunFileScriptAsync(
        string skillName,
        string scriptName,
        string fullPath,
        JsonElement? parametersSchema,
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        // The span is opened in this method's own body: Activity.Current is
        // an AsyncLocal, and a span opened in a helper method does not flow
        // back to the caller.
        using var activity = ActivitySource.StartActivity(
            TraconDiagnostics.SkillScriptActivityName,
            ActivityKind.Internal);

        activity?.SetTag(TraconDiagnostics.Tags.SkillName, skillName);
        activity?.SetTag(TraconDiagnostics.Tags.ScriptName, scriptName);

        var extension = Path.GetExtension(fullPath).TrimStart('.');
        var workingDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory;

        return await ExecuteAsync(
                new ScriptRequest(
                    skillName,
                    scriptName,
                    extension,
                    parametersSchema,
                    arguments,
                    Stored: false,
                    ScriptPath: fullPath,
                    WorkingDirectory: workingDirectory,
                    Content: null,
                    PinnedScriptHash: null,
                    PinnedSetHash: null),
                activity,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a script of a skill that is stored in the database or registered in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This path is used only when <see cref="TraconSkillScriptOptions.AllowStoredScripts"/>
    /// is enabled. The content is written to a temporary directory accessible
    /// only to its owner, only for the duration of the run, and deleted
    /// afterward.
    /// </para>
    /// <para>
    /// The grant must pin the content: a grant for <paramref name="scriptName"/>
    /// must carry the script's <see cref="AgentSkillScriptDefinition.ContentHash"/>,
    /// and a skill-wide grant the skill's <see cref="AgentSkillDefinition.ScriptSetHash"/>.
    /// A grant that pins nothing, or pins other content, refuses the run. The hash is
    /// computed from <paramref name="skill"/>, the definition that is about to run -
    /// the store is not read again between the check and the run.
    /// </para>
    /// </remarks>
    /// <param name="skill">The skill that carries the script, as it will run.</param>
    /// <param name="scriptName">The name of the script in <paramref name="skill"/>.</param>
    /// <param name="arguments">The arguments produced by the model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text result to return to the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skill"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scriptName"/> is empty.</exception>
    /// <exception cref="TraconException">One of the gates is closed, or the skill has no such script.</exception>
    public async Task<object?> RunStoredScriptAsync(
        AgentSkillDefinition skill,
        string scriptName,
        JsonElement? arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skill);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptName);

        var skillName = skill.Name;

        if (!Options.AllowStoredScripts)
        {
            await DenyAsync(
                    skillName,
                    scriptName,
                    "Running stored scripts is disabled. AllowStoredScripts must be enabled.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var script = skill.Scripts.FirstOrDefault(candidate => string.Equals(candidate.Name, scriptName, StringComparison.Ordinal));

        if (script is null)
        {
            await DenyAsync(
                    skillName,
                    scriptName,
                    $"Skill '{skillName}' has no script named '{scriptName}'.",
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using var activity = ActivitySource.StartActivity(
            TraconDiagnostics.SkillScriptActivityName,
            ActivityKind.Internal);

        activity?.SetTag(TraconDiagnostics.Tags.SkillName, skillName);
        activity?.SetTag(TraconDiagnostics.Tags.ScriptName, script.Name);

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
                        "The script's argument schema is not valid JSON.",
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
                    Content: script.Content,
                    PinnedScriptHash: script.ContentHash,
                    PinnedSetHash: skill.ScriptSetHash),
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
                    "Skill script execution is disabled. It must be enabled with UseSkillScripts.",
                    cancellationToken,
                    activity)
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
                    "There is no valid execution grant for this script.",
                    cancellationToken,
                    activity)
                .ConfigureAwait(false);
        }

        // A stored or code-defined script runs only under a grant that pins the
        // content about to run. A file script is not pinned: its content is part
        // of the deployment, and the skill roots must be read-only to the process.
        if (request.Stored)
        {
            if (grant!.ContentHash is not { Length: > 0 } pinned)
            {
                await DenyAsync(
                        request.SkillName,
                        request.ScriptName,
                        "The execution grant does not pin the script content. Grant it again with the content hash.",
                        cancellationToken,
                        activity)
                    .ConfigureAwait(false);
            }
            else
            {
                // A narrow grant pins the script, a skill-wide grant the whole set.
                var current = grant.ScriptName is null ? request.PinnedSetHash : request.PinnedScriptHash;

                if (!string.Equals(pinned, current, StringComparison.OrdinalIgnoreCase))
                {
                    await DenyAsync(
                            request.SkillName,
                            request.ScriptName,
                            "The script content changed since the grant. Review it and grant it again.",
                            cancellationToken,
                            activity)
                        .ConfigureAwait(false);
                }
            }
        }

        if (!options.Interpreters.TryGetValue(request.Extension, out var interpreter))
        {
            await DenyAsync(
                    request.SkillName,
                    request.ScriptName,
                    $"Extension '{request.Extension}' is not on the interpreter allow-list.",
                    cancellationToken,
                    activity)
                .ConfigureAwait(false);
            interpreter = string.Empty;
        }

        var argumentsJson = request.Arguments?.GetRawText();

        if (argumentsJson is not null && Encoding.UTF8.GetByteCount(argumentsJson) > options.MaxArgumentBytes)
        {
            await DenyAsync(
                    request.SkillName,
                    request.ScriptName,
                    $"The arguments exceed the {options.MaxArgumentBytes}-byte limit.",
                    cancellationToken,
                    activity)
                .ConfigureAwait(false);
        }

        if (!SkillScriptArgumentValidator.TryValidate(request.Schema, request.Arguments, out var schemaError))
        {
            await DenyAsync(request.SkillName, request.ScriptName, schemaError!, cancellationToken, activity)
                .ConfigureAwait(false);
        }

        // 🚨 The audit trail entry is written BEFORE execution, and is mandatory.
        // If it cannot be written, the run is denied; a code execution with
        // no record is unacceptable.
        var auditEntity = $"{request.SkillName}/{request.ScriptName}";

        await AuditRecorder.WriteOrThrowAsync(
                _auditLog,
                _actorResolver.Resolve(),
                _logger,
                _metrics,
                tenantId,
                "script.run",
                auditEntity,
                before: null,
                after: argumentsJson,
                refusal: $"Script '{auditEntity}' was not run",
                _timeProvider,
                cancellationToken)
            .ConfigureAwait(false);

        using var lease = await _limiter.AcquireAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var scratch = request.Stored ? Directory.CreateTempSubdirectory("tracon-script-") : null;

        try
        {
            var scriptPath = request.ScriptPath;
            var workingDirectory = request.WorkingDirectory;

            if (scratch is not null)
            {
                // 🚨 The file name is validated HERE, at the boundary that writes
                // it. Path.Combine returns the second part unchanged when it is
                // rooted, and it does not resolve "..", so a stored script named
                // "/etc/cron.d/tracon" or "../../x" was written OUTSIDE the
                // scratch directory, executed from there, and left behind - the
                // scratch cleanup only removes the scratch directory. The save
                // endpoint checks the name too, but a runner that trusts its
                // caller is one refactor away from the same hole.
                var fileName = $"{SkillScriptNaming.RequireSafeFileName(request.ScriptName)}.{request.Extension}";

                scriptPath = Path.Combine(scratch.FullName, fileName);
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

            activity?.SetTag(TraconDiagnostics.Tags.ExitCode, result.ExitCode);
            activity?.SetTag(
                TraconDiagnostics.Tags.DurationMs,
                result.Duration.TotalMilliseconds);
            activity?.SetStatus(result.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

            _metrics?.RecordToolInvocation(
                TraconDiagnostics.SkillScriptToolName,
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
            builder.AppendLine("The script timed out and the process tree was terminated.");
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
        if (_runStore is null || TraconRunContext.CurrentRunId is not { } runId)
        {
            return;
        }

        try
        {
            await _runStore.RecordToolInvocationAsync(
                new ToolInvocationRecord
                {
                    Id = TraconId.NewId(),
                    RunId = runId,
                    ToolName = TraconDiagnostics.SkillScriptToolName,
                    Source = $"skill:{request.SkillName}",
                    Arguments = argumentsJson,
                    Result = result.Succeeded ? text : null,
                    Duration = result.Duration,
                    Error = result.Succeeded
                        ? null
                        : result.TimedOut ? "Timed out." : $"Exit code {result.ExitCode}.",
                    CreatedAt = _timeProvider.GetUtcNow(),

                    // The running run's own tenant; NOT the ambient tenant (K-355).
                    TenantId = TraconRunContext.Current?.TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The invocation record is observability; a failure here does not
            // break the run. The mandatory part is the audit trail, and that
            // has already been written.
            _logger.LogWarning(ex, "The skill script invocation could not be written to the tool_invocations table.");
        }
    }

    /// <summary>Writes the denial to the audit trail and stops the run.</summary>
    /// <remarks>
    /// <para>This method always throws; the return type only shortens the call site.</para>
    /// <para>
    /// The span is closed here as well. A denied run never reaches the
    /// process, so the exit code and duration that every other outcome writes
    /// are never set, and the span used to end carrying the skill and script
    /// names and nothing else - the shape of a span that never finished. The
    /// only trace of the denial was an error.type on MAF's own
    /// <c>execute_tool</c> span above it, which names the exception type and
    /// not the gate.
    /// </para>
    /// <para>
    /// <paramref name="activity"/> is passed rather than read from
    /// <see cref="Activity.Current"/>: the span belongs to the caller's body,
    /// and this repository does not touch the ambient one from an async
    /// helper.
    /// </para>
    /// </remarks>
    [DoesNotReturn]
    private async ValueTask DenyAsync(
        string skillName,
        string scriptName,
        string reason,
        CancellationToken cancellationToken,
        Activity? activity = null)
    {
        activity?.SetTag(TraconDiagnostics.Tags.ScriptDenialReason, reason);
        activity?.SetStatus(ActivityStatusCode.Error, reason);

        await AuditRecorder.WriteAsync(
                _auditLog,
                _actorResolver,
                _logger,
                _metrics,
                _tenantContext.TenantId,
                "script.denied",
                $"{skillName}/{scriptName}",
                before: null,
                // 'after' is written to a jsonb column; passing raw text was
                // silently dropping EVERY denial with 22P02 ("invalid input
                // syntax for type json") (HATA-K-skill-audit-json, 2026-08-15)
                // - the audit trail entry was never created.
                after: JsonSerializer.Serialize(reason, TraconCoreJsonContext.Default.String),
                cancellationToken)
            .ConfigureAwait(false);

        throw new TraconException($"Script '{skillName}/{scriptName}' was not run: {reason}");
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
            // A temporary directory that cannot be deleted does not invalidate the run.
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
        string? Content,
        string? PinnedScriptHash,
        string? PinnedSetHash);
}
