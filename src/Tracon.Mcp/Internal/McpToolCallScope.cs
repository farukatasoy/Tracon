using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// What the MCP client's elicitation handler needs to know about the tool call it was reached from.
/// </summary>
/// <remarks>
/// <para>
/// Held in an <see cref="AsyncLocal{T}"/> that <see cref="McpElicitingTool"/> opens in the BODY of its own
/// <c>InvokeCoreAsync</c> (an assignment made in a helper async method does not flow back to its caller).
/// The write flows downward: the SDK runs the handler inside the same asynchronous flow as the
/// <c>tools/call</c> request, so the handler sees the scope of exactly the call that is waiting for it. A
/// request the server sends outside any tool call (the older protocol's direct request, delivered from the
/// connection's receive loop) sees no scope and is declined.
/// </para>
/// <para>
/// The scope is closed (<see cref="IsActive"/> turns off) when the call ends, so a handler that is reached
/// later from a task the SDK left running cannot answer on behalf of a call that is over.
/// </para>
/// </remarks>
internal sealed class McpToolCallScope
{
    private static readonly AsyncLocal<McpToolCallScope?> Holder = new();

    // Locks on the list itself: net8.0 has no System.Threading.Lock, and a separate
    // object field would trigger MA0158 on net9.0+ (the InMemoryJobStore pattern).
    private readonly List<ToolInputAnswer> _unconsumed;
    private PendingToolInput? _pending;
    private volatile bool _active = true;

    /// <summary>Creates the scope of one tool call.</summary>
    /// <param name="serverName">The MCP server the call goes to.</param>
    /// <param name="toolName">The qualified tool name.</param>
    /// <param name="run">The run the call belongs to; <see langword="null"/> outside a run.</param>
    /// <param name="call">The call the function-invocation loop is running; <see langword="null"/> when the loop did not call the tool.</param>
    /// <param name="canTerminate">Whether the loop can be told to stop after this call.</param>
    /// <param name="answers">The user's answers for this call, if it replays a suspended one.</param>
    public McpToolCallScope(
        string serverName,
        string toolName,
        AgentRunScope? run,
        FunctionCallContent? call,
        bool canTerminate,
        IReadOnlyList<ToolInputAnswer>? answers)
    {
        ServerName = serverName;
        ToolName = toolName;
        Run = run;
        Call = call;
        CanTerminate = canTerminate;
        HasAnswers = answers is { Count: > 0 };
        _unconsumed = answers is null ? [] : [.. answers];
    }

    /// <summary>Gets the scope of the tool call the current asynchronous flow belongs to, if any.</summary>
    public static McpToolCallScope? Current => Holder.Value;

    /// <summary>Gets the MCP server the call goes to.</summary>
    public string ServerName { get; }

    /// <summary>Gets the qualified tool name.</summary>
    public string ToolName { get; }

    /// <summary>Gets the run the call belongs to; <see langword="null"/> outside a run.</summary>
    public AgentRunScope? Run { get; }

    /// <summary>Gets the call the loop is running, or <see langword="null"/> when the loop did not call the tool.</summary>
    public FunctionCallContent? Call { get; }

    /// <summary>Gets the call identity, or <see langword="null"/> when the loop did not call the tool.</summary>
    public string? CallId => Call?.CallId;

    /// <summary>Gets whether the function-invocation loop can be told to stop after this call.</summary>
    public bool CanTerminate { get; }

    /// <summary>Gets whether this call replays a suspended one and so came with answers.</summary>
    public bool HasAnswers { get; }

    /// <summary>Gets whether the call is still running.</summary>
    public bool IsActive => _active;

    /// <summary>Gets the request that suspended the call, or <see langword="null"/> when none did.</summary>
    public PendingToolInput? Pending => Volatile.Read(ref _pending);

    /// <summary>
    /// Opens a scope. Call it from the body of the method that runs the tool, never from a helper: the
    /// assignment must stay in the flow the SDK's handler runs in.
    /// </summary>
    /// <param name="scope">The scope to make current.</param>
    public static void Open(McpToolCallScope scope) => Holder.Value = scope;

    /// <summary>Closes the scope when the call ends.</summary>
    public void Close() => _active = false;

    /// <summary>Takes the answer given to the request with this fingerprint, if there is one.</summary>
    /// <param name="fingerprint">The digest of the request the server just sent.</param>
    /// <param name="answer">The answer.</param>
    /// <returns><see langword="true"/> when an unconsumed answer was given to this request.</returns>
    public bool TryTakeAnswer(string fingerprint, out ToolInputAnswer answer)
    {
        lock (_unconsumed)
        {
            var index = _unconsumed.FindIndex(candidate => string.Equals(candidate.Fingerprint, fingerprint, StringComparison.Ordinal));

            if (index < 0)
            {
                answer = default!;
                return false;
            }

            answer = _unconsumed[index];
            _unconsumed.RemoveAt(index);

            return true;
        }
    }

    /// <summary>Records the request that suspends the call; only the first one is kept.</summary>
    /// <param name="input">The request.</param>
    /// <returns>The request the call is suspended on (the first one recorded).</returns>
    public PendingToolInput Suspend(PendingToolInput input)
        => Interlocked.CompareExchange(ref _pending, input, comparand: null) ?? input;
}
