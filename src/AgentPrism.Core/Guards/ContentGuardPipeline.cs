using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPrism;

/// <summary>
/// Pipeline that runs the registered <see cref="IContentGuard"/> implementations
/// in sequence and records their decisions.
/// </summary>
/// <remarks>
/// <para>
/// Guards run in registration order and <strong>the strictest decision wins</strong>:
/// if a guard returns <see cref="ContentGuardAction.Mask"/> the text is replaced
/// and the <em>replaced version</em> is handed to the next guard; if a guard
/// returns <see cref="ContentGuardAction.Block"/> the chain stops immediately.
/// Because Block is the taxonomy's highest value, the outcome does not depend on
/// registration order.
/// </para>
/// <para>
/// <strong>If a guard throws, the run fails.</strong> The "observability does
/// not break functionality" rule does not apply here: a guard is a control, not
/// an observation tool, and content that cannot be inspected is not let through.
/// </para>
/// <para>
/// The <em>recording</em> of the decision, however, is subject to that rule: if
/// the event writer or the audit log fails, the failure is logged and the
/// decision is still applied. The decision itself is never lost.
/// </para>
/// </remarks>
public sealed class ContentGuardPipeline
{
    private const string UninspectableGuardName = "AgentPrism.ContentGuard";
    private const string UninspectableRuleName = "tool-result-not-inspectable";

    private readonly IContentGuard[] _guards;
    private readonly IOptionsMonitor<AgentPrismContentGuardOptions> _options;
    private readonly IAuditLog _auditLog;
    private readonly IAuditActorResolver _actorResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger _logger;

    /// <summary>Creates a new pipeline.</summary>
    /// <param name="guards">The registered guards. May be empty.</param>
    /// <param name="options">Pipeline settings.</param>
    /// <param name="auditLog">The audit log to write blocking decisions to.</param>
    /// <param name="actorResolver">The audit log actor resolver.</param>
    /// <param name="tenantContext">The tenant context to use when no run scope is available.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    /// <exception cref="ArgumentNullException">One of the dependencies is <see langword="null"/>.</exception>
    public ContentGuardPipeline(
        IEnumerable<IContentGuard> guards,
        IOptionsMonitor<AgentPrismContentGuardOptions> options,
        IAuditLog auditLog,
        IAuditActorResolver actorResolver,
        ITenantContext tenantContext,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(guards);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(auditLog);
        ArgumentNullException.ThrowIfNull(actorResolver);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _guards = [.. guards];
        _options = options;
        _auditLog = auditLog;
        _actorResolver = actorResolver;
        _tenantContext = tenantContext;
        _logger = loggerFactory.CreateLogger<ContentGuardPipeline>();
    }

    /// <summary>
    /// Whether at least one guard is registered.
    /// </summary>
    /// <remarks>
    /// If <see langword="false"/>, <c>ModelProviderRegistry</c>
    /// <strong>never adds</strong> the inspection wrapper to the pipeline: not
    /// even a single <c>if</c> runs on the model-call path.
    /// </remarks>
    public bool HasGuards => _guards.Length > 0;

    /// <summary>The pipeline's current settings.</summary>
    public AgentPrismContentGuardOptions Options => _options.CurrentValue;

    /// <summary>Runs a piece of text through every guard.</summary>
    /// <param name="direction">The direction of the inspection.</param>
    /// <param name="text">The text to inspect.</param>
    /// <param name="modelId">The identity of the model being called.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The masked text; <see langword="null"/> if no guard requested a change.
    /// A <see langword="null"/> return tells the caller it needs to rebuild
    /// nothing, and is the allocation-free path.
    /// </returns>
    /// <exception cref="AgentPrismContentBlockedException">
    /// A guard returned <see cref="ContentGuardAction.Block"/>.
    /// </exception>
    public async ValueTask<string?> InspectAsync(
        ContentGuardDirection direction,
        string text,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var scope = AgentPrismRunContext.Current;
        var current = text;
        string? masked = null;

        foreach (var guard in _guards)
        {
            var context = new ContentGuardContext
            {
                Direction = direction,
                Text = current,
                RunId = scope?.RunId,
                TenantId = scope?.TenantId ?? _tenantContext.TenantId,
                AgentName = scope?.AgentName,
                ModelId = modelId,
            };

            var result = await guard.InspectAsync(context, cancellationToken).ConfigureAwait(false);

            switch (result.Action)
            {
                case ContentGuardAction.Block:
                    await RecordAsync(guard, result, direction, scope, cancellationToken).ConfigureAwait(false);

                    throw new AgentPrismContentBlockedException(
                        $"Content was blocked by the '{guard.Name}' guard " +
                        $"(rule: {result.RuleName ?? "unknown"}, direction: {direction}). " +
                        (result.Reason ?? "No reason was reported.") +
                        " The blocked text is deliberately not recorded.")
                    {
                        GuardName = guard.Name,
                        RuleName = result.RuleName,
                        Direction = direction,
                    };

                case ContentGuardAction.Mask when result.MaskedText is { } replacement:
                    await RecordAsync(guard, result, direction, scope, cancellationToken).ConfigureAwait(false);
                    current = replacement;
                    masked = replacement;
                    break;

                default:
                    break;
            }
        }

        return masked;
    }

