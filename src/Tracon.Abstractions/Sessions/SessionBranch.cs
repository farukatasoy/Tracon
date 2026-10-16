using System.ComponentModel;

namespace Tracon;

[Description("A request to branch a conversation from a specific point.")]
internal sealed record SessionBranchRequest
{
    /// <remarks>
    /// <c>0</c> is a valid value and opens a branch carrying only the first
    /// item. A negative value is rejected.
    /// </remarks>
    [Description(
        "The sequence number of the last item to include (inclusive). If not\n" +
        "given, the whole conversation is copied.")]
    public long? UpToSequence { get; init; }

    /// <remarks>
    /// If the identifier comes from the caller, an existing session with the
    /// same identifier is <strong>not overwritten</strong>; the request is rejected.
    /// </remarks>
    [Description("The identifier of the new session to open. Generated if not given.")]
    public string? NewSessionId { get; init; }
}

[Description("The result of branching.")]
internal sealed record SessionBranchResult
{
    [Description("The new session's identifier.")]
    public required string SessionId { get; init; }

    [Description("The new conversation's identifier.")]
    public required Guid ConversationId { get; init; }

    [Description("The source session's identifier.")]
    public required string ParentSessionId { get; init; }

    [Description("The source conversation's identifier.")]
    public required Guid ParentConversationId { get; init; }

    [Description(
        "The branch point: the sequence number of the last item copied to the\n" +
        "new conversation. `-1` if no item was copied.")]
    public required long BranchFromSequence { get; init; }

    [Description("The number of items copied.")]
    public required int CopiedItemCount { get; init; }
}

/// <summary>
/// The store that branches a conversation by copying it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Copying was chosen, not a pointer chain.</strong> If the new
/// conversation only kept <c>parent_conversation_id</c>, every history read
/// would be recursive; <c>SqlChatHistoryProvider</c> is the hottest read path,
/// running on every agent turn, and this would charge a cost even to a
/// consumer who never uses branching. With copying, the read path
/// <strong>never changes</strong>. The pointer is only lineage information.
/// </para>
/// <para>
/// This interface is registered only when a SQL provider is enabled
/// (<c>UsePostgreSql()</c>, <c>UseSqlServer()</c>, <c>UseSqlite()</c>). In an
/// in-memory setup, chat history lives inside Microsoft Agent Framework's
/// <c>InMemoryChatHistoryProvider</c> object, in the session state's
/// <strong>opaque</strong> block, and cannot be copied up to a specific
/// sequence number. The endpoint returns <c>501</c> in this case — it does
/// not silently copy the whole thing.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
/// </remarks>
public interface IConversationBranchStore
{
    /// <summary>Opens a new conversation by copying an existing one.</summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="parentConversationId">The source conversation.</param>
    /// <param name="upToSequence">
    /// The sequence number of the last item to include. If
    /// <see langword="null"/>, the whole conversation is copied.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The new conversation's identifier, the branch point, and the number of
    /// items copied; <see langword="null"/> if the source conversation does
    /// not exist or belongs to another tenant.
    /// </returns>
    ValueTask<ConversationBranch?> BranchAsync(
        string tenantId,
        Guid parentConversationId,
        long? upToSequence,
        CancellationToken cancellationToken = default);
}

/// <summary>A conversation branch's store-level result.</summary>
/// <param name="ConversationId">The new conversation's identifier.</param>
/// <param name="BranchFromSequence">
/// The sequence number of the last item copied; <c>-1</c> if no item was copied.
/// </param>
/// <param name="CopiedItemCount">The number of items copied.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct ConversationBranch(
    Guid ConversationId,
    long BranchFromSequence,
    int CopiedItemCount);
