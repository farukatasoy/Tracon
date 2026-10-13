namespace Tracon;

/// <summary>
/// Starts a skill script that already passed every execution gate, and reports
/// what it did. Register your own implementation to run scripts somewhere other
/// than the Tracon host: a container, a micro-VM, or a remote runner service.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What stays with Tracon.</strong> The caller decides whether a script
/// may run before it calls this interface: the feature switch, the tenant's
/// <see cref="SkillScriptGrant"/> and its content pin, the interpreter allow-list,
/// the argument size and schema, and the audit trail entry. A request that fails
/// one of them never reaches an executor. After the call, Tracon applies the
/// concurrency quota, measures the duration, enforces the output limit again, and
/// writes the span, the metric, and the tool call record. An executor cannot skip
/// a gate, and it cannot write or suppress the audit entry.
/// </para>
/// <para>
/// <strong>What the executor owns.</strong> Where and how the script runs:
/// the process, its operating-system identity, its file system, its network, and
/// its CPU and memory limits. Tracon cannot see or check any of this. The script
/// runs where your executor runs it. The built-in default starts a local process
/// under the host's own operating-system identity.
/// </para>
/// <para>
/// <strong>Lifetime: singleton.</strong> Tracon resolves one instance when it
/// builds its script runner and calls it concurrently from many runs. An
/// implementation must be thread-safe. Register it before or after
/// <c>AddTracon()</c> with <c>AddSingleton</c>; both orders win over the
/// built-in default. <c>RequireCustomBinding&lt;ISkillScriptExecutor&gt;()</c>
/// stops the host when the built-in default is what resolves.
/// </para>
/// <para>
/// <strong>Tenant mode: expected tenant.</strong> The request carries the tenant
/// whose grant opened the gates (<see cref="SkillScriptExecutionRequest.TenantId"/>).
/// An implementation uses that value and never reads <c>ITenantContext</c>: the
/// call can run on a thread that belongs to no request.
/// </para>
/// <para>
/// <strong>Delivery: no delivery guarantee applies.</strong> Tracon calls the
/// executor once for each approved run and never retries. A failure is reported
/// to the model as a failed tool call; the model may call the script again, and
/// that new call passes every gate again.
/// </para>
/// <para>
/// <strong>Guarantee limit.</strong> Tracon does not isolate the script process
/// and cannot verify that an executor does. Cancellation is cooperative: Tracon
/// cancels the token when the caller stops or when <see cref="SkillScriptExecutionRequest.Timeout"/>
/// plus a fixed grace period has passed, and then stops waiting. It does not
/// force the work to stop. A script whose executor ignores the token can keep
/// running after Tracon recorded it as timed out.
/// </para>
/// <para>
/// Register it with <c>builder.Services.AddSingleton&lt;ISkillScriptExecutor, RunnerServiceScriptExecutor&gt;()</c>
/// and keep <c>UseSkillScripts</c>: the gates, the grant, and the interpreter
/// allow-list still decide what reaches the executor. With a remote executor an
/// allow-list value can be a runtime name, for example
/// <c>o.Interpreters["py"] = "python-3.12"</c>.
/// </para>
/// <example>
/// <code>
/// public sealed class RunnerServiceScriptExecutor(System.Net.Http.HttpClient runner) : ISkillScriptExecutor
/// {
///     private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
///
///     public async ValueTask&lt;SkillScriptExecutionResult&gt; ExecuteAsync(
///         SkillScriptExecutionRequest request, CancellationToken cancellationToken = default)
///     {
///         // The runner service gets the content, never a host path, and stops
///         // the script at the timeout. The tenant lets it pick an isolated runner.
///         using var response = await runner.PostAsync(
///             "runs",
///             System.Net.Http.Json.JsonContent.Create(new
///             {
///                 tenant = request.TenantId,
///                 runtime = request.Interpreter,
///                 source = request.Content,
///                 sourceSha256 = request.ContentSha256,
///                 standardInput = request.ArgumentsJson,
///                 timeoutSeconds = request.Timeout.TotalSeconds,
///                 maxOutputBytes = request.MaxOutputBytes,
///             }),
///             cancellationToken);
///
///         response.EnsureSuccessStatusCode();
///
///         var outcome = JsonSerializer.Deserialize&lt;RunnerOutcome&gt;(
///             await response.Content.ReadAsStringAsync(cancellationToken), Web)
///             ?? throw new InvalidOperationException("The runner service returned no outcome.");
///
///         return new SkillScriptExecutionResult
///         {
///             ExitCode = outcome.TimedOut ? null : outcome.ExitCode,
///             StandardOutput = outcome.StandardOutput,
///             StandardError = outcome.StandardError,
///             Truncated = outcome.Truncated,
///             TimedOut = outcome.TimedOut,
///         };
///     }
///
///     private sealed record RunnerOutcome(
///         int? ExitCode, string StandardOutput, string StandardError, bool Truncated, bool TimedOut);
/// }
/// </code>
/// </example>
/// </remarks>
public interface ISkillScriptExecutor
{
    /// <summary>Runs one script and reports its outcome.</summary>
    /// <param name="request">
    /// The script to run. Every gate already passed and the audit entry is
    /// already written when this method is called.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancelled when the caller stops the run, or when the timeout plus the
    /// grace period has passed and Tracon stops waiting.
    /// </param>
    /// <returns>
    /// The outcome. When the script runs past <see cref="SkillScriptExecutionRequest.Timeout"/>,
    /// stop it and return <see cref="SkillScriptExecutionResult.TimedOut"/> set to
    /// <see langword="true"/> and no exit code. Keep at most
    /// <see cref="SkillScriptExecutionRequest.MaxOutputBytes"/> UTF-8 bytes of each
    /// output stream and set <see cref="SkillScriptExecutionResult.Truncated"/> when
    /// you cut one.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// The caller cancelled <paramref name="cancellationToken"/>. Do not report a
    /// caller's cancellation as a timeout.
    /// </exception>
    /// <remarks>
    /// Any other exception fails the tool call: Tracon records it on the span, the
    /// metric, and the tool call record, and the model sees a failed call. Its
    /// message is not shown to the model or written to the tool call record.
    /// </remarks>
    ValueTask<SkillScriptExecutionResult> ExecuteAsync(
        SkillScriptExecutionRequest request,
        CancellationToken cancellationToken = default);
}