    /// <summary>
    /// Runs <paramref name="text"/> through every guard but <strong>records no
    /// decision</strong> — it writes to neither the event writer nor the audit log.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists for <see cref="RunRecordingAgent"/>'s <c>BeginRunAsync</c>:
    /// the text written to the <c>RunStarted</c> event and to
    /// <see cref="IRunInputStore"/> must be THE SAME as the guard's decision,
    /// but at this stage the run row (<c>runs</c>) does NOT exist
    /// yet. When <see cref="InspectAsync"/> finds a decision it calls
    /// <c>scope.Writer.AppendAsync</c>; without a <c>runs</c> row the store
    /// rejects it and the writer is PERMANENTLY disabled for the whole run
    /// (<see cref="RunEventWriter.IsDisabled"/>). This method applies THE SAME
    /// guard order and masking chain without carrying that risk; the actual
    /// decision recording happens when <see cref="ContentGuardingChatClient"/>
    /// calls <see cref="InspectAsync"/> normally on the way to the model.
    /// </para>
    /// <para>
    /// On a block, <strong>no exception is thrown</strong> — the caller should
    /// continue starting the run; the real block happens on the way to the
    /// model, and the run then closes with <c>Failed</c>/<c>content_blocked</c>.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The text to record; <see langword="null"/> if no guard requested a change
    /// (the caller should use the original text).
    /// </returns>
    public async ValueTask<string?> PreviewAsync(
        ContentGuardDirection direction,
        string text,
        string? modelId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var scope = AgentPrismRunContext.Current;
        var current = text;
        var changed = false;

        foreach (var guard in _guards)
        {
            var context = new ContentGuardContext
            {
                Direction = direction,
                Text = current,
                RunId = scope?.RunId,
                TenantId = scope?.TenantId ?? _tenantContext.TenantId,
                AgentName = scope?.AgentName,
                ModelId = modelId,
            };

            var result = await guard.InspectAsync(context, cancellationToken).ConfigureAwait(false);

            switch (result.Action)
            {
                case ContentGuardAction.Block:
                    // The blocked text is deliberately not recorded (in the
                    // spirit of K-059, same rationale as InspectAsync's
                    // exception message).
                    return "[content_blocked]";

                case ContentGuardAction.Mask when result.MaskedText is { } replacement:
                    current = replacement;
                    changed = true;
                    break;

                default:
                    break;
            }
        }

        return changed ? current : null;
    }

    /// <summary>
    /// Records that a tool result could not be normalized into inspectable
    /// text and was unconditionally replaced. This is AgentPrism's own
    /// fail-closed decision, not a registered <see cref="IContentGuard"/>'s,
    /// so it carries a synthetic guard identity instead of a real one.
    /// </summary>
    /// <remarks>
    /// Called only from the real decision path (<c>ContentGuardMessageMasker.MaskAsync</c>),
    /// never from the preview path — same rationale as <see cref="RecordAsync"/>:
    /// before the <c>runs</c> row exists, writing an event permanently disables
    /// the writer for the whole run.
    /// </remarks>
    internal static async ValueTask RecordUninspectableToolResultAsync(
        ContentGuardDirection direction,
        CancellationToken cancellationToken)
    {
        if (AgentPrismRunContext.Current?.Writer is not { } writer)
        {
            return;
        }

        await writer.AppendAsync(
            new RunEventDraft(RunEventType.ContentMasked)
            {
                Text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{UninspectableGuardName}/{UninspectableRuleName} ({direction})"),
                Payload = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{{\"guard\":\"{UninspectableGuardName}\",\"rule\":\"{UninspectableRuleName}\",\"direction\":\"{direction}\",\"action\":\"Mask\"}}"),
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the decision to the run event, and a block additionally to the audit log.
    /// </summary>
    /// <remarks>
    /// What is written is <strong>not the content</strong>: the guard name, the
    /// rule name, and the direction. Blocked content is by definition sensitive;
    /// writing it to a log would make the problem permanent (in the spirit of
    /// the same direction as <c>AuditSecretFilter</c>).
    /// </remarks>
    private async ValueTask RecordAsync(
        IContentGuard guard,
        ContentGuardResult result,
        ContentGuardDirection direction,
        AgentRunScope? scope,
        CancellationToken cancellationToken)
    {
        var blocked = result.Action == ContentGuardAction.Block;
        var summary = string.Create(
            CultureInfo.InvariantCulture,
            $"{guard.Name}/{result.RuleName ?? "unknown"} ({direction})");

        if (scope?.Writer is { } writer)
        {
            await writer.AppendAsync(
                new RunEventDraft(blocked ? RunEventType.ContentBlocked : RunEventType.ContentMasked)
                {
                    Text = summary,
                    Payload = Describe(guard, result, direction),
                },
                cancellationToken).ConfigureAwait(false);
        }

        if (!blocked)
        {
            return;
        }

        await AuditRecorder.WriteAsync(
            _auditLog,
            _actorResolver,
            _logger,
            scope?.TenantId ?? _tenantContext.TenantId,
            action: "content.blocked",
            entity: scope is { } run
                ? string.Create(CultureInfo.InvariantCulture, $"run:{run.RunId}")
                : "run:unknown",
            before: null,
            after: Describe(guard, result, direction),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Describes the decision as JSON. Carries no content.</summary>
    /// <remarks>
    /// Formatted by hand: <c>AgentPrism.Core</c> is AOT-compatible, and opening a
    /// <c>JsonSerializerContext</c> entry for an object this small is unnecessary
    /// (the same rationale was used for tool argument formatting).
    /// </remarks>
    private static string Describe(IContentGuard guard, ContentGuardResult result, ContentGuardDirection direction)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"guard\":\"{Escape(guard.Name)}\",\"rule\":\"{Escape(result.RuleName)}\",\"direction\":\"{direction}\",\"action\":\"{result.Action}\"}}");

    private static string Escape(string? value)
        => value is null ? string.Empty : value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
