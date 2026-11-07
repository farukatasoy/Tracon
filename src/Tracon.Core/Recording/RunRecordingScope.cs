using System.Diagnostics;

namespace Tracon;

/// <summary>The recording state of a single run.</summary>
internal sealed record RunRecordingScope(
    RunEventWriter Writer,
    Activity? Activity,
    ToolInvocationTracker Tools,
    string AgentName,
    string TenantId,
    long StartedAt,
    AgentRunBudget? Budget,
    SideChannelUsageAccumulator? ExtraUsage,
    bool OwnsTrace,
    int? AgentVersion,
    Guid RunId,
    Guid RootRunId,
    int Depth,
    string? SessionId,
    RunKind Kind,
    FallbackModelAttribution? FallbackAttribution,
    string? UserId,
    NonTokenCostAccumulator? NonTokenCost,
    HostedCallLedger? HostedCalls = null,
    ToolCallLedger? ToolCalls = null);
