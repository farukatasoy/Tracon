namespace AgentPrism;

/// <summary>Defines data retention and archival options.</summary>
/// <remarks>
/// <para>
/// Read from the <c>AgentPrism:Retention</c> configuration section. This class
/// carries only <strong>configuration-based defaults</strong>; the source of
/// truth is the <c>retention_policies</c> database table through
/// <see cref="IRetentionPolicyStore"/>. These settings are ignored when a
/// database record exists for a target.
/// </para>
/// <para>
/// <see cref="Enabled"/> defaults to <see langword="false"/>. A package
/// upgrade must not delete data before a consumer adds configuration. This flag
/// must explicitly be <see langword="true"/> before configuration defaults apply.
/// </para>
/// </remarks>
public sealed class AgentPrismRetentionOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "AgentPrism:Retention";

    /// <summary>
    /// Gets or sets whether configuration-based defaults are enabled. When
    /// disabled, no data is deleted even if target settings below are read; only
    /// explicit database policies apply.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the maximum row count in a deletion batch.</summary>
    public int BatchSize { get; set; } = 5000;

    /// <summary>Gets or sets the delay between batches to avoid saturating production load.</summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Gets the default for <see cref="RetentionTargets.RunEvents"/>.</summary>
    public RetentionTargetOptions RunEvents { get; } = new() { MaxAgeDays = 30 };

    /// <summary>Gets the default for <see cref="RetentionTargets.ToolInvocations"/>.</summary>
    public RetentionTargetOptions ToolInvocations { get; } = new() { MaxAgeDays = 90 };

    /// <summary>Gets the default for <see cref="RetentionTargets.Traces"/>, including traces and spans.</summary>
    public RetentionTargetOptions Spans { get; } = new() { MaxAgeDays = 14 };

    /// <summary>Gets the default for <see cref="RetentionTargets.Jobs"/>, limited to completed jobs.</summary>
    public RetentionTargetOptions Jobs { get; } = new() { MaxAgeDays = 30 };

    /// <summary>Gets the default for <see cref="RetentionTargets.WebhookDeliveries"/>, limited to delivered webhooks.</summary>
    public RetentionTargetOptions WebhookDeliveries { get; } = new() { MaxAgeDays = 7 };

    /// <summary>Gets the default for <see cref="RetentionTargets.EvalCaseResults"/>.</summary>
    public RetentionTargetOptions EvalCaseResults { get; } = new() { MaxAgeDays = 180 };

    /// <summary>Gets the default for <see cref="RetentionTargets.WorkflowCheckpoints"/> after a run completes.</summary>
    public RetentionTargetOptions WorkflowCheckpoints { get; } = new() { MaxAgeDays = 7 };

    /// <summary>Gets the default for expired or revoked <see cref="RetentionTargets.SkillScriptGrants"/>.</summary>
    public RetentionTargetOptions SkillScriptGrants { get; } = new() { MaxAgeDays = 30 };

    /// <summary>Gets the default for orphaned <see cref="RetentionTargets.Attachments"/>.</summary>
    public RetentionTargetOptions Attachments { get; } = new() { MaxAgeDays = 7 };

    /// <summary>
    /// Gets the default for <see cref="RetentionTargets.Sessions"/>. Its
    /// <see cref="RetentionTargetOptions.MaxAgeDays"/> defaults to <see langword="null"/>:
    /// this is user data, supported but disabled.
    /// </summary>
    public RetentionTargetOptions Sessions { get; } = new();

    /// <summary>
    /// Gets the default for <see cref="RetentionTargets.Conversations"/>. Its
    /// <see cref="RetentionTargetOptions.MaxAgeDays"/> defaults to <see langword="null"/>:
    /// this is user data, supported but disabled.
    /// </summary>
    public RetentionTargetOptions Conversations { get; } = new();

    /// <summary>
    /// Gets the default for <see cref="RetentionTargets.IdempotencyKeys"/>.
    /// The stored response was already sent to the client, so it does not expose
    /// new data, but its lifetime is still bounded (43.5).
    /// </summary>
    public RetentionTargetOptions IdempotencyKeys { get; } = new() { MaxAgeDays = 1 };

    /// <summary>
    /// Gets the default for <see cref="RetentionTargets.RunInputs"/>.
    /// It is not user data; see the note on <see cref="RetentionTargets.RunInputs"/>.
    /// It is absent from <see cref="RetentionTargets.UserDataTargets"/>, so the
    /// configuration-based default applies.
    /// </summary>
    public RetentionTargetOptions RunInputs { get; } = new() { MaxAgeDays = 30 };

    /// <summary>
    /// Gets the default for <see cref="RetentionTargets.VoiceSessions"/>.
    /// The record contains summary metrics only, not audio bytes; see the target note.
    /// </summary>
    public RetentionTargetOptions VoiceSessions { get; } = new() { MaxAgeDays = 30 };

    /// <summary>
    /// Gets the default for <see cref="RetentionTargets.RunScores"/>.
    /// It has the same lifetime class as <see cref="EvalCaseResults"/>: evaluation history.
    /// </summary>
    public RetentionTargetOptions RunScores { get; } = new() { MaxAgeDays = 180 };

    /// <summary>
    /// Gets the default for <see cref="RetentionTargets.DocumentEmbeddings"/>.
    /// <see cref="RetentionTargetOptions.MaxAgeDays"/> defaults to <see langword="null"/>:
    /// knowledge-base content is reference data uploaded by the user, not a log
    /// or event. Automatic deletion must be explicitly enabled.
    /// </summary>
    public RetentionTargetOptions DocumentEmbeddings { get; } = new();

    /// <summary>Returns the option object that corresponds to a target name.</summary>
    /// <param name="target">See <see cref="RetentionTargets"/>.</param>
    /// <returns>The option object, or <see langword="null"/> for an unknown target.</returns>
    internal RetentionTargetOptions? ForTarget(string target)
        => target switch
        {
            RetentionTargets.RunEvents => RunEvents,
            RetentionTargets.ToolInvocations => ToolInvocations,
            RetentionTargets.Traces => Spans,
            RetentionTargets.Jobs => Jobs,
            RetentionTargets.WebhookDeliveries => WebhookDeliveries,
            RetentionTargets.EvalCaseResults => EvalCaseResults,
            RetentionTargets.WorkflowCheckpoints => WorkflowCheckpoints,
            RetentionTargets.SkillScriptGrants => SkillScriptGrants,
            RetentionTargets.Attachments => Attachments,
            RetentionTargets.Sessions => Sessions,
            RetentionTargets.Conversations => Conversations,
            RetentionTargets.IdempotencyKeys => IdempotencyKeys,
            RetentionTargets.RunInputs => RunInputs,
            RetentionTargets.VoiceSessions => VoiceSessions,
            RetentionTargets.RunScores => RunScores,
            RetentionTargets.DocumentEmbeddings => DocumentEmbeddings,
            _ => null,
        };
}

/// <summary>Defines a configuration-based retention default for one target.</summary>
public sealed class RetentionTargetOptions
{
    /// <summary>
    /// Gets or sets the age after which rows are eligible for deletion. <see
    /// langword="null"/> disables the default for this target.
    /// </summary>
    public int? MaxAgeDays { get; set; }

    /// <summary>Gets or sets whether to archive before deletion.</summary>
    public bool Archive { get; set; }
}
