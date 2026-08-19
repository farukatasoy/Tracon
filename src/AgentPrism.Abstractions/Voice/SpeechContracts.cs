namespace AgentPrism;

/// <summary>A provider that generates speech from text.</summary>
/// <remarks>
/// <para>
/// ElevenLabs is an <em>implementation</em>, not a dependency. A consumer
/// wanting another provider registers their own implementation; tools know
/// only this contract.
/// </para>
/// <para>
/// The contract lives HERE, not in <c>AgentPrism.Voice</c>: the HTTP layer
/// (<c>AgentPrism.AspNetCore</c>) sees these types while serving the voice
/// endpoints, but it CANNOT reference <c>AgentPrism.Voice</c> (the package
/// direction rule). The same pattern was applied to the MCP abstractions — K-174.
/// </para>
/// </remarks>
public interface ISpeechSynthesizer
{
    /// <summary>The provider name. Used in cost records and health checks.</summary>
    string ProviderName { get; }

    /// <summary>
    /// The maximum number of characters accepted in a single
    /// <see cref="SynthesizeAsync"/> call.
    /// </summary>
    /// <remarks>
    /// The tool call (<c>speak</c>) AND the direct HTTP operator endpoint
    /// (<c>POST /api/voice/speak</c>) read the SAME value — the limit is
    /// applied consistently across both paths.
    /// </remarks>
    int MaxCharactersPerRequest { get; }

    /// <summary>Synthesizes speech from a text.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated audio.</returns>
    ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default);

    /// <summary>Synthesizes speech from a text and streams the audio piece by piece.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audio chunks.</returns>
    /// <remarks>
    /// <para>
    /// The streaming path is for low latency, and Phase 29's voice layer uses
    /// it. The signature is defined <strong>now</strong>: adding a member to
    /// an interface later is a breaking change for consumers implementing that interface.
    /// </para>
    /// <para>
    /// 🚨 Streamed audio is <strong>not stored as an attachment</strong>. The
    /// billed quantity is unknown until the stream ends, and the chunks are
    /// not a valid file on their own; the <c>speak</c> tool uses the
    /// non-streaming path for this reason.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the available voices.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The voices, ordered by name.</returns>
    ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>A provider that transcribes speech to text.</summary>
/// <remarks>
/// The contract is single-shot. The <em>incremental</em> resolution Phase 29
/// will need should be a separate interface; adding a member here breaks consumer implementations.
/// </remarks>
public interface ISpeechTranscriber
{
    /// <summary>The provider name.</summary>
    string ProviderName { get; }

    /// <summary>Transcribes an audio stream to text.</summary>
    /// <param name="audio">The audio stream.</param>
    /// <param name="mediaType">The audio's MIME type.</param>
    /// <param name="options">The settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The transcribed text.</returns>
    ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the configured voice model's pricing.
/// </summary>
/// <remarks>
/// The HTTP layer uses this abstraction to show voice pricing; pricing
/// resolution is inside <c>AgentPrism.Voice</c>, and the HTTP layer cannot
/// reference that package (the package direction rule). AgentPrism does not
/// fabricate a price (K-032): if there is no match, <see langword="null"/> is
/// returned — <strong>not</strong> zero.
/// </remarks>
public interface IVoicePricingReader
{
    /// <summary>The currency label shown in reports.</summary>
    string? Currency { get; }

    /// <summary>Computes a character-based generation's cost.</summary>
    /// <param name="characters">The billed character count.</param>
    /// <returns>The amount; <see langword="null"/> if pricing is undefined.</returns>
    decimal? ForCharacters(decimal characters);
}

/// <summary>A speech synthesis request as an operator action.</summary>
public sealed record SpeakRequest
{
    /// <summary>The text to speak.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The session the attachment is bound to. 🚨 If left empty, the
    /// attachment is considered orphaned and deleted by the retention policy.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>The identifier of the voice to use. The default voice if empty.</summary>
    public string? VoiceId { get; init; }

    /// <summary>Requests character-level timing alongside the audio. Default <see langword="false"/>.</summary>
    public bool IncludeTimestamps { get; init; }
}

/// <summary>The result of speech synthesis.</summary>
public sealed record SpeakResponse
{
    /// <summary>The saved attachment.</summary>
    public required AttachmentDescriptor Attachment { get; init; }

    /// <summary>The billed character count.</summary>
    public required int Characters { get; init; }

    /// <summary>Whether the character count is estimated.</summary>
    public required bool IsEstimated { get; init; }

    /// <summary>The computed amount. <see langword="null"/> if pricing is undefined.</summary>
    public decimal? Cost { get; init; }

    /// <summary>The currency label.</summary>
    public string? Currency { get; init; }

    /// <summary>
    /// Character-level timing. <see langword="null"/> when not requested via
    /// <see cref="SpeakRequest.IncludeTimestamps"/>, or when the provider does not support it.
    /// </summary>
    public IReadOnlyList<SpeechAlignment>? Alignment { get; init; }
}

/// <summary>Checks the voice provider's reachability.</summary>
public interface IVoiceHealthCheck
{
    /// <summary>Checks access to the provider.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status.</returns>
    /// <remarks>The check <strong>produces no charge</strong>: no audio is generated, only the list is read.</remarks>
    ValueTask<VoiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
