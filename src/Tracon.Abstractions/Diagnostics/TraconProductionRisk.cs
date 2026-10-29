namespace Tracon;

/// <summary>
/// One production decision the profile gate refuses to leave unanswered.
/// </summary>
/// <remarks>
/// <para>
/// Every value names the risk a PERMISSIVE default leaves behind, not the
/// switch that removes it. A deployment answers the decision by turning the
/// feature on, or accepts the risk by name through the profile options'
/// <c>Accept</c> method. There is deliberately
/// no way to accept all of them at once: an accepted risk is a decision, and
/// each one should be visible on its own line in a code review.
/// </para>
/// <para>
/// <strong>This set is a versioned contract.</strong> A later release that adds
/// a value stops a host that already calls <c>RequireProductionProfile()</c>
/// until the new decision is answered or accepted. That is the method's
/// purpose rather than a defect, so a release that adds one declares it as a
/// behavioural breaking change and says why the decision was added.
/// </para>
/// </remarks>
public enum TraconProductionRisk
{
    /// <summary>
    /// Tenants are not separated: every call resolves to the same default
    /// tenant, so one caller's agents, runs and sessions are another caller's.
    /// </summary>
    SingleTenant,

    /// <summary>
    /// Sessions are not stamped with the user they belong to, so no listing can
    /// be narrowed to its owner. Rows written while this is off keep no owner
    /// forever; turning it on later is not retroactive.
    /// </summary>
    UnownedSessions,

    /// <summary>
    /// Recorded content — prompts, responses, tool arguments — is stored as
    /// clear text, so anyone who can read the store can read the conversations.
    /// </summary>
    UnencryptedContentAtRest,

    /// <summary>
    /// No prompt or response is inspected on its way through the model
    /// pipeline, so nothing masks or refuses content the deployment would not
    /// want sent or stored.
    /// </summary>
    UninspectedContent,

    /// <summary>
    /// Request rate is not limited, so a single caller can exhaust the
    /// deployment's model budget and crowd every other caller out.
    /// </summary>
    UnlimitedRequestRate,

    /// <summary>
    /// No retention default deletes anything, so recorded data grows without a
    /// bound and old conversations stay readable indefinitely.
    /// </summary>
    UnboundedRetention,

    /// <summary>
    /// At least one of the <c>Tracon.Reader</c>, <c>Tracon.Operator</c> and
    /// <c>Tracon.Admin</c> authorization policies is not registered, so the
    /// endpoints it would guard get no role check and any caller that passes the
    /// outer layers reaches them.
    /// </summary>
    UnregisteredRolePolicies,

    /// <summary>
    /// The tool authorization handler is Tracon's built-in one, which allows every
    /// call, so any caller of a run can make the agent run any tool the agent has.
    /// </summary>
    UncheckedToolCalls,

    /// <summary>
    /// Tenants are separated, but a tenant with no egress policy may reach every
    /// provider, so a tenant nobody wrote a policy for sends its data wherever
    /// the deployment has a provider.
    /// </summary>
    UnrestrictedTenantEgress,

    /// <summary>
    /// The tenant is read from a request header and no claim takes precedence, so
    /// any client that can reach the endpoint can name the tenant it acts as.
    /// </summary>
    UnverifiedTenantHeader,

    /// <summary>
    /// Tenants are separated, but no limit caps the workflow runs one tenant has
    /// going at once, so one tenant can take every workflow slot and the others
    /// wait.
    /// </summary>
    UnlimitedTenantWorkflows,

    /// <summary>
    /// The audit trail's hash chain has no key, so anyone who can write to the audit table
    /// can change an entry and recompute every hash after it.
    /// </summary>
    UnkeyedAuditChain,

    /// <summary>
    /// More than one instance is declared (<c>Tracon:Deployment:Instances</c>), and the store keeps its
    /// state inside one process: in memory, or in a SQLite file. Each instance then sees its own runs,
    /// sessions and job queue, and a request that lands on the other instance finds none of them.
    /// </summary>
    InstanceLocalState,

    /// <summary>
    /// More than one instance is declared and run reconciliation is off. A cancel that reaches the
    /// instance that does not run the run answers <c>409</c>, and a run whose instance died stays
    /// <c>Running</c> until someone closes it by hand.
    /// </summary>
    UnreconciledRuns,

    /// <summary>
    /// More than one instance is declared and single-executor election is off, so every instance runs
    /// the periodic background work (approval expiry, canary rollback, workflow request expiry, the
    /// orphaned-run scan, the model health probe) at the same time.
    /// </summary>
    UnelectedBackgroundWork,

    /// <summary>
    /// More than one instance is declared, request rate is limited, and the limit is counted in the
    /// memory of each process, so the deployment admits the limit once per instance.
    /// </summary>
    InstanceLocalRateLimit,

    /// <summary>
    /// More than one instance is declared and the OAuth tokens of MCP servers live in the memory of one
    /// process, so an authorization the other instance started cannot finish, and a token the other
    /// instance obtained cannot be used. Accept it when no MCP server uses OAuth.
    /// </summary>
    InstanceLocalMcpTokens,
}
