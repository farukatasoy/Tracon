using System.Globalization;
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

        if (TimeSpan.TryParse(
                section[nameof(TraconApprovalOptions.DefaultExpiration)],
                CultureInfo.InvariantCulture,
                out var defaultExpiration))
        {
            options.DefaultExpiration = defaultExpiration;
        }

        if (TryReadBool(section, nameof(TraconApprovalOptions.ExpirationEnabled), out var expirationEnabled))
        {
            options.ExpirationEnabled = expirationEnabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconApprovalOptions.ScanInterval)],
                CultureInfo.InvariantCulture,
                out var scanInterval))
        {
            options.ScanInterval = scanInterval;
        }

        if (int.TryParse(
                section[nameof(TraconApprovalOptions.MaxPerScan)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxPerScan))
        {
            options.MaxPerScan = maxPerScan;
        }
    }

    /// <summary>Binds the <c>Tracon:Egress</c> section.</summary>
    private static void BindEgress(IConfigurationSection section, TraconEgressOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconEgressOptions.AllowPrivateNetworkTargets), out var allowPrivate))
        {
            options.AllowPrivateNetworkTargets = allowPrivate;
        }
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
    }

    /// <summary>Binds the <c>Tracon:InboundTriggers</c> section.</summary>
    private static void BindInboundTriggers(IConfigurationSection section, TraconInboundTriggerOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconInboundTriggerOptions.TimestampTolerance)],
                CultureInfo.InvariantCulture,
                out var timestampTolerance))
        {
            options.TimestampTolerance = timestampTolerance;
        }

        if (int.TryParse(
                section[nameof(TraconInboundTriggerOptions.MaxBodyBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxBodyBytes))
        {
            options.MaxBodyBytes = maxBodyBytes;
        }

        if (int.TryParse(
                section[nameof(TraconInboundTriggerOptions.MaxRequestsPerMinute)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRequestsPerMinute))
        {
            options.MaxRequestsPerMinute = maxRequestsPerMinute;
        }

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

        if (TryReadBool(section, nameof(TraconContentGuardOptions.InspectInput), out var inspectInput))
        {
            options.InspectInput = inspectInput;
        }

        if (TryReadBool(section, nameof(TraconContentGuardOptions.InspectOutput), out var inspectOutput))
        {
            options.InspectOutput = inspectOutput;
        }

        if (TryReadBool(section, nameof(TraconContentGuardOptions.BufferStreamingOutput), out var buffer))
        {
            options.BufferStreamingOutput = buffer;
        }
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
    /// NOT skipped: one typo failed the whole parse, the value stayed
    /// <see cref="PiiPatterns.None"/> and PII masking was silently off. It is
    /// bound as a value with an undefined bit, and the text is kept beside it, so
    /// <see cref="PatternContentGuardOptionsValidator"/> names it at startup.
    /// </para>
    /// </remarks>
    private static void BindPatternContentGuard(IConfigurationSection section, PatternContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        BindList(section.GetSection(nameof(PatternContentGuardOptions.DeniedTerms)), options.DeniedTerms);

        if (section[nameof(PatternContentGuardOptions.MaskedPii)] is { } piiText && !string.IsNullOrWhiteSpace(piiText))
        {
            var pii = Enum.TryParse<PiiPatterns>(piiText, ignoreCase: true, out var parsed)
                ? parsed
                : PatternContentGuardOptionsValidator.UnreadableMaskedPii;

            options.MaskedPii = pii;

            if (PatternContentGuardOptionsValidator.UndefinedBits(pii) != PiiPatterns.None)
            {
                options.UnreadableMaskedPii = (piiText, pii);
            }
        }

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
    /// Nothing is skipped, the rule the scheduling collections follow. An unknown
    /// name, an empty entry or an undefined number used to be dropped:
    /// <c>["RunInput", "RunInputt"]</c> protected only <c>RunInput</c> and left the
    /// column the operator meant in plain text. Such an entry is now bound as an undefined
    /// <see cref="ProtectedColumn"/> value with its text kept beside it, and a key
    /// entry with an empty name is bound as written; in both cases
    /// <see cref="TraconContentProtectionOptionsValidator"/> names the entry at
    /// startup, whether protection is on or off.
    /// </para>
    /// </remarks>
    private static void BindContentProtection(IConfigurationSection section, TraconContentProtectionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconContentProtectionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

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

        var columns = section.GetSection(nameof(TraconContentProtectionOptions.Columns)).GetChildren().ToList();

        if (columns.Count > 0)
        {
            options.Columns.Clear();

            foreach (var child in columns)
            {
                BindProtectedColumn(child.Value ?? string.Empty, options);
            }
        }
    }

    /// <summary>Adds one <c>Columns</c> entry, or the undefined value that stands in for it.</summary>
    private static void BindProtectedColumn(string text, TraconContentProtectionOptions options)
    {
        // A comma would make Enum.TryParse OR two names into a third column
        // ("RunInput,ToolArguments" is AgentFileContent): one entry is one name.
        if (!text.Contains(',', StringComparison.Ordinal)
            && Enum.TryParse<ProtectedColumn>(text, ignoreCase: true, out var column)
            && Enum.IsDefined(column))
        {
            options.Columns.Add(column);

            return;
        }

        var placeholder = UnreadableColumnPlaceholder(text, options);
        options.Columns.Add(placeholder);
        options.UnreadableColumns[placeholder] = text;
    }

    /// <summary>
    /// Picks the undefined <see cref="ProtectedColumn"/> value that stands in for an
    /// entry the binder could not read. Each entry gets its own value, so two typos
    /// are two members of the set and the validator names both.
    /// </summary>
    private static ProtectedColumn UnreadableColumnPlaceholder(string text, TraconContentProtectionOptions options)
    {
        // An undefined number ("99") stands for itself when it is still free.
        if (!text.Contains(',', StringComparison.Ordinal)
            && Enum.TryParse<ProtectedColumn>(text, ignoreCase: true, out var number)
            && !Enum.IsDefined(number)
            && !options.Columns.Contains(number))
        {
            return number;
        }

        var candidate = -1;

        while (Enum.IsDefined((ProtectedColumn)candidate) || options.Columns.Contains((ProtectedColumn)candidate))
        {
            candidate--;
        }

        return (ProtectedColumn)candidate;
    }

    /// <summary>Binds the <c>Tracon:Webhooks</c> section.</summary>
    private static void BindWebhooks(IConfigurationSection section, TraconWebhookOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconWebhookOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(TraconWebhookOptions.AllowPrivateNetworkTargets), out var allowPrivate))
        {
            options.AllowPrivateNetworkTargets = allowPrivate;
        }

        if (TryReadBool(section, nameof(TraconWebhookOptions.AllowInsecureHttp), out var allowHttp))
        {
            options.AllowInsecureHttp = allowHttp;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconWebhookOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconWebhookOptions.SignatureTolerance)],
                CultureInfo.InvariantCulture,
                out var tolerance))
        {
            options.SignatureTolerance = tolerance;
        }

        if (section[nameof(TraconWebhookOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }

        if (int.TryParse(
                section[nameof(TraconWebhookOptions.MaxExtraHeaders)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxExtraHeaders))
        {
            options.MaxExtraHeaders = maxExtraHeaders;
        }

        if (int.TryParse(
                section[nameof(TraconWebhookOptions.MaxResponseBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResponseBytes))
        {
            options.MaxResponseBytes = maxResponseBytes;
        }

        if (int.TryParse(
                section[nameof(TraconWebhookOptions.DisableAfterConsecutiveFailures)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var disableAfter))
        {
            options.DisableAfterConsecutiveFailures = disableAfter;
        }

        // F-247, the F-385 rule: a list that is present REPLACES the default, and
        // a delay that does not parse is bound as zero rather than skipped, so
        // TraconWebhookOptionsValidator names it at startup. Skipping it (and a
        // zero or negative delay with it) cut a rung off the retry ladder without
        // a word. The text is kept by index so the message quotes what was
        // written, not the zero that stands in for it.
        var delays = section.GetSection(nameof(TraconWebhookOptions.RetryDelays)).GetChildren().ToList();

        if (delays.Count > 0)
        {
            options.RetryDelays.Clear();

            foreach (var child in delays)
            {
                if (TimeSpan.TryParse(child.Value, CultureInfo.InvariantCulture, out var delay))
                {
                    options.RetryDelays.Add(delay);
                }
                else
                {
                    options.UnreadableRetryDelays[options.RetryDelays.Count] = child.Value ?? string.Empty;
                    options.RetryDelays.Add(TimeSpan.Zero);
                }
            }
        }
    }

    /// <summary>Binds the <c>Tracon:Retention</c> section.</summary>
    private static void BindRetention(IConfigurationSection section, TraconRetentionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconRetentionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(TraconRetentionOptions.BatchSize)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var batchSize))
        {
            options.BatchSize = batchSize;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconRetentionOptions.BatchDelay)],
                CultureInfo.InvariantCulture,
                out var batchDelay))
        {
            options.BatchDelay = batchDelay;
        }

        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.RunEvents)), options.RunEvents);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.ToolInvocations)), options.ToolInvocations);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Spans)), options.Spans);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Jobs)), options.Jobs);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.WebhookDeliveries)), options.WebhookDeliveries);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.EvalCaseResults)), options.EvalCaseResults);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.WorkflowCheckpoints)), options.WorkflowCheckpoints);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.SkillScriptGrants)), options.SkillScriptGrants);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Attachments)), options.Attachments);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Sessions)), options.Sessions);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.Conversations)), options.Conversations);

        // F-247: defined and resolved by ForTarget, but never read here - the
        // HATA-S1-005 shape once more. TraconRetentionOptionsBindingTests now
        // counts the targets by reflection, so a target missing here fails it.
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.IdempotencyKeys)), options.IdempotencyKeys);

        // HATA-S1-005: these four targets were added to ForTarget but were NOT
        // added HERE - a signature change is not considered complete until
        // applied at EVERY call site of the body (see AGENTS.md, the Phase 20 note).
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.RunInputs)), options.RunInputs);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.VoiceSessions)), options.VoiceSessions);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.RunScores)), options.RunScores);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.DocumentEmbeddings)), options.DocumentEmbeddings);
        BindRetentionTarget(section.GetSection(nameof(TraconRetentionOptions.WorkflowRequests)), options.WorkflowRequests);
    }

    private static void BindRetentionTarget(IConfigurationSection section, RetentionTargetOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(RetentionTargetOptions.MaxAgeDays)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxAgeDays))
        {
            options.MaxAgeDays = maxAgeDays;
        }

        if (TryReadBool(section, nameof(RetentionTargetOptions.Archive), out var archive))
        {
            options.Archive = archive;
        }
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

        if (TryReadBool(section, nameof(TraconSessionOwnershipOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(TraconSessionOwnershipOptions.RequireAuthenticatedOwner), out var requireOwner))
        {
            options.RequireAuthenticatedOwner = requireOwner;
        }

        if (TryReadBool(section, nameof(TraconSessionOwnershipOptions.RefuseUnownedSessions), out var refuseUnowned))
        {
            options.RefuseUnownedSessions = refuseUnowned;
        }

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
