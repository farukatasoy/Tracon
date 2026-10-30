using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// Streams an agent's answer to a caller that acts on it. A harness loop yields
/// its final iteration only.
/// </summary>
/// <remarks>
/// <para>
/// A non-streaming loop run returns its final iteration; a streaming one yields
/// every iteration, its drafts and its feedback included. A caller that reads
/// the answer - a workflow step, the next participant, an OpenAI-compatible or
/// A2A client, a voice reply - would act on a rejected draft: a text-to-speech
/// node speaks it, the next agent builds on it. A loop agent therefore runs through the non-streaming
/// path, which returns the final iteration only, and that answer is replayed as
/// updates. The caller's own limits bound it the same way they bound a stream.
/// </para>
/// <para>
/// The native agent stream (<c>POST /api/agents/{name}/run</c>) does not use
/// this: it is documented to stream every iteration as it happens. The run's
/// <c>loop.iteration-completed</c> events tell the iterations apart, and that
/// stream sends each of them as a frame of the same name between two iterations.
/// </para>
/// </remarks>
internal static class AgentAnswerStream
{
    /// <summary>Runs <paramref name="agent"/> and streams the answer its caller acts on.</summary>
    /// <param name="agent">The agent to run.</param>
    /// <param name="messages">The input messages.</param>
    /// <param name="session">The session, or <see langword="null"/>.</param>
    /// <param name="options">The run options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>Every update for an agent without a loop; the final iteration's updates for a loop agent.</returns>
    public static IAsyncEnumerable<AgentResponseUpdate> RunAsync(
        AIAgent agent,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        CancellationToken cancellationToken)
        => RunsALoop(agent)
            ? FinalIterationAsync(agent, messages, session, options, cancellationToken)
            : agent.RunStreamingAsync(messages, session, options, cancellationToken);

    /// <summary>Whether <paramref name="agent"/> runs a harness loop.</summary>
    /// <remarks>
    /// Read through <see cref="AIAgent.GetService{TService}(object?)"/>: every Tracon
    /// decorator and the harness itself delegate it to the agent they wrap. A
    /// consumer decorator that does not delegate hides the loop, and the agent
    /// then streams every iteration.
    /// </remarks>
#pragma warning disable MAAI001 // The loop API is marked "evaluation purposes only"
    internal static bool RunsALoop(AIAgent agent) => agent.GetService<LoopAgent>() is not null;
#pragma warning restore MAAI001

    private static async IAsyncEnumerable<AgentResponseUpdate> FinalIterationAsync(
        AIAgent agent,
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var response = await agent.RunAsync(messages, session, options, cancellationToken).ConfigureAwait(false);

        foreach (var update in response.ToAgentResponseUpdates())
        {
            yield return update;
        }
    }
}
