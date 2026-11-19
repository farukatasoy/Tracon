using Microsoft.Extensions.AI;

namespace Tracon;

/// <summary>
/// The chat client factory a provider was set up with, and, when the host named the configuration keys of the key and the
/// address, the clients that follow them.
/// </summary>
/// <remarks>
/// Everything that sends the setup key of one OpenAI options instance asks this object for the factory at the time of the
/// call: both API surfaces, the image and the speech clients, the health check. When no configuration key is named
/// <see cref="Live"/> is <see langword="null"/> and <see cref="Initial"/> is the factory for the life of the host.
/// </remarks>
internal sealed class OpenAISetup(OpenAIChatClientFactory initial, LiveSetup<OpenAIChatClientFactory>? live)
{
    /// <summary>Gets the factory the host started with.</summary>
    internal OpenAIChatClientFactory Initial { get; } = initial;

    /// <summary>Gets the live setup, or <see langword="null"/> when the key and the address are fixed for the life of the host.</summary>
    internal LiveSetup<OpenAIChatClientFactory>? Live { get; } = live;

    /// <summary>Returns the factory of the key and the address that are current now.</summary>
    /// <returns>The factory.</returns>
    internal OpenAIChatClientFactory Current() => Live is null ? Initial : Live.Current();

    /// <summary>Creates the image generator of this setup.</summary>
    /// <param name="model">The image model.</param>
    /// <returns>
    /// A generator that shares the setup's client; when the key and the address are live it asks for the current client
    /// every time it generates.
    /// </returns>
#pragma warning disable MEAI001
    internal IImageGenerator CreateImageGenerator(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return Live is null
            ? Initial.CreateImageGenerator(model)
            : new OpenAIPipelineImageGenerator(() => Current().ImageTarget(), model, OpenAIProviderNames.ChatCompletions);
    }

    /// <summary>Creates the text-to-speech client of this setup.</summary>
    /// <param name="model">The speech model. The client is bound to it for its whole life.</param>
    /// <returns>A client that shares the setup's client; live setups ask for the current client on every request.</returns>
    internal ITextToSpeechClient CreateTextToSpeechClient(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return Live is null
            ? Initial.CreateTextToSpeechClient(model)
            : new OpenAITextToSpeechClient(new OpenAIAudioTransport(() => Current().AudioTarget()), model);
    }

    /// <summary>Creates the speech-to-text client of this setup.</summary>
    /// <param name="model">The transcription model. The client is bound to it for its whole life.</param>
    /// <returns>A client that shares the setup's client; live setups ask for the current client on every request.</returns>
    internal ISpeechToTextClient CreateSpeechToTextClient(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return Live is null
            ? Initial.CreateSpeechToTextClient(model)
            : new OpenAISpeechToTextClient(new OpenAIAudioTransport(() => Current().AudioTarget()), model);
    }
#pragma warning restore MEAI001
}
