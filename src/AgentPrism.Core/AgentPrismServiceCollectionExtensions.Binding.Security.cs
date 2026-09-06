using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace AgentPrism;

public static partial class AgentPrismServiceCollectionExtensions
{
    /// <summary>Binds the <c>AgentPrism:Approvals</c> section.</summary>
    private static void BindApproval(IConfigurationSection section, AgentPrismApprovalOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismApprovalOptions.DefaultExpiration)],
                CultureInfo.InvariantCulture,
                out var defaultExpiration))
        {
            options.DefaultExpiration = defaultExpiration;
        }

        if (TryReadBool(section, nameof(AgentPrismApprovalOptions.ExpirationEnabled), out var expirationEnabled))
        {
            options.ExpirationEnabled = expirationEnabled;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismApprovalOptions.ScanInterval)],
                CultureInfo.InvariantCulture,
                out var scanInterval))
        {
            options.ScanInterval = scanInterval;
        }

        if (int.TryParse(
                section[nameof(AgentPrismApprovalOptions.MaxPerScan)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxPerScan))
        {
            options.MaxPerScan = maxPerScan;
        }
    }

    /// <summary>Binds the <c>AgentPrism:Egress</c> section.</summary>
    private static void BindEgress(IConfigurationSection section, AgentPrismEgressOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismEgressOptions.AllowPrivateNetworkTargets), out var allowPrivate))
        {
            options.AllowPrivateNetworkTargets = allowPrivate;
        }
    }

    private static void BindMcpSecurity(IConfigurationSection section, AgentPrismMcpSecurityOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismMcpSecurityOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }
    }

    /// <summary>Binds the <c>AgentPrism:TenantProviders</c> section.</summary>
    private static void BindTenantProviders(IConfigurationSection section, AgentPrismTenantProviderOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(AgentPrismTenantProviderOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }
    }

    /// <summary>Binds the <c>AgentPrism:InboundTriggers</c> section.</summary>
    private static void BindInboundTriggers(IConfigurationSection section, AgentPrismInboundTriggerOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismInboundTriggerOptions.TimestampTolerance)],
                CultureInfo.InvariantCulture,
                out var timestampTolerance))
        {
            options.TimestampTolerance = timestampTolerance;
        }

        if (int.TryParse(
                section[nameof(AgentPrismInboundTriggerOptions.MaxBodyBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxBodyBytes))
        {
            options.MaxBodyBytes = maxBodyBytes;
        }

        if (int.TryParse(
                section[nameof(AgentPrismInboundTriggerOptions.MaxRequestsPerMinute)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxRequestsPerMinute))
        {
            options.MaxRequestsPerMinute = maxRequestsPerMinute;
        }

        if (section[nameof(AgentPrismInboundTriggerOptions.AllowedConfigurationPrefix)] is { Length: > 0 } allowedPrefix)
        {
            options.AllowedConfigurationPrefix = allowedPrefix;
        }
    }

    /// <summary>Binds the <c>AgentPrism:ContentGuard</c> section.</summary>
    private static void BindContentGuard(IConfigurationSection section, AgentPrismContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.InspectInput), out var inspectInput))
        {
            options.InspectInput = inspectInput;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.InspectOutput), out var inspectOutput))
        {
            options.InspectOutput = inspectOutput;
        }

        if (TryReadBool(section, nameof(AgentPrismContentGuardOptions.BufferStreamingOutput), out var buffer))
        {
            options.BufferStreamingOutput = buffer;
        }
    }

    /// <summary>Binds the <c>AgentPrism:ContentGuard:Pattern</c> section.</summary>
    /// <remarks>
    /// <see cref="PiiPatterns"/> is a <c>[Flags]</c> enum, written in
    /// configuration as a comma-separated name list (example:
    /// <c>"Email,CreditCard"</c>). <c>Enum.TryParse</c> is AOT-clean.
    /// </remarks>
    private static void BindPatternContentGuard(IConfigurationSection section, PatternContentGuardOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        BindList(section.GetSection(nameof(PatternContentGuardOptions.DeniedTerms)), options.DeniedTerms);

        if (Enum.TryParse<PiiPatterns>(section[nameof(PatternContentGuardOptions.MaskedPii)], ignoreCase: true, out var pii))
        {
            options.MaskedPii = pii;
        }

        if (section[nameof(PatternContentGuardOptions.MaskReplacement)] is { Length: > 0 } replacement)
        {
            options.MaskReplacement = replacement;
        }
    }

    /// <summary>Binds the <c>AgentPrism:ContentProtection</c> section.</summary>
    /// <remarks>
    /// <see cref="ProtectedColumn"/> values are written in configuration as a
    /// list of names (example: <c>Columns:0 = "RunInput"</c>). <c>Enum.TryParse</c>
    /// is AOT-clean; an unrecognized name is skipped rather than failing the
    /// whole bind, matching <see cref="BindList"/>'s tolerance elsewhere.
    /// </remarks>
    private static void BindContentProtection(IConfigurationSection section, AgentPrismContentProtectionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismContentProtectionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (section[nameof(AgentPrismContentProtectionOptions.ActiveKeyId)] is { Length: > 0 } activeKeyId)
        {
            options.ActiveKeyId = activeKeyId;
        }

        var keysSection = section.GetSection(nameof(AgentPrismContentProtectionOptions.Keys));

        if (keysSection.Exists())
        {
            foreach (var child in keysSection.GetChildren())
            {
                if (child.Value is { Length: > 0 } configurationKeyName)
                {
                    options.Keys[child.Key] = configurationKeyName;
                }
            }
        }

        var columnsSection = section.GetSection(nameof(AgentPrismContentProtectionOptions.Columns));

        if (columnsSection.Exists())
        {
            var parsed = new HashSet<ProtectedColumn>();

            foreach (var child in columnsSection.GetChildren())
            {
                if (child.Value is { Length: > 0 } value && Enum.TryParse<ProtectedColumn>(value, ignoreCase: true, out var column))
                {
                    parsed.Add(column);
                }
            }

            if (parsed.Count > 0)
            {
                options.Columns.Clear();

                foreach (var column in parsed)
                {
                    options.Columns.Add(column);
                }
            }
        }
    }

    /// <summary>Binds the <c>AgentPrism:Webhooks</c> section.</summary>
    private static void BindWebhooks(IConfigurationSection section, AgentPrismWebhookOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.AllowPrivateNetworkTargets), out var allowPrivate))
        {
            options.AllowPrivateNetworkTargets = allowPrivate;
        }

        if (TryReadBool(section, nameof(AgentPrismWebhookOptions.AllowInsecureHttp), out var allowHttp))
        {
            options.AllowInsecureHttp = allowHttp;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismWebhookOptions.Timeout)],
                CultureInfo.InvariantCulture,
                out var timeout))
        {
            options.Timeout = timeout;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismWebhookOptions.SignatureTolerance)],
                CultureInfo.InvariantCulture,
                out var tolerance))
        {
            options.SignatureTolerance = tolerance;
        }

        if (section[nameof(AgentPrismWebhookOptions.AllowedConfigurationPrefix)] is { Length: > 0 } prefix)
        {
            options.AllowedConfigurationPrefix = prefix;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.MaxExtraHeaders)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxExtraHeaders))
        {
            options.MaxExtraHeaders = maxExtraHeaders;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.MaxResponseBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxResponseBytes))
        {
            options.MaxResponseBytes = maxResponseBytes;
        }

        if (int.TryParse(
                section[nameof(AgentPrismWebhookOptions.DisableAfterConsecutiveFailures)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var disableAfter))
        {
            options.DisableAfterConsecutiveFailures = disableAfter;
        }

        var delays = section.GetSection(nameof(AgentPrismWebhookOptions.RetryDelays));

        if (delays.Exists())
        {
            var parsed = delays.GetChildren()
                .Select(child => TimeSpan.TryParse(child.Value, CultureInfo.InvariantCulture, out var delay) ? delay : TimeSpan.Zero)
                .Where(delay => delay > TimeSpan.Zero)
                .ToList();

            if (parsed.Count > 0)
            {
                options.RetryDelays.Clear();

                foreach (var delay in parsed)
                {
                    options.RetryDelays.Add(delay);
                }
            }
        }
    }

    /// <summary>Binds the <c>AgentPrism:Retention</c> section.</summary>
    private static void BindRetention(IConfigurationSection section, AgentPrismRetentionOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismRetentionOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(AgentPrismRetentionOptions.BatchSize)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var batchSize))
        {
            options.BatchSize = batchSize;
        }

        if (TimeSpan.TryParse(
                section[nameof(AgentPrismRetentionOptions.BatchDelay)],
                CultureInfo.InvariantCulture,
                out var batchDelay))
        {
            options.BatchDelay = batchDelay;
        }

        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.RunEvents)), options.RunEvents);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.ToolInvocations)), options.ToolInvocations);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Spans)), options.Spans);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Jobs)), options.Jobs);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.WebhookDeliveries)), options.WebhookDeliveries);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.EvalCaseResults)), options.EvalCaseResults);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.WorkflowCheckpoints)), options.WorkflowCheckpoints);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.SkillScriptGrants)), options.SkillScriptGrants);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Attachments)), options.Attachments);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Sessions)), options.Sessions);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.Conversations)), options.Conversations);

        // HATA-S1-005: these four targets were added to ForTarget but were NOT
        // added HERE - a signature change is not considered complete until
        // applied at EVERY call site of the body (see AGENTS.md, the Phase 20 note).
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.RunInputs)), options.RunInputs);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.VoiceSessions)), options.VoiceSessions);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.RunScores)), options.RunScores);
        BindRetentionTarget(section.GetSection(nameof(AgentPrismRetentionOptions.DocumentEmbeddings)), options.DocumentEmbeddings);
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
    /// Binds the <c>AgentPrism:SessionOwnership</c> section.
    /// </summary>
    /// <param name="section">The configuration section.</param>
    /// <param name="options">The settings to fill in.</param>
    /// <remarks>
    /// Bound BY HAND like every other section here. A missing section leaves
    /// every default in place, which means ownership stays off.
    /// </remarks>
    private static void BindSessionOwnership(IConfigurationSection section, AgentPrismSessionOwnershipOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(AgentPrismSessionOwnershipOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(AgentPrismSessionOwnershipOptions.RequireAuthenticatedOwner), out var requireOwner))
        {
            options.RequireAuthenticatedOwner = requireOwner;
        }

        // 🚨 Read with the empty string ACCEPTED, unlike most string settings
        // here: "" is the documented way to say "no caller gets an unfiltered
        // listing", and treating it as "not configured" would silently restore
        // the Operator default the deployment just tried to remove.
        if (section[nameof(AgentPrismSessionOwnershipOptions.ManagementPolicy)] is { } managementPolicy)
        {
            options.ManagementPolicy = managementPolicy;
        }
    }
}
