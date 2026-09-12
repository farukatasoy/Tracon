using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// A run error's class.
/// </summary>
/// <remarks>
/// <para>
/// The taxonomy is kept small and stable. <see cref="Unknown"/> is not a
/// failure but a measurement tool: a classifier that finds no matching rule
/// does NOT GUESS an error's class, it writes into this bucket. A high
/// proportion here shows the taxonomy is incomplete.
/// </para>
/// <para>
/// Written <strong>as a name</strong> in JSON; stored as <c>smallint</c> in
/// the database (the same pattern as every other <c>runs</c> enum, for
/// example <see cref="RunKind"/>). Once assigned, numeric values are
/// <strong>never renumbered</strong>: they are stored permanently in past
/// rows. A new class is added to the end of the list, not inserted in order.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<RunErrorClass>))]
public enum RunErrorClass
{
    /// <summary>Matched no rule. Not a failure, a measurement tool.</summary>
    Unknown = 0,

    /// <summary>The model provider returned an error (5xx, connection, non-timeout).</summary>
    ProviderError = 1,

    /// <summary>The circuit breaker is open (<see cref="TraconProviderUnavailableException"/>).</summary>
    ProviderUnavailable = 2,

    /// <summary>The provider returned 429.</summary>
    RateLimited = 3,

    /// <summary>
    /// A Tracon quota was exhausted, or a run tree's token, cost, or time
    /// budget ran out mid-run (<see cref="TraconRunBudgetExceededException"/>).
    /// </summary>
    QuotaExceeded = 4,

    /// <summary>
    /// The model response was cut off by the <strong>provider's</strong>
    /// safety/content filter.
    /// </summary>
    /// <remarks>
    /// This is NOT the class for a decision made by Tracon's own guard;
    /// see <see cref="ContentBlocked"/>. Merging the two would make the
    /// operator lose the distinction between "the model refused" and "our
    /// policy refused."
    /// </remarks>
    ContentFiltered = 5,

    /// <summary>A tool threw an exception.</summary>
    ToolError = 6,

    /// <summary>The run exceeded its time limit.</summary>
    Timeout = 7,

    /// <summary>The agent definition failed to compile.</summary>
    CompilationFailed = 8,

    // 🚨 9 is a RETIRED GAP, never a free slot. It held BudgetExceeded ("the
    // tree or context budget was exceeded"), which no code path could produce:
    // when the tree budget runs out ChildAgentInvoker returns text to the model
    // instead of throwing, so the run ends successfully. The member still
    // reached consumers through the OpenAPI document, the TypeScript schema and
    // both UI dictionaries, advertising a state that never occurred. It was
    // removed while PublicAPI.Shipped.txt was still empty (K-603). Reusing 9
    // would give stored records and older clients a second, conflicting
    // meaning. RunErrorClassContractTests pins this.

    /// <summary>Cancelled.</summary>
    Canceled = 10,

    /// <summary>
    /// The content was blocked by Tracon's own <see cref="IContentGuard"/>
    /// policy (<see cref="TraconContentBlockedException"/>).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ContentFiltered"/>: the cause is not the
    /// provider but the installation's own rule set. The corresponding action
    /// is also different — one calls for loosening provider settings, the
    /// other for reviewing the policy.
    /// </remarks>
    ContentBlocked = 11,

    /// <summary>
    /// The process running the run disappeared without ever talking to the
    /// provider (example: an <c>agent.RunAsync</c> crash with no retry).
    /// </summary>
    /// <remarks>
    /// Only orphaned-run reconciliation falls into this class.
    /// Every other class relies on a response from the provider/tool/quota;
    /// this one requires a separate class because NO response was ever received.
    /// </remarks>
    Infrastructure = 12,

    /// <summary>
    /// A tool call did not settle within its configured timeout
    /// (<see cref="TraconToolTimeoutException"/>).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Canceled"/>: the wrapper's own linked
    /// cancellation looks identical to a user cancellation at the exception
    /// type level (<see cref="OperationCanceledException"/>), and merging the
    /// two would hide the reason for every tool timeout behind "the user
    /// canceled". This class does not fail a run — the run continues with a
    /// tool error — but the taxonomy applies wherever this exception's stable
    /// identity is classified.
    /// </remarks>
    ToolTimeout = 13,

    /// <summary>
    /// The response failed structured output validation
    /// (<see cref="TraconStructuredResponseException"/>).
    /// </summary>
    StructuredResponseInvalid = 14,
}
