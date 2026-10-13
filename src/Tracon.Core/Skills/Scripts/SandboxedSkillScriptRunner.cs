using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tracon;

/// <summary>
/// Tracon's single script execution path: it decides whether a skill script may
/// run, and hands an approved run to the registered <see cref="ISkillScriptExecutor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is not an operating-system sandbox.</strong> This class decides
/// WHETHER a script runs; the executor decides WHERE. The built-in executor starts
/// a process under the same operating-system user as Tracon, which can read what
/// that user can read, reach the network it can reach, and use the CPU and memory
/// it can use. Isolation is the host's job: a container, an unprivileged user, a
/// restricted network, or an executor that runs scripts somewhere else. What this
/// class adds are execution gates, a wait limit, output limits, a concurrency
/// limit, and the audit trail.
/// </para>
/// <para>
/// <strong>Microsoft Agent Framework never runs a script on its own.</strong>
/// <c>AgentFileSkillScriptRunner</c> is a call point; the gates, timeout,
/// resource limits, and audit trail are entirely Tracon's responsibility.
/// </para>
/// <para>
/// Every run passes through these gates in order. If one is closed, the
/// executor is <em>never called at all</em>:
/// </para>
/// <list type="number">
///   <item><description>Is the feature enabled (<see cref="TraconSkillScriptOptions.Enabled"/>)</description></item>
///   <item><description>
///   Is there a valid <see cref="SkillScriptGrant"/> for the tenant - and, for a
///   stored or code-defined script, does it pin the content that is about to run
///   (<see cref="SkillScriptGrant.ContentHash"/>)
///   </description></item>
///   <item><description>Is the extension on the interpreter allow-list</description></item>
///   <item><description>
///   Did the arguments pass size and schema validation, and is a stored script's
///   name a plain file name
///   </description></item>
///   <item><description>Was the audit trail entry written</description></item>
/// </list>
/// <para>
/// The last item is a <strong>deliberate exception</strong> to the
/// "observability does not break functionality" rule: a script run that
/// cannot be written to the audit trail would be a remote code execution
/// with no record at all. The entry is written here, in the runner, and never
/// in an executor: an executor that skipped it would run code with no record.
/// </para>
/// <para>
/// After the gates comes the concurrency quota, then the executor. The runner
/// waits for the executor at most the script timeout plus
/// <see cref="ExecutorGracePeriod"/>; past that it cancels the executor's token,
/// stops waiting, releases the quota, and records a timeout. The runner measures
/// the duration and applies the output limit again, whatever the executor returned.
/// </para>
/// <para>
/// MAF's approval flow stays active: <c>DisableRunSkillScriptApproval</c> is
/// not set, meaning every script call waits for user approval first.
/// </para>
/// </remarks>
internal sealed class SandboxedSkillScriptRunner : IDisposable
{
    /// <summary>The tool call record's error when the runner stopped waiting for the executor.</summary>
    private const string AbandonedError = "Timed out; the script executor did not return in time.";

    /// <summary>
    /// How long the runner waits for an executor past the script timeout before it
    /// stops waiting and records a timeout itself.
    /// </summary>
    /// <remarks>
    /// The built-in executor kills the process tree at the timeout and returns at
    /// once; the grace period covers a remote executor's round trip. It is fixed:
    /// it is a safety net for an executor that ignores its timeout, not a setting.
    /// The option validator keeps the timeout plus this period inside the range a
    /// timer accepts.
    /// </remarks>
    internal static readonly TimeSpan ExecutorGracePeriod = TimeSpan.FromSeconds(5);

    private static readonly ActivitySource ActivitySource = new(TraconDiagnostics.ActivitySourceName);

