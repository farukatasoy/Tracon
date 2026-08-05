using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AgentPrism.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>Sunucudan alinmis bir konusma cercevesi.</summary>
/// <param name="Type">Olay adi; ikili cercevede <see langword="null"/>.</param>
/// <param name="Json">JSON govdesi; ikili cercevede <see langword="null"/>.</param>
/// <param name="Audio">Ses baytlari; metin cercevesinde <see langword="null"/>.</param>
internal readonly record struct VoiceEvent(string? Type, JsonElement? Json, byte[]? Audio)
{
    /// <summary>Cerceve ikili mi.</summary>
    public bool IsAudio => Audio is not null;

    /// <summary>Bir metin alanini okur.</summary>
    /// <param name="name">Alan adi.</param>
    /// <returns>Deger; alan yoksa <see langword="null"/>.</returns>
    public string? Text(string name)
        => Json?.TryGetProperty(name, out var value) == true ? value.GetString() : null;

    /// <summary>Bir mantiksal alani okur.</summary>
    /// <param name="name">Alan adi.</param>
    /// <returns>Deger; alan yoksa <see langword="null"/>.</returns>
    public bool? Flag(string name)
        => Json?.TryGetProperty(name, out var value) == true ? value.GetBoolean() : null;
}

/// <summary>
/// Konusma protokolunu konusan kucuk bir test istemcisi.
/// </summary>
/// <remarks>
/// Tarayicinin yaptigi isin sunucuya bakan yarisini yapar: denetim mesajlarini
/// JSON metin cercevesi, sesi ikili cerceve olarak gonderir ve olaylari sirayla
/// okur.
/// </remarks>
internal sealed class VoiceConversationClient(WebSocket socket) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Bir denetim mesaji gonderir.</summary>
    /// <param name="payload">JSON'a cevrilecek nesne.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public Task SendControlAsync(object payload)
        => socket.SendAsync(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, Json)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

    /// <summary>Bir ses parcasi gonderir.</summary>
    /// <param name="audio">Ham baytlar.</param>
    /// <returns>Tamamlanma gorevi.</returns>
    public Task SendAudioAsync(byte[] audio)
        => socket.SendAsync(
            audio,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

    /// <summary>Adi verilen olay gelene kadar okur.</summary>
    /// <param name="type">Beklenen olay adi.</param>
    /// <param name="collected">Bu arada gelen tum cerceveler.</param>
    /// <returns>Beklenen olay.</returns>
    /// <exception cref="InvalidOperationException">Olay gelmeden soket kapanirsa.</exception>
    public async Task<VoiceEvent> WaitForAsync(string type, List<VoiceEvent>? collected = null)
    {
        while (true)
        {
            var next = await ReceiveAsync();

            if (next is not { } received)
            {
                throw new InvalidOperationException(
                    $"'{type}' olayi gelmeden baglanti kapandi. Gelenler: " +
                    string.Join(", ", collected?.Select(static frame => frame.Type ?? "<ses>") ?? []));
            }

            collected?.Add(received);

            if (string.Equals(received.Type, type, StringComparison.Ordinal))
            {
                return received;
            }
        }
    }

    /// <summary>Tek bir cerceve okur.</summary>
    /// <returns>Cerceve; soket kapandiysa <see langword="null"/>.</returns>
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
                // Sunucu once kapatmis olabilir.
            }
        }

        socket.Dispose();
    }
}
