using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>The server-side control connection of an OpenAI live voice session.</summary>
/// <remarks>
/// <para>
/// The outbound address policy is applied before this socket is opened.
/// <see cref="ClientWebSocket"/> offers no connection callback, so the policy cannot
/// be enforced by the transport the way it is for HTTP; the provider resolves and
/// judges the target explicitly instead.
/// </para>
/// <para>
/// The socket carries no primary audio in either direction: WebRTC carries that.
/// The provider does mirror the session's audio onto this socket, and those frames
/// are deliberately ignored — consuming them would make Tracon a party to media
/// it has no way to store lawfully or usefully here.
/// </para>
/// </remarks>
internal sealed class OpenAILiveSideband : ILiveVoiceSideband
{
    private const int ReceiveBufferSize = 64 * 1024;

    private readonly ClientWebSocket _socket;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    /// <summary>Creates a sideband over an already-connected socket.</summary>
    /// <param name="socket">The connected socket.</param>
    /// <param name="logger">The logger.</param>
    public OpenAILiveSideband(ClientWebSocket socket, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(logger);

        _socket = socket;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask AppendAsync(LiveVoiceAppend append, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(append);

        var envelope = new OpenAILiveEventEnvelope
        {
            Type = append.Channel switch
            {
                LiveVoiceAppendChannel.Thinking => "session.thinking.append",
                LiveVoiceAppendChannel.Commentary => "session.commentary.append",
                LiveVoiceAppendChannel.Instructions => "session.instructions.append",
                _ => throw new NotSupportedException($"The append channel '{append.Channel}' is not supported."),
            },
            DelegationId = append.DelegationId,
            Content = append.Text,
        };

        var payload = JsonSerializer.SerializeToUtf8Bytes(envelope, OpenAILiveJsonContext.Default.OpenAILiveEventEnvelope);

        // A WebSocket permits only one send at a time; delegations run concurrently.
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await _socket
                .SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LiveVoiceEvent> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[ReceiveBufferSize];
        var builder = new StringBuilder();

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult? result;

            try
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException exception)
            {
                // The provider drops the socket without a close handshake when it
                // fails. C# forbids `yield return` inside a catch, so the event is
                // emitted just below.
                _logger.LogDebug(exception, "The live voice sideband ended without a close handshake.");

                result = null;
            }

            if (result is null)
            {
                // 🚨 An Error event, not a silent `yield break`. A stream that simply
                // ENDS is indistinguishable from a conversation the caller finished —
                // the session record would say "the client hung up" about a provider
                // outage, and the operator investigating it would never find it.
                yield return new LiveVoiceEvent
                {
                    Kind = LiveVoiceEventKind.Error,
                    Text = "The sideband connection was lost.",
                };

                yield break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                yield return new LiveVoiceEvent
                {
                    Kind = LiveVoiceEventKind.Closed,
                    Reason = result.CloseStatusDescription,
                };

                yield break;
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (!result.EndOfMessage)
            {
                continue;
            }

            var message = builder.ToString();
            builder.Clear();

            if (Translate(message) is { } translated)
            {
                yield return translated;
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket
                    .CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            _logger.LogDebug(exception, "The live voice sideband could not be closed cleanly.");
        }

        _socket.Dispose();
        _sendGate.Dispose();
    }

    /// <summary>Translates one provider frame into a Tracon event.</summary>
    /// <param name="message">The raw frame.</param>
    /// <returns>The event, or <see langword="null"/> when the frame carries nothing Tracon models.</returns>
    /// <remarks>
    /// The type names come from a raw dump of a real session (2026-09-11). Frames
    /// that are not modelled — the mirrored input and output audio above all — return
    /// <see langword="null"/> rather than an <c>Unknown</c> event: a stream of events
    /// meaning "something happened" is noise a consumer cannot act on.
    /// </remarks>
    internal static LiveVoiceEvent? Translate(string message)
    {
        OpenAILiveEventEnvelope? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize(message, OpenAILiveJsonContext.Default.OpenAILiveEventEnvelope);
        }
        catch (JsonException)
        {
            return new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.Error,
                Text = "The provider sent a frame that could not be parsed.",
            };
        }

        if (envelope?.Type is not { Length: > 0 } type)
        {
            return null;
        }

        return type switch
        {
            "session.started" => new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.SessionStarted,
                SessionId = envelope.Session?.Id,
            },

            "session.input_transcript.delta" => new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.InputTranscript,
                Text = envelope.Delta,
                StartMilliseconds = envelope.StartMs,
                EndMilliseconds = envelope.EndMs,
            },

            "session.output_transcript.delta" => new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.OutputTranscript,
                Text = envelope.Delta,
                StartMilliseconds = envelope.StartMs,
                EndMilliseconds = envelope.EndMs,
            },

            "session.delegation.created" => new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.DelegationCreated,
                DelegationId = envelope.Delegation?.Id,
                ItemId = envelope.Delegation?.Id,
                OffsetMilliseconds = envelope.OffsetMs,
            },

            "session.thinking.appended" or "session.commentary.appended" or "session.instructions.appended"
                => new LiveVoiceEvent
                {
                    Kind = LiveVoiceEventKind.AppendAccepted,
                    StartMilliseconds = envelope.StartMs,
                    EndMilliseconds = envelope.EndMs,
                },

            "session.usage.updated" => new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.UsageUpdated,
                Seconds = envelope.Usage?.Seconds,
            },

            "session.closed" => new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.Closed,
                SessionId = envelope.Session?.Id,
                Reason = envelope.Reason,
                Seconds = envelope.Usage?.Seconds,
            },

            "error" => new LiveVoiceEvent
            {
                Kind = LiveVoiceEventKind.Error,
                Text = envelope.Error?.Message,
            },

            // 🚨 session.input_audio.append and session.output_audio.delta land here.
            // The provider does mirror the media onto this socket; this phase does
            // not consume it.
            _ => null,
        };
    }
}
