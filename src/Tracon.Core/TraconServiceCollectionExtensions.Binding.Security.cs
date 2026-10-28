using Microsoft.Extensions.Configuration;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    /// <summary>Binds the <c>Tracon:Approvals</c> section.</summary>
    private static void BindApproval(IConfigurationSection section, TraconApprovalOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.TimeSpan(
            nameof(TraconApprovalOptions.DefaultExpiration),
            static o => o.DefaultExpiration,
            static (o, v) => o.DefaultExpiration = v);

        read.Boolean(
            nameof(TraconApprovalOptions.ExpirationEnabled),
            static o => o.ExpirationEnabled,
            static (o, v) => o.ExpirationEnabled = v);

        read.TimeSpan(
            nameof(TraconApprovalOptions.ScanInterval),
            static o => o.ScanInterval,
            static (o, v) => o.ScanInterval = v);

        read.Int32(
            nameof(TraconApprovalOptions.MaxPerScan),
            static o => o.MaxPerScan,
            static (o, v) => o.MaxPerScan = v);
    }

    /// <summary>Binds the <c>Tracon:Egress</c> section.</summary>
    private static void BindEgress(IConfigurationSection section, TraconEgressOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconEgressOptions.AllowPrivateNetworkTargets),
            static o => o.AllowPrivateNetworkTargets,
            static (o, v) => o.AllowPrivateNetworkTargets = v);
    }

    private static void BindMcpSecurity(IConfigurationSection section, TraconMcpSecurityOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(TraconMcpSecurityOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }
    }

    /// <summary>Binds the <c>Tracon:TenantProviders</c> section.</summary>
    private static void BindTenantProviders(IConfigurationSection section, TraconTenantProviderOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(TraconTenantProviderOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconTenantProviderOptions.RequireEgressPolicy),
            static o => o.RequireEgressPolicy,
            static (o, v) => o.RequireEgressPolicy = v);
    }

    /// <summary>Binds the <c>Tracon:InboundTriggers</c> section.</summary>
    private static void BindInboundTriggers(IConfigurationSection section, TraconInboundTriggerOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.TimeSpan(
            nameof(TraconInboundTriggerOptions.TimestampTolerance),
            static o => o.TimestampTolerance,
            static (o, v) => o.TimestampTolerance = v);

        read.Int32(
            nameof(TraconInboundTriggerOptions.MaxBodyBytes),
            static o => o.MaxBodyBytes,
            static (o, v) => o.MaxBodyBytes = v);

        read.Int32(
            nameof(TraconInboundTriggerOptions.MaxRequestsPerMinute),
            static o => o.MaxRequestsPerMinute,
            static (o, v) => o.MaxRequestsPerMinute = v);

        if (section[nameof(TraconInboundTriggerOptions.AllowedConfigurationPrefix)] is { Length: > 0 } allowedPrefix)
        {
            options.AllowedConfigurationPrefix = allowedPrefix;
        }
    }

    /// <summary>Binds the <c>Tracon:ContentGuard</c> section.</summary>
    private static void BindContentGuard(IConfigurationSection section, TraconContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconContentGuardOptions.InspectInput),
            static o => o.InspectInput,
            static (o, v) => o.InspectInput = v);

        read.Boolean(
            nameof(TraconContentGuardOptions.InspectOutput),
            static o => o.InspectOutput,
            static (o, v) => o.InspectOutput = v);

        read.Boolean(
            nameof(TraconContentGuardOptions.BufferStreamingOutput),
            static o => o.BufferStreamingOutput,
            static (o, v) => o.BufferStreamingOutput = v);
    }

    /// <summary>Binds the <c>Tracon:ContentGuard:Pattern</c> section.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="PiiPatterns"/> is a <c>[Flags]</c> enum, written in
    /// configuration as a comma-separated name list (example:
    /// <c>"Email,CreditCard"</c>). <c>Enum.TryParse</c> is AOT-clean.
    /// </para>
    /// <para>
    /// An empty value means "not set", as before. A value that does not parse is
    /// NOT skipped silently: one typo failed the whole parse, the value stayed
    /// <see cref="PiiPatterns.None"/> and PII masking was silently off. The text is
    /// recorded and <see cref="UnreadableConfigurationValidator{TOptions}"/> names it at
    /// startup. A number with an undefined bit does parse; the options validator
    /// refuses it, whatever its source.
    /// </para>
    /// </remarks>
    private static void BindPatternContentGuard(IConfigurationSection section, PatternContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.StringList(nameof(PatternContentGuardOptions.DeniedTerms), options.DeniedTerms);

        read.Enum<PiiPatterns>(
            nameof(PatternContentGuardOptions.MaskedPii),
            static o => o.MaskedPii,
            static (o, v) => o.MaskedPii = v);

        if (section[nameof(PatternContentGuardOptions.MaskReplacement)] is { Length: > 0 } replacement)
        {
            options.MaskReplacement = replacement;
        }
    }

    /// <summary>Binds the <c>Tracon:ContentProtection</c> section.</summary>
    /// <remarks>
    /// <para>
    /// <see cref="ProtectedColumn"/> values are written in configuration as a
    /// list of names (example: <c>Columns:0 = "RunInput"</c>). <c>Enum.TryParse</c>
    /// and <c>Enum.IsDefined</c> are AOT-clean. A list that is present REPLACES
    /// the default (every column).
    /// </para>
    /// <para>
    /// Nothing is skipped without a word, the rule the scheduling collections follow.
    /// An unknown name, an empty entry or an undefined number used to be dropped:
    /// <c>["RunInput", "RunInputt"]</c> protected only <c>RunInput</c> and left the
    /// column the operator meant in plain text. Such an entry is now left out and
    /// recorded with its position and its text, and a key entry with an empty name is
    /// bound as written; in both cases the startup validators name the entry
    /// (<see cref="UnreadableConfigurationValidator{TOptions}"/> and
    /// <see cref="TraconContentProtectionOptionsValidator"/>), whether protection is on
    /// or off.
    /// </para>
    /// </remarks>
    private static void BindContentProtection(IConfigurationSection section, TraconContentProtectionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconContentProtectionOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        if (section[nameof(TraconContentProtectionOptions.ActiveKeyId)] is { Length: > 0 } activeKeyId)
        {
            options.ActiveKeyId = activeKeyId;
        }

        foreach (var child in section.GetSection(nameof(TraconContentProtectionOptions.Keys)).GetChildren())
        {
            // Bound as written, even empty: the validator names an empty entry
            // instead of the key id silently vanishing (an old row encrypted
            // under it would then fail to decrypt at run time).
            options.Keys[child.Key] = child.Value ?? string.Empty;
        }

        read.EnumList(nameof(TraconContentProtectionOptions.Columns), options.Columns);
    }

    /// <summary>Binds the <c>Tracon:Webhooks</c> section.</summary>
    private static void BindWebhooks(IConfigurationSection section, TraconWebhookOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconWebhookOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Boolean(
            nameof(TraconWebhookOptions.AllowPrivateNetworkTargets),
            static o => o.AllowPrivateNetworkTargets,
            static (o, v) => o.AllowPrivateNetworkTargets = v);

        read.Boolean(
            nameof(TraconWebhookOptions.AllowInsecureHttp),
            static o => o.AllowInsecureHttp,
            static (o, v) => o.AllowInsecureHttp = v);

        read.TimeSpan(
            nameof(TraconWebhookOptions.Timeout),
            static o => o.Timeout,
            static (o, v) => o.Timeout = v);

        read.TimeSpan(
            nameof(TraconWebhookOptions.SignatureTolerance),
            static o => o.SignatureTolerance,
            static (o, v) => o.SignatureTolerance = v);

        if (section[nameof(TraconWebhookOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }

        read.Int32(
            nameof(TraconWebhookOptions.MaxExtraHeaders),
            static o => o.MaxExtraHeaders,
            static (o, v) => o.MaxExtraHeaders = v);

        read.Int32(
            nameof(TraconWebhookOptions.MaxResponseBytes),
            static o => o.MaxResponseBytes,
            static (o, v) => o.MaxResponseBytes = v);

        read.Int32(
            nameof(TraconWebhookOptions.DisableAfterConsecutiveFailures),
            static o => o.DisableAfterConsecutiveFailures,
            static (o, v) => o.DisableAfterConsecutiveFailures = v);

        // F-247: a list that is present REPLACES the default. A delay that is not
        // readable is left out and named at startup with its position and its text
        // (F-572); skipping it with no word cut a rung off the retry ladder.
        read.TimeSpanList(nameof(TraconWebhookOptions.RetryDelays), options.RetryDelays);
    }

    /// <summary>Binds the <c>Tracon:Retention</c> section.</summary>
    private static void BindRetention(IConfigurationSection section, TraconRetentionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconRetentionOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Int32(
            nameof(TraconRetentionOptions.BatchSize),
            static o => o.BatchSize,
            static (o, v) => o.BatchSize = v);

        read.TimeSpan(
            nameof(TraconRetentionOptions.BatchDelay),
            static o => o.BatchDelay,
            static (o, v) => o.BatchDelay = v);

        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.RunEvents)), options.RunEvents, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.ToolInvocations)), options.ToolInvocations, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Spans)), options.Spans, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Jobs)), options.Jobs, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.WebhookDeliveries)), options.WebhookDeliveries, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.EvalCaseResults)), options.EvalCaseResults, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.WorkflowCheckpoints)), options.WorkflowCheckpoints, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.SkillScriptGrants)), options.SkillScriptGrants, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Attachments)), options.Attachments, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Sessions)), options.Sessions, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Conversations)), options.Conversations, options);

        // F-247: defined and resolved by ForTarget, but never read here - the
        // HATA-S1-005 shape once more. TraconRetentionOptionsBindingTests now
        // counts the targets by reflection, so a target missing here fails it.
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.IdempotencyKeys)), options.IdempotencyKeys, options);

        // HATA-S1-005: these four targets were added to ForTarget but were NOT
        // added HERE - a signature change is not considered complete until
        // applied at EVERY call site of the body (see AGENTS.md, the Phase 20 note).
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.RunInputs)), options.RunInputs, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.VoiceSessions)), options.VoiceSessions, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.RunScores)), options.RunScores, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.DocumentEmbeddings)), options.DocumentEmbeddings, options);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.WorkflowRequests)), options.WorkflowRequests, options);
    }

    private static void BindRetentionTarget(IConfigurationSection section, RetentionTargetOptions options, TraconRetentionOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Int32(
            nameof(RetentionTargetOptions.MaxAgeDays),
            static o => o.MaxAgeDays,
            static (o, v) => o.MaxAgeDays = v);

        read.Boolean(
            nameof(RetentionTargetOptions.Archive),
            static o => o.Archive,
            static (o, v) => o.Archive = v);
    }

    /// <summary>
    /// Binds the <c>Tracon:SessionOwnership</c> section.
    /// </summary>
    /// <param name="section">The configuration section.</param>
    /// <param name="options">The settings to fill in.</param>
    /// <remarks>
    /// Bound BY HAND like every other section here. A missing section leaves
    /// every default in place, which means ownership stays off.
    /// </remarks>
    private static void BindSessionOwnership(IConfigurationSection section, TraconSessionOwnershipOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(TraconSessionOwnershipOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Boolean(
            nameof(TraconSessionOwnershipOptions.RequireAuthenticatedOwner),
            static o => o.RequireAuthenticatedOwner,
            static (o, v) => o.RequireAuthenticatedOwner = v);

        read.Boolean(
            nameof(TraconSessionOwnershipOptions.RefuseUnownedSessions),
            static o => o.RefuseUnownedSessions,
            static (o, v) => o.RefuseUnownedSessions = v);

        // 🚨 Read with the empty string ACCEPTED, unlike most string settings
        // here: "" is the documented way to say "no caller gets an unfiltered
        // listing", and treating it as "not configured" would silently restore
        // the Operator default the deployment just tried to remove.
        if (section[nameof(TraconSessionOwnershipOptions.ManagementPolicy)] is { } managementPolicy)
        {
            options.ManagementPolicy = managementPolicy;
        }
    }
}
