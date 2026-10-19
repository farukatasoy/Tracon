namespace Tracon;

/// <summary>
/// Binds a <c>Microsoft.Extensions.AI</c> text-to-speech client in the container to a named
/// Tracon speech synthesizer.
/// </summary>
/// <remarks>
/// <para>
/// Pass it to <see cref="TraconBuilderExtensions.AddSpeechSynthesizer(ITraconBuilder, string, TextToSpeechClientBinding)"/>.
/// The client itself stays in the container: register it with the extension of its
/// own package, or with <c>AddTextToSpeechClient</c> / <c>AddKeyedTextToSpeechClient</c>.
/// </para>
/// <para>
/// The record declares what the client cannot say about itself. Microsoft.Extensions.AI
/// reports no character limit, no voice list and, for many clients, no provider name,
/// and Tracon needs all three: the limit guards <c>speak</c> and
/// <c>POST /api/voice/speak</c>, the list feeds <c>GET /api/voice/voices</c>, and the name
/// is what a tenant's egress policy and provider bindings name.
/// </para>
/// </remarks>
public sealed record TextToSpeechClientBinding
{
    /// <summary>
    /// Gets the service key the client is registered under, or <see langword="null"/> for the
    /// unkeyed registration.
    /// </summary>
    public object? ServiceKey { get; init; }

    /// <summary>
    /// Gets the provider name, or <see langword="null"/> to read it from the client's metadata.
    /// </summary>
    /// <remarks>
    /// A tenant's egress policy and provider bindings name this value, so it must be
    /// the process's own word for the service, not the client's. A client with neither this
    /// name nor metadata stops the host at startup: a provider with no name could not be
    /// checked against any policy.
    /// </remarks>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Gets the most characters one synthesis call accepts. Must be greater than zero.
    /// </summary>
    /// <remarks>
    /// Read by the <c>speak</c> tool and by <c>POST /api/voice/speak</c>, which refuse longer
    /// text before the provider is called.
    /// </remarks>
    public required int MaxCharactersPerRequest { get; init; }

    /// <summary>
    /// Gets the voices <c>GET /api/voice/voices</c> lists for this synthesizer. Empty by default.
    /// </summary>
    /// <remarks>
    /// A static declaration: the client has no way to list its voices. A call that names no
    /// voice leaves the choice to the client.
    /// </remarks>
    public IReadOnlyList<VoiceDescriptor> Voices { get; init; } = [];
}

/// <summary>
/// Binds a <c>Microsoft.Extensions.AI</c> speech-to-text client in the container to a named
/// Tracon speech transcriber.
/// </summary>
/// <remarks>
/// Pass it to <see cref="TraconBuilderExtensions.AddSpeechTranscriber(ITraconBuilder, string, SpeechToTextClientBinding)"/>.
/// The client stays in the container, as for <see cref="TextToSpeechClientBinding"/>.
/// </remarks>
public sealed record SpeechToTextClientBinding
{
    /// <summary>
    /// Gets the service key the client is registered under, or <see langword="null"/> for the
    /// unkeyed registration.
    /// </summary>
    public object? ServiceKey { get; init; }

    /// <summary>
    /// Gets the provider name, or <see langword="null"/> to read it from the client's metadata.
    /// </summary>
    /// <remarks>
    /// The same rule as <see cref="TextToSpeechClientBinding.ProviderName"/>: a tenant's egress
    /// policy names this value, and a client with no name stops the host at startup.
    /// </remarks>
    public string? ProviderName { get; init; }
}
