// MEAI001: Microsoft.Extensions.AI marks its speech clients "experimental" (10.11.0).
// This file and OpenAIChatClientFactory are the only places in Tracon.OpenAI that name
// them for speech; no public member does (YAYIN-HAZIRLIK criterion 5).
#pragma warning disable MEAI001

using System.Buffers;
using System.ClientModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>The container key an OpenAI speech client is registered under.</summary>
/// <remarks>
/// A private record, not a string: a consumer's own keyed client cannot collide with it,
/// and nothing outside <c>UseOpenAISpeech</c> can resolve it by accident.
/// </remarks>
/// <param name="Kind">Whether the client synthesizes or transcribes.</param>
/// <param name="Name">The registration name of the Tracon provider the client serves.</param>
internal sealed record OpenAISpeechClientKey(SpeechProviderKind Kind, string Name);

/// <summary>Registers the OpenAI speech clients that <c>UseOpenAISpeech</c> binds.</summary>
internal static class OpenAISpeechClientRegistration
{
    /// <summary>Registers a text-to-speech client bound to one model.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="key">The key the Tracon binding names.</param>
    /// <param name="model">The model.</param>
    public static void AddTextToSpeech(IServiceCollection services, OpenAISpeechClientKey key, string model)
        => services.AddKeyedSingleton<ITextToSpeechClient>(
            key,
            (provider, _) => OpenAIFactoryFor(provider).CreateTextToSpeechClient(model));

    /// <summary>Registers a speech-to-text client bound to one model.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="key">The key the Tracon binding names.</param>
    /// <param name="model">The model.</param>
    public static void AddSpeechToText(IServiceCollection services, OpenAISpeechClientKey key, string model)
        => services.AddKeyedSingleton<ISpeechToTextClient>(
            key,
            (provider, _) => OpenAIFactoryFor(provider).CreateSpeechToTextClient(model));

    private static OpenAIChatClientFactory OpenAIFactoryFor(IServiceProvider provider)
        => provider.GetService<OpenAIChatClientFactory>()
            ?? throw new TraconException(
                "UseOpenAISpeech shares the OpenAI client of UseOpenAI, and UseOpenAI was not called. " +
                "Call UseOpenAI(...) on the same Tracon chain.");
}

// Measurements behind the shape of these two clients (2026-10-19, Microsoft.Extensions.AI.OpenAI 10.11.0,
// OpenAI 2.14.0, System.ClientModel 1.16.0):
//  1. The MEAI adapter's model on the wire is always the audio client's. TextToSpeechOptions.ModelId and
//     SpeechToTextOptions.ModelId are ignored on the wire, yet the text-to-speech response reports the
//     requested model as if it had been used: a price looked up under it would be wrong.
//  2. The adapter streams speech as server-sent events, which OpenAI documents as unavailable for tts-1 and
//     tts-1-hd. The endpoint's default stream is raw chunked audio, which every model serves.
//  3. The adapter sends every transcription stream as "audio.mp3".
//  4. AudioClient cannot be published with Native AOT (see OpenAIAudioTransport).
//
/// <summary>
/// An OpenAI text-to-speech client bound to one model, written over the shared client's pipeline
/// instead of <c>AudioClient</c> or the adapter Microsoft.Extensions.AI.OpenAI offers.
/// </summary>
/// <remarks>
/// <para>
/// It asks for one model and reports it, sends the container the service knows, refuses a setting it
/// does not read, and streams the audio as the service sends it: raw chunks, not server-sent events.
/// </para>
/// </remarks>
internal sealed class OpenAITextToSpeechClient : ITextToSpeechClient
{
    /// <summary>The voice the service needs when a request names none. The service refuses a body without a voice.</summary>
    internal const string DefaultVoice = "alloy";

    private readonly OpenAIAudioTransport _transport;
    private readonly string _model;
    private readonly TextToSpeechClientMetadata _metadata;

    public OpenAITextToSpeechClient(OpenAIAudioTransport transport, string model)
    {
        _transport = transport;
        _model = model;
        _metadata = new TextToSpeechClientMetadata(OpenAIProviderNames.ChatCompletions, providerUri: null, model);
    }

    /// <inheritdoc />
    public async Task<TextToSpeechResponse> GetAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var (body, container) = BuildRequest(text, options);

        using var content = BinaryContent.Create(body);
        var audio = await _transport.PostAsync("audio/speech", content, "application/json", cancellationToken).ConfigureAwait(false);