    private readonly IOptions<TraconOptions> _options;
    private readonly ITenantContext _tenantContext;
    private readonly ISkillScriptGrantStore _grantStore;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ISkillScriptExecutor _executor;
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
    /// <param name="executor">
    /// The executor an approved run is handed to. It is a constructor parameter
    /// because the service provider MAF passes to a script call is empty.
    /// </param>
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
        ISkillScriptExecutor executor,
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
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _tenantContext = tenantContext;
        _grantStore = grantStore;
        _auditLog = auditLog;
        _actorResolver = actorResolver;
        _executor = executor;
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
    /// <exception cref="TraconException">One of the gates is closed, or the executor failed.</exception>
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
    /// read-only to the process. The runner reads the file after the quota is
    /// taken and sends its text to the executor together with its path; files
    /// next to it are not carried.
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

        return await ExecuteAsync(
                new ScriptRequest(
                    skillName,
                    scriptName,
                    extension,
                    parametersSchema,
                    arguments,
                    SkillScriptOrigin.File,
                    SourcePath: fullPath,
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
    /// is enabled. The content travels to the executor in the request; the
    /// built-in executor writes it to a temporary directory accessible only to its
    /// owner, only for the duration of the run, and deletes it afterward.
    /// </para>
    /// <para>
    /// The grant must pin the content: a grant for <paramref name="scriptName"/>
    /// must carry the script's <see cref="AgentSkillScriptDefinition.ContentHash"/>,
    /// and a skill-wide grant the skill's <see cref="AgentSkillDefinition.ScriptSetHash"/>.
    /// A grant that pins nothing, or pins other content, refuses the run. The hash is
    /// computed from <paramref name="skill"/>, the definition that is about to run -
    /// the store is not read again between the check and the run, and the content
    /// sent to the executor comes from the same definition.
    /// </para>
    /// </remarks>
    /// <param name="skill">The skill that carries the script, as it will run.</param>
    /// <param name="scriptName">The name of the script in <paramref name="skill"/>.</param>
    /// <param name="arguments">The arguments produced by the model.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The text result to return to the model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="skill"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="scriptName"/> is empty.</exception>
    /// <exception cref="TraconException">One of the gates is closed, the skill has no such script, or the executor failed.</exception>
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
                    SkillScriptOrigin.Stored,
                    SourcePath: null,
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
        if (request.Origin == SkillScriptOrigin.Stored)
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

        // 🚨 A stored script's name becomes a file name in the built-in executor.
        // It is checked here, before the call, and again where that executor
        // writes the file: a custom executor that writes the content to disk must
        // not be the only line of defense, and the built-in one must not trust
        // its caller.
        if (request.Origin == SkillScriptOrigin.Stored && !SkillScriptNaming.IsSafeFileName(request.ScriptName))
        {
            await DenyAsync(
                    request.SkillName,
                    request.ScriptName,
                    "The script name is not a plain file name.",
                    cancellationToken,
                    activity)
                .ConfigureAwait(false);
        }

        // 🚨 The audit trail entry is written BEFORE execution, and is mandatory.
        // If it cannot be written, the run is denied; a code execution with
        // no record is unacceptable. It stays in the runner: an executor could
        // skip it.
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

        var startedAt = _timeProvider.GetTimestamp();
        SkillScriptExecutionResult? result;
        bool abandoned;
        var reading = request.Content is null;

        try
        {
            // A file script's content is read only now, after the quota: the
            // runner holds a slot while it reads, the same as while it runs.
            var content = request.Content
                ?? await File.ReadAllTextAsync(request.SourcePath!, cancellationToken).ConfigureAwait(false);

            reading = false;

            var executionRequest = new SkillScriptExecutionRequest
            {
                TenantId = tenantId,
                RunId = TraconRunContext.CurrentRunId,
                SkillName = request.SkillName,
                ScriptName = request.ScriptName,
                Origin = request.Origin,
                Extension = request.Extension,
                Interpreter = interpreter,
                Content = content,
                ContentSha256 = Sha256(content),
                SourcePath = request.SourcePath,
                ArgumentsJson = argumentsJson,
                Timeout = options.Timeout,
                MaxOutputBytes = options.MaxOutputBytes,
            };

            (result, abandoned) = await InvokeExecutorAsync(executionRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
        {
            var reason = reading
                ? "The script file could not be read."
                : $"The script executor failed ({ex.GetType().Name}).";

            throw await RecordFailureAsync(
                    request,
                    argumentsJson,
                    activity,
                    _timeProvider.GetElapsedTime(startedAt),
                    reason,
                    ex,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        var duration = _timeProvider.GetElapsedTime(startedAt);

        // A result the executor got wrong fails the call instead of reaching the
        // model: a timeout with an exit code, or neither, cannot be reported
        // honestly either way.
        if (result is null || CheckConsistency(result) is not null)
        {
            throw await RecordFailureAsync(
                    request,
                    argumentsJson,
                    activity,
                    duration,
                    $"The script executor {(result is null ? "returned no result" : CheckConsistency(result))}.",
                    exception: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // The output limit is applied again, whatever the executor returned: a
        // custom executor that ignores MaxOutputBytes must not be able to put more
        // in front of the model than the host allows.
        var truncated = result.Truncated;
        result = result with
        {
            StandardOutput = SkillScriptOutput.Cap(result.StandardOutput, options.MaxOutputBytes, ref truncated),
            StandardError = SkillScriptOutput.Cap(result.StandardError, options.MaxOutputBytes, ref truncated),
            Truncated = truncated,
        };

        activity?.SetTag(TraconDiagnostics.Tags.ExitCode, result.ExitCode);
        activity?.SetTag(TraconDiagnostics.Tags.DurationMs, duration.TotalMilliseconds);
        activity?.SetStatus(result.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error);

        _metrics?.RecordToolInvocation(
            TraconDiagnostics.SkillScriptToolName,
            result.Succeeded,
            duration);

        var text = Format(result, abandoned, options.MaxOutputBytes);

        await RecordInvocationAsync(
                request,
                argumentsJson,
                result.Succeeded ? text : null,
                duration,
                result.Succeeded
                    ? null
                    : abandoned ? AbandonedError
                    : result.TimedOut ? "Timed out." : $"Exit code {result.ExitCode}.",
                cancellationToken)
            .ConfigureAwait(false);

        return text;
    }

    /// <summary>
    /// Calls the executor and waits for it at most the script timeout plus
    /// <see cref="ExecutorGracePeriod"/>.
    /// </summary>
    /// <returns>
    /// The executor's result, or a timed-out result with <c>Abandoned</c> set when
    /// the wait limit passed first. The result is <see langword="null"/> when the
    /// executor broke its contract and returned none.
    /// </returns>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    private async Task<(SkillScriptExecutionResult? Result, bool Abandoned)> InvokeExecutorAsync(
        SkillScriptExecutionRequest request,
        CancellationToken cancellationToken)
    {
        // 🚨 The wait limit is a timer of its own: Timeout + grace. The option
        // validator bounds Timeout so that the sum stays inside the range a timer
        // accepts; a wider value would throw HERE, after the audit entry was
        // written and the quota taken.
        using var waitLimit = new CancellationTokenSource(request.Timeout + ExecutorGracePeriod, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, waitLimit.Token);

        // A synchronous throw from ExecuteAsync is an executor failure like any
        // other; the caller records it.
        var execution = _executor.ExecuteAsync(request, linked.Token).AsTask();

        try
        {
            return (await execution.WaitAsync(linked.Token).ConfigureAwait(false), false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && waitLimit.IsCancellationRequested)
        {
            // The executor ignored its timeout. Waiting stops here, the quota is
            // released when the caller returns, and the run is recorded as timed
            // out. The executor may still be running; its late outcome, success
            // or failure, is observed and dropped.
            ObserveLateOutcome(execution);

            if (_logger.IsEnabled(LogLevel.Warning))
            {
                _logger.LogWarning(
                    "Skill script {SkillName}/{ScriptName} did not return within its timeout of {Timeout} plus {GracePeriod}; " +
                    "it is recorded as timed out and may still be running in the script executor.",
                    request.SkillName,
                    request.ScriptName,
                    request.Timeout,
                    ExecutorGracePeriod);
            }

            return (new SkillScriptExecutionResult { TimedOut = true }, true);
        }
    }

    private static void ObserveLateOutcome(Task<SkillScriptExecutionResult> execution)
        => _ = execution.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    /// <summary>Names what is wrong with a result, or returns <see langword="null"/> when it is consistent.</summary>
    private static string? CheckConsistency(SkillScriptExecutionResult result)
    {
        if (result.StandardOutput is null || result.StandardError is null)
        {
            return "returned a result with no output text";
        }

        if (result.TimedOut && result.ExitCode is not null)
        {
            return "reported a timeout together with an exit code";
        }

        if (!result.TimedOut && result.ExitCode is null)
        {
            return "reported neither an exit code nor a timeout";
        }

        return null;
    }

    /// <summary>
    /// Records a run the executor could not complete, and returns the exception
    /// MAF reports to the model.
    /// </summary>
    /// <remarks>
    /// The exception's own message never reaches the model or the tool call
    /// record: an executor's message can carry anything, including the host's
    /// internals. It goes to the server log only.
    /// </remarks>
    private async ValueTask<TraconException> RecordFailureAsync(
        ScriptRequest request,
        string? argumentsJson,
        Activity? activity,
        TimeSpan duration,
        string reason,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Error))
        {
            _logger.LogError(
                exception,
                "Skill script {SkillName}/{ScriptName} failed: {Reason}",
                request.SkillName,
                request.ScriptName,
                reason);
        }

        activity?.SetTag(TraconDiagnostics.Tags.DurationMs, duration.TotalMilliseconds);
        activity?.SetStatus(ActivityStatusCode.Error, reason);

        _metrics?.RecordToolInvocation(TraconDiagnostics.SkillScriptToolName, succeeded: false, duration);

        await RecordInvocationAsync(request, argumentsJson, result: null, duration, reason, cancellationToken)
            .ConfigureAwait(false);

        var message = $"Script '{request.SkillName}/{request.ScriptName}' failed: {reason}";

        return exception is null ? new TraconException(message) : new TraconException(message, exception);
    }

    private static string Format(SkillScriptExecutionResult result, bool abandoned, int maxOutputBytes)
    {
        var builder = new StringBuilder();

        if (abandoned)
        {
            builder.AppendLine(
                "The script timed out. The script executor did not return in time, so the script may still be running.");
        }
        else if (result.TimedOut)
        {
            builder.AppendLine("The script timed out and was stopped.");
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

        if (result.Truncated)
        {
            builder.AppendLine(
                CultureInfo.InvariantCulture,
                $"[Tracon: output truncated at the {maxOutputBytes}-byte limit per stream.]");
        }

        return builder.ToString();
    }

    private static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    private async ValueTask RecordInvocationAsync(
        ScriptRequest request,
        string? argumentsJson,
        string? result,
        TimeSpan duration,
        string? error,
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
                    Result = result,
                    Duration = duration,
                    Error = error,
                    CreatedAt = _timeProvider.GetUtcNow(),

                    // The running run's own tenant; NOT the ambient tenant.
                    TenantId = TraconRunContext.Current?.TenantId,
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (OperationCancellation.IsFailure(ex, cancellationToken))
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
    /// executor, so the exit code and duration that every other outcome writes
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
                // syntax for type json") - the audit trail entry was never created.
                after: JsonSerializer.Serialize(reason, TraconCoreJsonContext.Default.String),
                cancellationToken)
            .ConfigureAwait(false);

        throw new TraconException($"Script '{skillName}/{scriptName}' was not run: {reason}");
    }

    private sealed record ScriptRequest(
        string SkillName,
        string ScriptName,
        string Extension,
        JsonElement? Schema,
        JsonElement? Arguments,
        SkillScriptOrigin Origin,
        string? SourcePath,
        string? Content,
        string? PinnedScriptHash,
        string? PinnedSetHash);
}
