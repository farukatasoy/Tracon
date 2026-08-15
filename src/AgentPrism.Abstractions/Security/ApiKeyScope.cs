using System.Text.Json.Serialization;

namespace AgentPrism;

/// <summary>An authority scope an API key may open.</summary>
/// <remarks>
/// <para>
/// A scope <strong>does not replace role policies</strong>, it narrows them.
/// A key's effective authority is the <c>role ∩ scope</c> set
/// (docs/53-KIRACI-API-ANAHTARLARI.md, section 53.3).
/// </para>
/// <para>
/// The scope list is <strong>closed</strong>: free-text scopes are not
/// accepted, an unknown value is rejected at creation time. Adding a new
/// member to this type is NOT a breaking change; making the list extensible
/// from the UI requires a separate, deliberate decision (an authority
/// language is a security surface).
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ApiKeyScope>))]
public enum ApiKeyScope
{
    /// <summary>Run reads, event stream, statistics.</summary>
    RunsRead = 0,

    /// <summary>Starting a run, cancelling, giving approval.</summary>
    RunsWrite = 1,

    /// <summary>Catalog and definition reads.</summary>
    AgentsRead = 2,

    /// <summary>Definition writes, version rollback.</summary>
    AgentsAdmin = 3,

    /// <summary>
    /// The external surface (MCP server, A2A). Kept separate: an internal
    /// automation key must not open the externally exposed surface on its own.
    /// </summary>
    ExternalInvoke = 4,

    /// <summary>Knowledge-base reads: collection listing, semantic search.</summary>
    KnowledgeRead = 5,

    /// <summary>Knowledge-base writes: document upload, deletion.</summary>
    KnowledgeAdmin = 6,

    /// <summary>Workflow definition reads: catalog listing, graph, checkpoint/request listing.</summary>
    WorkflowsRead = 7,

    /// <summary>Workflow definition writes: saving, deleting. Running is NOT included in this scope — see <see cref="RunsWrite"/>.</summary>
    WorkflowsAdmin = 8,

    /// <summary>Eval suite/case/run reads: listing, single fetch, online evaluation summary.</summary>
    EvalsRead = 9,

    /// <summary>Eval suite/case writes: saving, deleting, promoting a case from a run. Triggering a run is NOT included in this scope — see <see cref="RunsWrite"/>.</summary>
    EvalsAdmin = 10,

    /// <summary>Experiment reads: listing, single fetch, results, canary status.</summary>
    ExperimentsRead = 11,

    /// <summary>Experiment writes: saving, deleting, starting/stopping, canary policy.</summary>
    ExperimentsAdmin = 12,

    /// <summary>
    /// Platform operations configuration and health reads: tenant
    /// registration, quotas, retention, scheduling, webhooks, diagnostics,
    /// provider health.
    /// </summary>
    PlatformRead = 13,

    /// <summary>Platform operations configuration writes and running retention cleanup.</summary>
    PlatformAdmin = 14,

    /// <summary>
    /// Surfaces that generate/extend authority: API keys, skill script
    /// grants, MCP OAuth start. The only self-elevating scope — should be
    /// granted rarely.
    /// </summary>
    SecurityAdmin = 15,

    /// <summary>Audit trail reads.</summary>
    AuditRead = 16,
}
