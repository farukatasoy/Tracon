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
}
