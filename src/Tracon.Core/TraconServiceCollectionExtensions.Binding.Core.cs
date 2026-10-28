using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Tracon;

public static partial class TraconServiceCollectionExtensions
{
    /// <summary>Binds the two scalar <c>TraconOptions</c> fields that carry no sub-section.</summary>
    private static void BindCoreFields(IConfiguration section, TraconOptions options)
    {
        var read = ConfigurationValueReader.For(section, options);

        if (section[nameof(TraconOptions.DefaultTenantId)] is { Length: > 0 } tenantId)
        {
            options.DefaultTenantId = tenantId;
        }

        read.Int32(
            nameof(TraconOptions.MaxParameterValueLength),
            static o => o.MaxParameterValueLength,
            static (o, v) => o.MaxParameterValueLength = v);
    }

    /// <summary>Binds the <c>Tracon:Tools</c> section.</summary>
    private static void BindTools(IConfigurationSection section, TraconToolOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.TimeSpan(
            nameof(TraconToolOptions.DefaultTimeout),
            static o => o.DefaultTimeout,
            static (o, v) => o.DefaultTimeout = v);

        read.Int32(
            nameof(TraconToolOptions.DefaultMaxOutputBytes),
            static o => o.DefaultMaxOutputBytes,
            static (o, v) => o.DefaultMaxOutputBytes = v);

        read.Boolean(
            nameof(TraconToolOptions.AllowUnverifiedToolRegistry),
            static o => o.AllowUnverifiedToolRegistry,
            static (o, v) => o.AllowUnverifiedToolRegistry = v);

        read.TimeSpan(
            nameof(TraconToolOptions.ApprovalPresentationTimeout),
            static o => o.ApprovalPresentationTimeout,
            static (o, v) => o.ApprovalPresentationTimeout = v);

        read.Boolean(
            nameof(TraconToolOptions.RequireTenantPolicy),
            static o => o.RequireTenantPolicy,
            static (o, v) => o.RequireTenantPolicy = v);
    }

