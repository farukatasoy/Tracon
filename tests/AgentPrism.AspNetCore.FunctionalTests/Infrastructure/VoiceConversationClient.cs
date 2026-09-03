using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>A conversation frame received from the server.</summary>
/// <param name="Type">The event name; <see langword="null"/> in a binary frame.</param>
/// <param name="Json">The JSON body; <see langword="null"/> in a binary frame.</param>
/// <param name="Audio">The audio bytes; <see langword="null"/> in a text frame.</param>
internal readonly record struct VoiceEvent(string? Type, JsonElement? Json, byte[]? Audio)
{
    /// <summary>Whether the frame is binary.</summary>
    public bool IsAudio => Audio is not null;

    /// <summary>Reads a text field.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The value; <see langword="null"/> if the field is absent.</returns>
    public string? Text(string name)
        => Json?.TryGetProperty(name, out var value) == true ? value.GetString() : null;

    /// <summary>Reads a boolean field.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The value; <see langword="null"/> if the field is absent.</returns>
    public bool? Flag(string name)
        => Json?.TryGetProperty(name, out var value) == true ? value.GetBoolean() : null;
}

/// <summary>
/// A small test client that speaks the voice conversation protocol.
/// </summary>
/// <remarks>
/// Plays the server-facing half of what the browser does: sends control
/// messages as a JSON text frame, audio as a binary frame, and reads events
/// in order.
/// </remarks>
internal sealed class VoiceConversationClient(WebSocket socket) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sends a control message.</summary>
    /// <param name="payload">The object to serialize to JSON.</param>
    /// <returns>The completion task.</returns>
    public Task SendControlAsync(object payload)
        => socket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

    /// <summary>Sends an audio chunk.</summary>
    /// <param name="audio">The raw bytes.</param>
    /// <returns>The completion task.</returns>
    public Task SendAudioAsync(byte[] audio)
        => socket.SendAsync(
            audio,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

    /// <summary>Reads frames until the named event arrives.</summary>
    /// <param name="type">The expected event name.</param>
    /// <param name="collected">All frames received in the meantime.</param>
    /// <returns>The expected event.</returns>
    /// <exception cref="InvalidOperationException">The socket closed before the event arrived.</exception>
    public async Task<VoiceEvent> WaitForAsync(string type, List<VoiceEvent>? collected = null)
    {
        while (true)
        {
            var next = await ReceiveAsync();

            if (next is not { } received)
            {
                throw new InvalidOperationException(
                    $"The connection closed before the '{type}' event arrived. Received: " +
                    string.Join(", ", collected?.Select(static frame => frame.Type ?? "<audio>") ?? []));
            }

            collected?.Add(received);

            if (string.Equals(received.Type, type, StringComparison.Ordinal))
            {
                return received;
            }
        }
    }

    /// <summary>Reads a single frame.</summary>
    /// <returns>The frame; <see langword="null"/> if the socket closed.</returns>
    public async Task<VoiceEvent?> ReceiveAsync()
    {
        var buffer = new byte[16 * 1024];
        using var payload = new MemoryStream();
        WebSocketMessageType type;

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), TestContext.Current.CancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            type = result.MessageType;
            payload.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        var bytes = payload.ToArray();

        if (type == WebSocketMessageType.Binary)
        {
            return new VoiceEvent(null, null, bytes);
        }

        var element = JsonSerializer.Deserialize<JsonElement>(bytes, Json);

        return new VoiceEvent(element.GetProperty("type").GetString(), element, null);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (socket.State == WebSocketState.Open)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // The server may have already closed.
            }
            catch (ObjectDisposedException)
            {
                // 🚨 A test that gave up on a frame it expected leaves a receive in
                // flight, and TestWebSocket.CloseAsync reads before it closes - so
                // teardown threw and REPLACED the assertion that actually failed.
                // A failing test has to say what it was waiting for, not how its
                // socket ended.
            }
        }

        socket.Dispose();
    }
}
