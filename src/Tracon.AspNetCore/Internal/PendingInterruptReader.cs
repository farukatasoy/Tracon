using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>The requests a session waits on, by kind.</summary>
/// <param name="ApprovalRequestIds">The identities of the tool approval requests nobody has answered yet.</param>
/// <param name="InputRequestIds">The identities of the tool input requests nobody has answered yet.</param>
internal sealed record PendingInterrupts(
    IReadOnlySet<string> ApprovalRequestIds,
    IReadOnlySet<string> InputRequestIds)
{
    /// <summary>Gets the state of a session that waits on nothing.</summary>
    public static PendingInterrupts None { get; } = new(
        new HashSet<string>(StringComparer.Ordinal),
        new HashSet<string>(StringComparer.Ordinal));
}

/// <summary>
/// Reads which requests a stored session waits on: the tool approvals and the tool inputs its history holds without an answer.
/// </summary>
/// <remarks>
/// <para>
/// A caller that answers a request by its identity alone, as an AG-UI front end does, does not say which kind
/// the request was. The history is the one place that knows; this reads it with the same collectors the resolvers match
/// with, so the answer to "what is this identity" and the match that follows cannot disagree.
/// </para>
/// <para>
/// A session that does not exist for the calling tenant, and one whose history cannot be read, wait on nothing: the answers
/// are then refused as unknown, which is what they are. The read is the caller's own session by the time it happens, because
/// the ownership gate ran first.
/// </para>
/// </remarks>
internal static class PendingInterruptReader
{
    /// <summary>Reads the requests the session waits on.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pending requests; <see cref="PendingInterrupts.None"/> when there is no readable session.</returns>
    public static async ValueTask<PendingInterrupts> ReadAsync(
        HttpContext httpContext,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var services = httpContext.RequestServices;

        var record = await services.GetRequiredService<ISessionStore>()
            .GetAsync(sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (record is null)
        {
            return PendingInterrupts.None;
        }

        var history = await ChatHistoryReader
            .ReadAsync(
                record,
                services.GetRequiredService<IAgentCatalog>(),
                services.GetRequiredService<ChatHistoryProvider>(),
                services.GetRequiredService<ILoggerFactory>(),
                cancellationToken)
            .ConfigureAwait(false);

        if (history is null)
        {
            return PendingInterrupts.None;
        }

        return new PendingInterrupts(
            ToolApprovalResolver.CollectPendingRequests(history).Keys.ToHashSet(StringComparer.Ordinal),
            ToolInputResolver.Collect(history).Pending.Keys.ToHashSet(StringComparer.Ordinal));
    }
}
