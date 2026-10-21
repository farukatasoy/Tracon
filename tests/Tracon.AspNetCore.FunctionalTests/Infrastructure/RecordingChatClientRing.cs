using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Tracon.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>One call a <see cref="RecordingChatClientRing"/> saw.</summary>
/// <param name="Ring">The ring's label.</param>
/// <param name="LastRole">The role of the last request message.</param>
/// <param name="Texts">The text of every request message, in order.</param>
/// <param name="Failure">The type name of the exception that passed up through the ring, or <see langword="null"/>.</param>
internal sealed record RingCall(string Ring, ChatRole LastRole, IReadOnlyList<string> Texts, string? Failure);

/// <summary>The calls every ring of a test saw, in the order they entered.</summary>
internal sealed class RingLog
{
    private readonly List<RingCall> _calls = [];
    private readonly List<string> _entries = [];

    /// <summary>Gets the calls, in the order they ended.</summary>
    public IReadOnlyList<RingCall> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    /// <summary>Gets the ring labels in the order the rings were entered.</summary>
    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_calls)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Gets the calls one ring saw.</summary>
    /// <param name="ring">The ring's label.</param>
    /// <returns>The calls.</returns>
    public IReadOnlyList<RingCall> Of(string ring) => [.. Calls.Where(call => string.Equals(call.Ring, ring, StringComparison.Ordinal))];

    public void Enter(string ring)
    {
        lock (_calls)
        {
            _entries.Add(ring);
        }
    }

    public void Add(RingCall call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }
    }
}

/// <summary>
/// A chat client ring that records each call it sees and can add a message to the request
/// before it passes the call on.
/// </summary>
/// <param name="inner">The client below the ring.</param>
/// <param name="label">The label the log records.</param>
/// <param name="log">The log.</param>
/// <param name="addedMessage">A message the ring appends to every request, or <see langword="null"/>.</param>
/// <param name="addedRole">The role of the added message; <see cref="ChatRole.User"/> when <see langword="null"/>.</param>
internal sealed class RecordingChatClientRing(
    IChatClient inner,
    string label,
    RingLog log,
    string? addedMessage = null,
    ChatRole? addedRole = null)
    : DelegatingChatClient(inner)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = Prepare(messages);

        try
        {
            var response = await base.GetResponseAsync(request, options, cancellationToken).ConfigureAwait(false);
            Record(request, failure: null);
            return response;
        }
        catch (Exception exception)
        {
            Record(request, exception.GetType().Name);
            throw;
        }
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var request = Prepare(messages);
        var enumerator = base.GetStreamingResponseAsync(request, options, cancellationToken).GetAsyncEnumerator(cancellationToken);

        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    Record(request, exception.GetType().Name);
                    throw;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return enumerator.Current;
            }
        }

        Record(request, failure: null);
    }

    private List<ChatMessage> Prepare(IEnumerable<ChatMessage> messages)
    {
        log.Enter(label);

        var request = messages.ToList();

        if (addedMessage is not null)
        {
            request.Add(new ChatMessage(addedRole ?? ChatRole.User, addedMessage));
        }

        return request;
    }

    private void Record(List<ChatMessage> request, string? failure)
        => log.Add(new RingCall(label, request[^1].Role, [.. request.Select(static message => message.Text)], failure));
}