    /// <summary>Binds the <c>Tracon:Preflight</c> section.</summary>
    private static void BindPreflight(IConfigurationSection section, TraconPreflightOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Boolean(
            nameof(TraconPreflightOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Double(
            nameof(TraconPreflightOptions.ReserveRatio),
            static o => o.ReserveRatio,
            static (o, v) => o.ReserveRatio = v);
    }

    /// <summary>Binds the <c>Tracon:ModelConcurrency</c> section.</summary>
    private static void BindModelConcurrency(IConfigurationSection section, TraconModelConcurrencyOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Int32(
            nameof(TraconModelConcurrencyOptions.MaxConcurrentCallsPerProvider),
            static o => o.MaxConcurrentCallsPerProvider,
            static (o, v) => o.MaxConcurrentCallsPerProvider = v);
    }

    /// <summary>Binds the <c>Tracon:Validation</c> section.</summary>
    private static void BindValidation(IConfigurationSection section, TraconValidationOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.TimeSpan(
            nameof(TraconValidationOptions.McpTimeout),
            static o => o.McpTimeout,
            static (o, v) => o.McpTimeout = v);
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

    private static void BindSkills(IConfigurationSection section, TraconSkillOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Int32(
            nameof(TraconSkillOptions.MaxSkillsPerAgent),
            static o => o.MaxSkillsPerAgent,
            static (o, v) => o.MaxSkillsPerAgent = v);

        read.Int32(
            nameof(TraconSkillOptions.MaxInstructionsLength),
            static o => o.MaxInstructionsLength,
            static (o, v) => o.MaxInstructionsLength = v);

        read.Int32(
            nameof(TraconSkillOptions.MaxResourceContentLength),
            static o => o.MaxResourceContentLength,
            static (o, v) => o.MaxResourceContentLength = v);

        read.Int32(
            nameof(TraconSkillOptions.MaxResourcesPerSkill),
            static o => o.MaxResourcesPerSkill,
            static (o, v) => o.MaxResourcesPerSkill = v);

        BindSkillScripts(section.GetSection(nameof(TraconSkillOptions.Scripts)), options.Scripts, root);
    }

    /// <summary>Binds script run settings from configuration.</summary>
    /// <remarks>
    /// <c>Enabled</c> and <c>PlatformIsolationAcknowledged</c> can deliberately
    /// also be read from here: a single deployment must be able to run script
    /// support on or off in different environments from the same image.
    /// Validation still requires both together; turning on only <c>Enabled</c> fails at startup.
    /// </remarks>
    private static void BindSkillScripts(IConfigurationSection section, TraconSkillScriptOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Boolean(
            nameof(TraconSkillScriptOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Boolean(
            nameof(TraconSkillScriptOptions.PlatformIsolationAcknowledged),
            static o => o.PlatformIsolationAcknowledged,
            static (o, v) => o.PlatformIsolationAcknowledged = v);

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

        read.TimeSpan(
            nameof(TraconSkillScriptOptions.Timeout),
            static o => o.Timeout,
            static (o, v) => o.Timeout = v);

        read.Int32(
            nameof(TraconSkillScriptOptions.MaxOutputBytes),
            static o => o.MaxOutputBytes,
            static (o, v) => o.MaxOutputBytes = v);

        read.Int32(
            nameof(TraconSkillScriptOptions.MaxArgumentBytes),
            static o => o.MaxArgumentBytes,
            static (o, v) => o.MaxArgumentBytes = v);

        read.Int32(
            nameof(TraconSkillScriptOptions.MaxScriptContentLength),
            static o => o.MaxScriptContentLength,
            static (o, v) => o.MaxScriptContentLength = v);

        read.Int32(
            nameof(TraconSkillScriptOptions.MaxScriptsPerSkill),
            static o => o.MaxScriptsPerSkill,
            static (o, v) => o.MaxScriptsPerSkill = v);

        read.Int32(
            nameof(TraconSkillScriptOptions.MaxConcurrentPerTenant),
            static o => o.MaxConcurrentPerTenant,
            static (o, v) => o.MaxConcurrentPerTenant = v);

        read.Int32(
            nameof(TraconSkillScriptOptions.MaxConcurrentTotal),
            static o => o.MaxConcurrentTotal,
            static (o, v) => o.MaxConcurrentTotal = v);

        read.Int32(
            nameof(TraconSkillScriptOptions.SearchDepth),
            static o => o.SearchDepth,
            static (o, v) => o.SearchDepth = v);

        read.StringList(nameof(TraconSkillScriptOptions.EnvironmentAllowList), options.EnvironmentAllowList);
    }

    private static void BindCircuitBreaker(IConfigurationSection section, TraconCircuitBreakerOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Boolean(
            nameof(TraconCircuitBreakerOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Int32(
            nameof(TraconCircuitBreakerOptions.FailureThreshold),
            static o => o.FailureThreshold,
            static (o, v) => o.FailureThreshold = v);

        read.TimeSpan(
            nameof(TraconCircuitBreakerOptions.BreakDuration),
            static o => o.BreakDuration,
            static (o, v) => o.BreakDuration = v);
    }

    private static void BindHealth(IConfigurationSection section, TraconHealthOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.TimeSpan(
            nameof(TraconHealthOptions.CacheTtl),
            static o => o.CacheTtl,
            static (o, v) => o.CacheTtl = v);

        read.TimeSpan(
            nameof(TraconHealthOptions.BackgroundInterval),
            static o => o.BackgroundInterval,
            static (o, v) => o.BackgroundInterval = v);
    }

    private static void BindRunRecording(IConfigurationSection recording, TraconRunRecordingOptions options, TraconOptions root)
    {
        if (!recording.Exists())
        {
            return;
        }

        var recordingRead = ConfigurationValueReader.For(recording, options, root);

        recordingRead.Boolean(
            nameof(TraconRunRecordingOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        recordingRead.Boolean(
            nameof(TraconRunRecordingOptions.RecordRunInput),
            static o => o.RecordRunInput,
            static (o, v) => o.RecordRunInput = v);

        recordingRead.Boolean(
            nameof(TraconRunRecordingOptions.RecordMessageDeltas),
            static o => o.RecordMessageDeltas,
            static (o, v) => o.RecordMessageDeltas = v);

        recordingRead.Boolean(
            nameof(TraconRunRecordingOptions.RecordToolPayloads),
            static o => o.RecordToolPayloads,
            static (o, v) => o.RecordToolPayloads = v);

        recordingRead.Boolean(
            nameof(TraconRunRecordingOptions.RecordReasoningDeltas),
            static o => o.RecordReasoningDeltas,
            static (o, v) => o.RecordReasoningDeltas = v);

        recordingRead.Int32(
            nameof(TraconRunRecordingOptions.MaxPayloadLength),
            static o => o.MaxPayloadLength,
            static (o, v) => o.MaxPayloadLength = v);
    }

    private static void BindObservability(IConfigurationSection section, TraconObservabilityOptions options, TraconOptions root)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options, root);

        read.Boolean(
            nameof(TraconObservabilityOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Boolean(
            nameof(TraconObservabilityOptions.PersistSpans),
            static o => o.PersistSpans,
            static (o, v) => o.PersistSpans = v);

        read.Boolean(
            nameof(TraconObservabilityOptions.AlwaysPersistFailures),
            static o => o.AlwaysPersistFailures,
            static (o, v) => o.AlwaysPersistFailures = v);

        read.Boolean(
            nameof(TraconObservabilityOptions.RecordSensitiveData),
            static o => o.RecordSensitiveData,
            static (o, v) => o.RecordSensitiveData = v);

        read.Double(
            nameof(TraconObservabilityOptions.SuccessSampleRatio),
            static o => o.SuccessSampleRatio,
            static (o, v) => o.SuccessSampleRatio = v);

        read.Int32(
            nameof(TraconObservabilityOptions.MaxSpansPerRun),
            static o => o.MaxSpansPerRun,
            static (o, v) => o.MaxSpansPerRun = v);

        read.Boolean(
            nameof(TraconObservabilityOptions.IncludeAgentVersionTag),
            static o => o.IncludeAgentVersionTag,
            static (o, v) => o.IncludeAgentVersionTag = v);

        read.Boolean(
            nameof(TraconObservabilityOptions.IncludeTenantTag),
            static o => o.IncludeTenantTag,
            static (o, v) => o.IncludeTenantTag = v);

        read.Boolean(
            nameof(TraconObservabilityOptions.EnableQuotaUsageGauge),
            static o => o.EnableQuotaUsageGauge,
            static (o, v) => o.EnableQuotaUsageGauge = v);

        read.TimeSpan(
            nameof(TraconObservabilityOptions.QuotaUsageRefreshInterval),
            static o => o.QuotaUsageRefreshInterval,
            static (o, v) => o.QuotaUsageRefreshInterval = v);

        read.Boolean(
            nameof(TraconObservabilityOptions.EnableJobQueueDepthGauge),
            static o => o.EnableJobQueueDepthGauge,
            static (o, v) => o.EnableJobQueueDepthGauge = v);

        read.TimeSpan(
            nameof(TraconObservabilityOptions.JobQueueDepthRefreshInterval),
            static o => o.JobQueueDepthRefreshInterval,
            static (o, v) => o.JobQueueDepthRefreshInterval = v);

        read.Int32(
            nameof(TraconObservabilityOptions.MaxJobLaneCardinality),
            static o => o.MaxJobLaneCardinality,
            static (o, v) => o.MaxJobLaneCardinality = v);
    }

    /// <summary>Binds the <c>Tracon:OnlineEvaluation</c> section.</summary>
    private static void BindOnlineEvaluation(IConfigurationSection section, OnlineEvaluationOptions options)
    {
        if (!section.Exists())
        {
            return;
        }

        var read = ConfigurationValueReader.For(section, options);

        read.Boolean(
            nameof(OnlineEvaluationOptions.Enabled),
            static o => o.Enabled,
            static (o, v) => o.Enabled = v);

        read.Double(
            nameof(OnlineEvaluationOptions.SampleRate),
            static o => o.SampleRate,
            static (o, v) => o.SampleRate = v);

        read.Int32(
            nameof(OnlineEvaluationOptions.MaxScoresPerHour),
            static o => o.MaxScoresPerHour,
            static (o, v) => o.MaxScoresPerHour = v);

        read.StringList(nameof(OnlineEvaluationOptions.AgentNames), options.AgentNames);

        read.Int32(
            nameof(OnlineEvaluationOptions.LowScoreThreshold),
            static o => o.LowScoreThreshold,
            static (o, v) => o.LowScoreThreshold = v);

        read.Int32(
            nameof(OnlineEvaluationOptions.MinSampleSize),
            static o => o.MinSampleSize,
            static (o, v) => o.MinSampleSize = v);

        read.TimeSpan(
            nameof(OnlineEvaluationOptions.EvaluationWindow),
            static o => o.EvaluationWindow,
            static (o, v) => o.EvaluationWindow = v);

        read.TimeSpan(
            nameof(OnlineEvaluationOptions.JudgeTimeout),
            static o => o.JudgeTimeout,
            static (o, v) => o.JudgeTimeout = v);
    }

}