        return new TextToSpeechResponse([new DataContent(audio.ToMemory(), OpenAIAudioModels.MediaType(container))])
        {
            ModelId = _model,
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TextToSpeechResponseUpdate> GetStreamingAudioAsync(
        string text,
        TextToSpeechOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var (body, container) = BuildRequest(text, options);
        var mediaType = OpenAIAudioModels.MediaType(container);

        using var content = BinaryContent.Create(body);

        await foreach (var chunk in _transport
                           .PostStreamingAsync("audio/speech", content, "application/json", cancellationToken)
                           .WithCancellation(cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return new TextToSpeechResponseUpdate([new DataContent(chunk, mediaType)]) { ModelId = _model };
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        return serviceType.IsInstanceOfType(_metadata)
            ? _metadata
            : serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>Builds the JSON body and reads the container the request asks for.</summary>
    private (BinaryData Body, string? Container) BuildRequest(string text, TextToSpeechOptions? options)
    {
        var container = Validate(options);
        var voice = string.IsNullOrWhiteSpace(options?.VoiceId) ? DefaultVoice : options.VoiceId;

        var body = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(body))
        {
            writer.WriteStartObject();
            writer.WriteString("model", _model);
            writer.WriteString("input", text);
            writer.WriteString("voice", voice);

            if (container is not null)
            {
                writer.WriteString("response_format", container);
            }

            if (options?.Speed is { } speed)
            {
                writer.WriteNumber("speed", speed);
            }

            writer.WriteEndObject();
        }

        return (BinaryData.FromBytes(body.WrittenMemory), container);
    }

    /// <summary>Refuses what the service cannot honour and reads the container name.</summary>
    private string? Validate(TextToSpeechOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        OpenAIAudioModels.RefuseOtherModel(options.ModelId, _model);

        if (options.AdditionalProperties is { Count: > 0 } settings)
        {
            OpenAIAudioModels.RefuseSettings(settings.Keys);
        }

        if (options.Pitch is not null || options.Volume is not null)
        {
            throw new TraconException("OpenAI speech has no pitch or volume setting.");
        }

        return OpenAIAudioModels.ContainerName(options.AudioFormat);
    }
}

/// <summary>
/// An OpenAI speech-to-text client bound to one model, written over the shared client's pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The same shape as <see cref="OpenAITextToSpeechClient"/>. The file name follows the bytes
/// (<see cref="OpenAIAudioModels.FileNameFor"/>), because the service reads the container from it.
/// </para>
/// <para>
/// The answer is read as <c>{"text": ...}</c>; no duration is read from it, so a transcription has none.
/// </para>
/// </remarks>
internal sealed class OpenAISpeechToTextClient : ISpeechToTextClient
{
    private readonly OpenAIAudioTransport _transport;
    private readonly string _model;
    private readonly SpeechToTextClientMetadata _metadata;

    public OpenAISpeechToTextClient(OpenAIAudioTransport transport, string model)
    {
        _transport = transport;
        _model = model;
        _metadata = new SpeechToTextClientMetadata(OpenAIProviderNames.ChatCompletions, providerUri: null, model);
    }

    /// <inheritdoc />
    public async Task<SpeechToTextResponse> GetTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audioSpeechStream);

        if (options is not null)
        {
            OpenAIAudioModels.RefuseOtherModel(options.ModelId, _model);

            if (options.AdditionalProperties is { Count: > 0 } settings)
            {
                OpenAIAudioModels.RefuseSettings(settings.Keys);
            }
        }

        using var audio = new MemoryStream();
        await audioSpeechStream.CopyToAsync(audio, cancellationToken).ConfigureAwait(false);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(_model), "model");

        if (!string.IsNullOrWhiteSpace(options?.SpeechLanguage))
        {
            form.Add(new StringContent(options.SpeechLanguage), "language");
        }

        form.Add(
            new ByteArrayContent(audio.GetBuffer(), 0, (int)audio.Length),
            "file",
            OpenAIAudioModels.FileNameFor(audio.GetBuffer().AsSpan(0, (int)Math.Min(audio.Length, 16))));

        var contentType = form.Headers.ContentType!.ToString();
        var payload = await form.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        using var content = BinaryContent.Create(BinaryData.FromBytes(payload));
        var answer = await _transport.PostAsync("audio/transcriptions", content, contentType, cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(answer);
        var text = document.RootElement.TryGetProperty("text", out var property) ? property.GetString() : null;

        return new SpeechToTextResponse(text ?? string.Empty) { ModelId = _model };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<SpeechToTextResponseUpdate> GetStreamingTextAsync(
        Stream audioSpeechStream,
        SpeechToTextOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("A Tracon speech transcriber is single-shot; streaming transcription is not offered.");

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        if (serviceKey is not null)
        {
            return null;
        }

        return serviceType.IsInstanceOfType(_metadata)
            ? _metadata
            : serviceType.IsInstanceOfType(this) ? this : null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
