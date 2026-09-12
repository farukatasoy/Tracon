namespace Tracon;

/// <summary>
/// Resolves who a run belongs to and what work it was made for.
/// </summary>
/// <remarks>
/// <para>
/// This is the sibling of <see cref="ITenantContext"/>. The tenant answers
/// "whose data is this"; this interface answers "which <em>user</em> spent
/// this" and "which <em>job</em> was it spent on". Both questions are needed
/// to break a cost report down further than the tenant.
/// </para>
/// <para>
/// <strong>The user identity is never read from the request body.</strong>
/// A <c>userId</c> field on <c>POST /api/agents/{name}/run</c> would let any
/// client write spend against another user's name, which forges the cost
/// record outright. The consumer binds this interface to its OWN identity
/// pipeline — a claim, an API key, a resolved principal.
/// </para>
/// <para>
/// The interface is registered with <c>TryAdd</c> and the built-in
/// implementation returns <see langword="null"/> for both members unless
/// <see cref="AmbientRunAttributionScope"/> is active, so an application that
/// registers nothing keeps its current behaviour exactly: the columns stay
/// NULL and nothing else changes.
/// </para>
/// <para>
/// The implementation must be a <strong>singleton</strong>, for the same
/// reason as <see cref="ITenantContext"/>: singleton services take a
/// dependency on it, and a scoped registration would be a captive dependency.
/// Resolve per-request state through <c>IHttpContextAccessor</c>.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> This interface
/// carries no tenant parameter and applies no tenant filtering of its own:
/// attribution (who spent this, what job was it for) is a per-request
/// identity concern, orthogonal to <see cref="ITenantContext"/>'s
/// data-tenancy concern. It answers "which user", never "which tenant".
/// </para>
/// </remarks>
public interface IRunAttributionContext
{
    /// <summary>
    /// Gets the user the current run belongs to, or <see langword="null"/>
    /// when it is unknown.
    /// </summary>
    /// <remarks>
    /// The value is an <strong>opaque string</strong>. Tracon neither
    /// resolves nor validates its meaning, and stores no personal detail of
    /// its own; the consumer decides what the identifier means. This is the
    /// same stance <c>IDataSubjectResolver</c> takes, and the data
    /// subject erasure flow covers this column too.
    /// </remarks>
    string? UserId { get; }

    /// <summary>
    /// Gets the labels of the current run, or <see langword="null"/> when
    /// there are none.
    /// </summary>
    /// <remarks>
    /// Labels are a <strong>query dimension, not a metric dimension</strong>.
    /// They live in the <c>runs</c> table and are never added to
    /// <c>tracon.tokens</c> or <c>tracon.run.cost</c>: turning a free
    /// label set into a metric dimension blows up time-series cardinality. The
    /// same rule applies to <see cref="UserId"/>.
    /// The limits are on <see cref="RunLabels"/>.
    /// </remarks>
    IReadOnlyDictionary<string, string>? Labels { get; }
}

/// <summary>
/// Reads an <see cref="IRunAttributionContext"/> safely on a recording path.
/// </summary>
/// <remarks>
/// Every deep call site must go through this rather than touching the
/// interface directly. Two guarantees are easy to lose and expensive to lose:
/// a consumer implementation reaches into its OWN identity pipeline and can
/// throw there, and it can return a value it never validated. Neither may take
/// down a run that is already under way — observability does not break
/// functionality. The loud rejection belongs at the boundaries where the value
/// ENTERS (the HTTP endpoint answers 400,
/// <see cref="AmbientRunAttributionScope.Begin"/> throws).
/// </remarks>
public static class RunAttributionReader
{
    /// <summary>Reads the current attribution, surviving a faulty implementation.</summary>
    /// <param name="context">The attribution context, or <see langword="null"/> when none is registered.</param>
    /// <param name="onFault">
    /// Called with a description when the value is unusable, so the caller can log
    /// it with its own run identity. Never called on the happy path.
    /// </param>
    /// <returns>
    /// The user and a FROZEN copy of the labels, or <c>(null, null)</c> when
    /// there is nothing usable. The copy matters: the caller's dictionary may be
    /// a live request object, and a run record must not change under whoever
    /// reads it later.
    /// </returns>
    public static (string? UserId, IReadOnlyDictionary<string, string>? Labels) Read(
        IRunAttributionContext? context,
        Action<string, Exception?>? onFault = null)
    {
        if (context is null)
        {
            return (null, null);
        }

        string? userId;
        IReadOnlyDictionary<string, string>? labels;

        try
        {
            userId = context.UserId;
            labels = context.Labels;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            onFault?.Invoke("IRunAttributionContext threw while resolving the attribution.", ex);

            return (null, null);
        }

        userId = string.IsNullOrWhiteSpace(userId) ? null : userId;

        // Dropped WHOLE, never trimmed: a trimmed label set reads as a complete
        // measurement to whoever queries the report later.
        if (RunLabels.Validate(userId, labels) is { } error)
        {
            onFault?.Invoke($"IRunAttributionContext returned an attribution that breaks a limit: {error}", null);

            return (null, null);
        }

        return (userId, labels is { Count: > 0 } ? RunLabels.Freeze(labels) : null);
    }
}
