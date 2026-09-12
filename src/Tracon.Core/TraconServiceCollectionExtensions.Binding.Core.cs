using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    /// <summary>Binds the two scalar <c>TraconOptions</c> fields that carry no sub-section.</summary>
    private static void BindCoreFields(IConfiguration section, TraconOptions options)
    {
        if (section[nameof(TraconOptions.DefaultTenantId)] is { Length: > 0 } tenantId)
        {
            options.DefaultTenantId = tenantId;
        }

        if (int.TryParse(
                section[nameof(TraconOptions.MaxParameterValueLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxParameterValueLength))
        {
            options.MaxParameterValueLength = maxParameterValueLength;
        }
    }

    /// <summary>Binds the <c>Tracon:Tools</c> section.</summary>
    private static void BindTools(IConfigurationSection section, TraconToolOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconToolOptions.DefaultTimeout)],
                CultureInfo.InvariantCulture,
                out var defaultTimeout))
        {
            options.DefaultTimeout = defaultTimeout;
        }

        if (int.TryParse(
                section[nameof(TraconToolOptions.DefaultMaxOutputBytes)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var defaultMaxOutputBytes))
        {
            options.DefaultMaxOutputBytes = defaultMaxOutputBytes;
        }

        if (bool.TryParse(section[nameof(TraconToolOptions.AllowUnverifiedToolRegistry)], out var allowUnverifiedToolRegistry))
        {
            options.AllowUnverifiedToolRegistry = allowUnverifiedToolRegistry;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconToolOptions.ApprovalPresentationTimeout)],
                CultureInfo.InvariantCulture,
                out var approvalPresentationTimeout))
        {
            options.ApprovalPresentationTimeout = approvalPresentationTimeout;
        }
    }

    /// <summary>Binds the <c>Tracon:Preflight</c> section.</summary>
    private static void BindPreflight(IConfigurationSection section, TraconPreflightOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconPreflightOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (double.TryParse(
                section[nameof(TraconPreflightOptions.ReserveRatio)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var reserveRatio))
        {
            options.ReserveRatio = reserveRatio;
        }
    }

    /// <summary>Binds the <c>Tracon:ModelConcurrency</c> section.</summary>
    private static void BindModelConcurrency(IConfigurationSection section, TraconModelConcurrencyOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(
                section[nameof(TraconModelConcurrencyOptions.MaxConcurrentCallsPerProvider)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxConcurrentCallsPerProvider))
        {
            options.MaxConcurrentCallsPerProvider = maxConcurrentCallsPerProvider;
        }
    }

    /// <summary>Binds the <c>Tracon:Validation</c> section.</summary>
    private static void BindValidation(IConfigurationSection section, TraconValidationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconValidationOptions.McpTimeout)],
                CultureInfo.InvariantCulture,
                out var mcpTimeout))
        {
            options.McpTimeout = mcpTimeout;
        }
    }

    private static void BindAudit(IConfigurationSection section, TraconAuditOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (section[nameof(TraconAuditOptions.ActorClaimType)] is { Length: > 0 } claimType)
        {
            options.ActorClaimType = claimType;
        }
    }

    private static void BindSkills(IConfigurationSection section, TraconSkillOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (int.TryParse(section[nameof(TraconSkillOptions.MaxSkillsPerAgent)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxSkills))
        {
            options.MaxSkillsPerAgent = maxSkills;
        }

        if (int.TryParse(section[nameof(TraconSkillOptions.MaxInstructionsLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxInstructions))
        {
            options.MaxInstructionsLength = maxInstructions;
        }

        if (int.TryParse(section[nameof(TraconSkillOptions.MaxResourceContentLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResourceContent))
        {
            options.MaxResourceContentLength = maxResourceContent;
        }

        if (int.TryParse(section[nameof(TraconSkillOptions.MaxResourcesPerSkill)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxResources))
        {
            options.MaxResourcesPerSkill = maxResources;
        }

        BindSkillScripts(section.GetSection(nameof(TraconSkillOptions.Scripts)), options.Scripts);
    }

    /// <summary>Binds script run settings from configuration.</summary>
    /// <remarks>
    /// <c>Enabled</c> and <c>PlatformIsolationAcknowledged</c> can deliberately
    /// also be read from here: a single deployment must be able to run script
    /// support on or off in different environments from the same image.
    /// Validation still requires both together; turning on only <c>Enabled</c> fails at startup.
    /// </remarks>
    private static void BindSkillScripts(IConfigurationSection section, TraconSkillScriptOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconSkillScriptOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(TraconSkillScriptOptions.PlatformIsolationAcknowledged), out var acknowledged))
        {
            options.PlatformIsolationAcknowledged = acknowledged;
        }

        // 🚨 AllowStoredScripts, SkillRoots and Interpreters are deliberately NOT
        // bound from configuration. All three widen what may be executed on the
        // server, and the shipped documentation states that script execution "can
        // only be turned on in code". Binding merged INTO the code-supplied values
        // instead of replacing them, so an environment variable such as
        // Tracon__Skills__Scripts__Interpreters__sh=/bin/sh added an
        // interpreter the application never approved, and K-087 had already
        // rejected configuration-supplied skill roots as arbitrary file system
        // reads. Enabled and PlatformIsolationAcknowledged stay bound on purpose
        // (see the comment above): they can only NARROW or acknowledge, never
        // widen the executable surface.

        if (TimeSpan.TryParse(section[nameof(TraconSkillScriptOptions.Timeout)], CultureInfo.InvariantCulture, out var timeout))
        {
            options.Timeout = timeout;
        }

        if (int.TryParse(section[nameof(TraconSkillScriptOptions.MaxOutputBytes)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOutput))
        {
            options.MaxOutputBytes = maxOutput;
        }

        if (int.TryParse(section[nameof(TraconSkillScriptOptions.MaxArgumentBytes)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxArguments))
        {
            options.MaxArgumentBytes = maxArguments;
        }

        if (int.TryParse(section[nameof(TraconSkillScriptOptions.MaxScriptContentLength)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxContent))
        {
            options.MaxScriptContentLength = maxContent;
        }

        if (int.TryParse(section[nameof(TraconSkillScriptOptions.MaxScriptsPerSkill)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxScripts))
        {
            options.MaxScriptsPerSkill = maxScripts;
        }

        if (int.TryParse(section[nameof(TraconSkillScriptOptions.MaxConcurrentPerTenant)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var perTenant))
        {
            options.MaxConcurrentPerTenant = perTenant;
        }

        if (int.TryParse(section[nameof(TraconSkillScriptOptions.MaxConcurrentTotal)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var total))
        {
            options.MaxConcurrentTotal = total;
        }

        if (int.TryParse(section[nameof(TraconSkillScriptOptions.SearchDepth)], NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth))
        {
            options.SearchDepth = depth;
        }

        BindList(section.GetSection(nameof(TraconSkillScriptOptions.EnvironmentAllowList)), options.EnvironmentAllowList);
    }

    /// <summary>Writes a configuration array into an existing list.</summary>
    /// <remarks>
    /// When the list is defined in configuration, the default content is
    /// <strong>fully</strong> replaced. If a merge were done instead, it would
    /// be impossible to narrow the environment variable allow-list.
    /// </remarks>
    private static void BindList(IConfigurationSection section, IList<string> target)
    {
        if (!section.Exists())
        {
            return;
        }

        var values = section.GetChildren()
            .Select(static child => child.Value)
            .Where(static value => value is { Length: > 0 })
            .ToArray();

        if (values.Length == 0)
        {
            return;
        }

        target.Clear();

        foreach (var value in values)
        {
            target.Add(value!);
        }
    }

    private static void BindCircuitBreaker(IConfigurationSection section, TraconCircuitBreakerOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconCircuitBreakerOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (int.TryParse(
                section[nameof(TraconCircuitBreakerOptions.FailureThreshold)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var failureThreshold))
        {
            options.FailureThreshold = failureThreshold;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconCircuitBreakerOptions.BreakDuration)],
                CultureInfo.InvariantCulture,
                out var breakDuration))
        {
            options.BreakDuration = breakDuration;
        }
    }

    private static void BindHealth(IConfigurationSection section, TraconHealthOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconHealthOptions.CacheTtl)],
                CultureInfo.InvariantCulture,
                out var cacheTtl))
        {
            options.CacheTtl = cacheTtl;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconHealthOptions.BackgroundInterval)],
                CultureInfo.InvariantCulture,
                out var backgroundInterval))
        {
            options.BackgroundInterval = backgroundInterval;
        }
    }

    private static void BindRunRecording(IConfigurationSection recording, TraconRunRecordingOptions options)
    {
        if (!recording.Exists())
        {
            return;
        }

        if (TryReadBool(recording, nameof(TraconRunRecordingOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(recording, nameof(TraconRunRecordingOptions.RecordRunInput), out var recordRunInput))
        {
            options.RecordRunInput = recordRunInput;
        }

        if (TryReadBool(recording, nameof(TraconRunRecordingOptions.RecordMessageDeltas), out var recordDeltas))
        {
            options.RecordMessageDeltas = recordDeltas;
        }

        if (TryReadBool(recording, nameof(TraconRunRecordingOptions.RecordToolPayloads), out var recordPayloads))
        {
            options.RecordToolPayloads = recordPayloads;
        }

        if (TryReadBool(
                recording,
                nameof(TraconRunRecordingOptions.RecordReasoningDeltas),
                out var recordReasoningDeltas))
        {
            options.RecordReasoningDeltas = recordReasoningDeltas;
        }

        if (int.TryParse(
                recording[nameof(TraconRunRecordingOptions.MaxPayloadLength)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxPayloadLength))
        {
            options.MaxPayloadLength = maxPayloadLength;
        }
    }

    private static void BindObservability(IConfigurationSection section, TraconObservabilityOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(TraconObservabilityOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (TryReadBool(section, nameof(TraconObservabilityOptions.PersistSpans), out var persistSpans))
        {
            options.PersistSpans = persistSpans;
        }

        if (TryReadBool(
                section,
                nameof(TraconObservabilityOptions.AlwaysPersistFailures),
                out var alwaysPersistFailures))
        {
            options.AlwaysPersistFailures = alwaysPersistFailures;
        }

        if (TryReadBool(
                section,
                nameof(TraconObservabilityOptions.RecordSensitiveData),
                out var recordSensitiveData))
        {
            options.RecordSensitiveData = recordSensitiveData;
        }

        if (double.TryParse(
                section[nameof(TraconObservabilityOptions.SuccessSampleRatio)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var sampleRatio))
        {
            options.SuccessSampleRatio = sampleRatio;
        }

        if (int.TryParse(
                section[nameof(TraconObservabilityOptions.MaxSpansPerRun)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxSpans))
        {
            options.MaxSpansPerRun = maxSpans;
        }

        if (TryReadBool(
                section,
                nameof(TraconObservabilityOptions.IncludeAgentVersionTag),
                out var includeAgentVersionTag))
        {
            options.IncludeAgentVersionTag = includeAgentVersionTag;
        }

        if (TryReadBool(
                section,
                nameof(TraconObservabilityOptions.EnableQuotaUsageGauge),
                out var enableQuotaUsageGauge))
        {
            options.EnableQuotaUsageGauge = enableQuotaUsageGauge;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconObservabilityOptions.QuotaUsageRefreshInterval)],
                CultureInfo.InvariantCulture,
                out var quotaUsageRefreshInterval))
        {
            options.QuotaUsageRefreshInterval = quotaUsageRefreshInterval;
        }

        if (TryReadBool(
                section,
                nameof(TraconObservabilityOptions.EnableJobQueueDepthGauge),
                out var enableJobQueueDepthGauge))
        {
            options.EnableJobQueueDepthGauge = enableJobQueueDepthGauge;
        }

        if (TimeSpan.TryParse(
                section[nameof(TraconObservabilityOptions.JobQueueDepthRefreshInterval)],
                CultureInfo.InvariantCulture,
                out var jobQueueDepthRefreshInterval))
        {
            options.JobQueueDepthRefreshInterval = jobQueueDepthRefreshInterval;
        }

        if (int.TryParse(
                section[nameof(TraconObservabilityOptions.MaxJobLaneCardinality)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxJobLaneCardinality))
        {
            options.MaxJobLaneCardinality = maxJobLaneCardinality;
        }
    }

    /// <summary>Binds the <c>Tracon:OnlineEvaluation</c> section.</summary>
    private static void BindOnlineEvaluation(IConfigurationSection section, OnlineEvaluationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        if (TryReadBool(section, nameof(OnlineEvaluationOptions.Enabled), out var enabled))
        {
            options.Enabled = enabled;
        }

        if (double.TryParse(
                section[nameof(OnlineEvaluationOptions.SampleRate)],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var sampleRate))
        {
            options.SampleRate = sampleRate;
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.MaxScoresPerHour)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var maxScoresPerHour))
        {
            options.MaxScoresPerHour = maxScoresPerHour;
        }

        var agentNames = section.GetSection(nameof(OnlineEvaluationOptions.AgentNames));

        if (agentNames.Exists())
        {
            var parsed = agentNames.GetChildren()
                .Select(static child => child.Value)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (parsed.Count > 0)
            {
                options.AgentNames.Clear();

                foreach (var name in parsed)
                {
                    options.AgentNames.Add(name!);
                }
            }
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.LowScoreThreshold)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var lowScoreThreshold))
        {
            options.LowScoreThreshold = lowScoreThreshold;
        }

        if (int.TryParse(
                section[nameof(OnlineEvaluationOptions.MinSampleSize)],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var minSampleSize))
        {
            options.MinSampleSize = minSampleSize;
        }

        if (TimeSpan.TryParse(
                section[nameof(OnlineEvaluationOptions.EvaluationWindow)],
                CultureInfo.InvariantCulture,
                out var evaluationWindow))
        {
            options.EvaluationWindow = evaluationWindow;
        }

        if (TimeSpan.TryParse(
                section[nameof(OnlineEvaluationOptions.JudgeTimeout)],
                CultureInfo.InvariantCulture,
                out var judgeTimeout))
        {
            options.JudgeTimeout = judgeTimeout;
        }
    }

    private static bool TryReadBool(IConfiguration section, string key, out bool value)
        => bool.TryParse(section[key], out value);
}
