using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>Turns one delegation from a live voice session into a Tracon run.</summary>
/// <remarks>
/// <para>
/// This is where the phase earns its keep: a delegated task becomes an
/// <strong>ordinary</strong> run. The tool registry, the content guard, the quota,
/// the cost accounting and the audit trail are all on, exactly as they are for
/// <c>POST /api/agents/{name}/run</c>, because it is the same path. Nothing in the
/// recording chain is special-cased for voice.
/// </para>
/// <para>
/// The delegation event <strong>cannot choose the agent</strong>. It arrives
/// from the provider, across the tenant boundary, and is untrusted input; letting it
/// name an agent would let a prompt injection pick a privileged one. The agent is
/// resolved once, when the session is created, from what the caller asked for.
/// </para>
/// </remarks>
internal sealed class LiveVoiceDelegationRunner
{
    private readonly AIAgent _agent;
    private readonly AgentSession _session;
    private readonly ILiveVoiceSideband _sideband;
    private readonly LiveTranscriptLedger _ledger;
    private readonly VoiceLiveOptions _options;
    private readonly ContentGuardPipeline? _guard;
    private readonly ILogger _logger;
    private readonly string _sessionId;
    private readonly int _maxAppendCharacters;

    /// <summary>Creates a runner.</summary>
    /// <param name="agent">The agent resolved when the session was created.</param>
    /// <param name="session">The agent session the conversation runs in.</param>
    /// <param name="sideband">The provider's control connection.</param>
    /// <param name="ledger">The conversation transcript.</param>
    /// <param name="options">The live layer's options.</param>
    /// <param name="maxAppendCharacters">The provider's per-append character ceiling.</param>
    /// <param name="guard">The content guard, when one is registered.</param>
    /// <param name="sessionId">The agent session identifier.</param>
    /// <param name="logger">The logger.</param>
    public LiveVoiceDelegationRunner(
        AIAgent agent,
        AgentSession session,
        ILiveVoiceSideband sideband,
        LiveTranscriptLedger ledger,
        VoiceLiveOptions options,
        int maxAppendCharacters,
        ContentGuardPipeline? guard,
        string sessionId,
        ILogger logger)
    {
        _agent = agent;
        _session = session;
        _sideband = sideband;
        _ledger = ledger;
        _options = options;
        _maxAppendCharacters = maxAppendCharacters;
        _guard = guard;
        _sessionId = sessionId;
        _logger = logger;
    }

