using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Collects the calls a run produced that never got a result, so the next run can close them.
/// </summary>
/// <remarks>
/// A tool that suspends for user input stops the function-invocation loop (<c>Terminate</c>). The loop runs
/// the calls of a turn one after the other, so a call queued behind the suspended one never runs and has no
/// result; a provider refuses a history with such a call. The run that suspended knows which calls those are;
/// <see cref="ToolInputSessionBridge"/> hands them to the next run of the session.
/// </remarks>
internal sealed class SkippedCallTracker
{
    private readonly HashSet<string> _called = new(StringComparer.Ordinal);
    private readonly HashSet<string> _settled = new(StringComparer.Ordinal);

    /// <summary>Notes one content the run produced.</summary>
    /// <param name="content">The content.</param>
    public void Observe(AIContent content)
    {
        switch (content)
        {
            case FunctionCallContent call:
                _called.Add(call.CallId);
                break;

            case FunctionResultContent result:
                _settled.Add(result.CallId);
                break;

            // A call that waits for an approval is answered by the approval, not skipped.
            case ToolApprovalRequestContent request:
                _settled.Add(request.ToolCall.CallId);
                break;

            default:
                break;
        }
    }

    /// <summary>Notes every content of the messages a run produced.</summary>
    /// <param name="messages">The messages.</param>
    public void Observe(IEnumerable<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            foreach (var content in message.Contents)
            {
                Observe(content);
            }
        }
    }

    /// <summary>Gets the calls that were made and never answered.</summary>
    /// <returns>The call identities, in no particular order.</returns>
    public IReadOnlyList<string> Skipped() => [.. _called.Where(id => !_settled.Contains(id))];
}