    /// <summary>Runs one delegation and speaks its result back into the conversation.</summary>
    /// <param name="delegationId">The provider's delegation identifier.</param>
    /// <param name="offsetMilliseconds">Where in the conversation the delegation was cut.</param>
    /// <param name="cancellationToken">Cancelled when the delegation or the session ends.</param>
    /// <returns>The run identifier, or <see langword="null"/> when no run was opened.</returns>
    public async Task<Guid?> RunAsync(
        string delegationId,
        int offsetMilliseconds,
        CancellationToken cancellationToken)
    {
        var cut = _ledger.Cut(offsetMilliseconds, _options.MaxLedgerEntriesPerDelegation);

        if (cut.Count == 0)
        {
            // 🚨 No run is opened for an empty cut. A run with no input costs the
            // consumer money and produces an answer to nothing.
            await AppendAsync(
                delegationId,
                LiveVoiceAppendChannel.Thinking,
                "There is no conversation context for this request yet.",
                new LiveVoiceAppendBudget(_maxAppendCharacters, 1),
                cancellationToken).ConfigureAwait(false);

            return null;
        }

        var runId = TraconId.NewId();
        var budget = new LiveVoiceAppendBudget(_maxAppendCharacters, _options.MaxAppendsPerDelegation);

        if (_options.Instructions is { Length: > 0 } instructions)
        {
            // 🚨 Instructions come from configuration only, never from a run's
            // output: text one model produced must not become the instruction
            // another model obeys.
            await AppendAsync(
                delegationId,
                LiveVoiceAppendChannel.Instructions,
                instructions,
                budget,
                cancellationToken).ConfigureAwait(false);
        }

        var spoken = new StringBuilder();

        try
        {
            // 🚨 The stream is consumed in THIS method's own body. Moving the
            // `await foreach` into an `async IAsyncEnumerable` helper that also
            // writes ambient state would lose that state: an ExecutionContext write
            // does not cross a `yield return` boundary. This class of defect has
            // cost this repository four times.
            var updates = _agent.RunStreamingAsync(
                [new ChatMessage(ChatRole.User, LiveTranscriptLedger.RenderPrompt(cut))],
                _session,
                new TraconRunOptions { RunId = runId, SessionId = _sessionId },
                cancellationToken);

            var segmenter = new VoiceSpeechSegmenter();

            await foreach (var update in updates.ConfigureAwait(false))
            {
                if (HasToolActivity(update))
                {
                    await AppendAsync(
                        delegationId,
                        LiveVoiceAppendChannel.Thinking,
                        "Still working on it.",
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }

                if (update.Text is not { Length: > 0 } delta)
                {
                    continue;
                }

                spoken.Append(delta);

                foreach (var segment in segmenter.Append(delta))
                {
                    await AppendAsync(
                        delegationId,
                        LiveVoiceAppendChannel.Commentary,
                        segment,
                        budget,
                        cancellationToken).ConfigureAwait(false);
                }
            }

            if (segmenter.Flush() is { Length: > 0 } tail)
            {
                await AppendAsync(
                    delegationId,
                    LiveVoiceAppendChannel.Commentary,
                    tail,
                    budget,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The delegation was cancelled, usually because the session closed. What
            // was said already stays on record; the caller writes it to the history.
            //
            // 🚨 The assignment below is in a `finally`, not after the try/catch:
            // this `throw;` would otherwise skip it and the caller's "write the
            // partial answer" branch would be dead code that always saw an empty
            // string.
            throw;
        }
        catch (Exception exception) when (exception is TraconException or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            // 🚨 The raw exception is NEVER spoken: it can carry a host:port, a
            // provider URL or a prompt fragment. The caller gets a correlation
            // reference and the detail stays in the log.
            _logger.LogWarning(
                exception,
                "The delegated run {RunId} of live voice session {SessionId} failed.",
                runId,
                _sessionId);

            await AppendAsync(
                delegationId,
                LiveVoiceAppendChannel.Thinking,
                $"That request could not be completed (reference {runId:D}).",
                budget,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            SpokenText = spoken.ToString();
        }

        return runId;
    }

    /// <summary>Gets what the delegated run produced, for the session history.</summary>
    public string SpokenText { get; private set; } = string.Empty;

    private static bool HasToolActivity(AgentResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            if (content is FunctionResultContent)
            {
                return true;
            }
        }

        return false;
    }

    private async Task AppendAsync(
        string delegationId,
        LiveVoiceAppendChannel channel,
        string text,
        LiveVoiceAppendBudget budget,
        CancellationToken cancellationToken)
    {
        if (budget.IsExhausted)
        {
            return;
        }

        // 🚨 The append leaves this process for a third party. The chat-client
        // decorator that guards model-bound text never sees this path — measured:
        // nothing here traverses an IChatClient — so the guard is called here,
        // explicitly, before the text goes out.
        var inspected = text;

        if (_guard is { HasGuards: true })
        {
            try
            {
                inspected = await _guard
                    .InspectAsync(
                        ContentGuardDirection.Output,
                        text,
                        ContentGuardSource.ModelOutput,
                        toolName: null,
                        modelId: null,
                        cancellationToken)
                    .ConfigureAwait(false) ?? text;
            }
            catch (TraconContentBlockedException)
            {
                _logger.LogWarning(
                    "A content guard blocked an append on live voice session {SessionId}.",
                    _sessionId);

                return;
            }
        }

        foreach (var piece in budget.Take(inspected))
        {
            try
            {
                await _sideband
                    .AppendAsync(
                        new LiveVoiceAppend
                        {
                            Channel = channel,
                            DelegationId = delegationId,
                            Text = piece,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TraconException or InvalidOperationException or HttpRequestException)
            {
                // An append that the provider refuses is LOGGED, never swallowed in
                // silence: a caller who hears half an answer must be able to find out
                // why from the logs.
                _logger.LogWarning(
                    exception,
                    "An append was refused on live voice session {SessionId}.",
                    _sessionId);

                return;
            }
        }
    }
}
