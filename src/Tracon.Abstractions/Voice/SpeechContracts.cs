namespace Tracon;

/// <summary>A provider that generates speech from text.</summary>
/// <remarks>
/// <para>
/// ElevenLabs is an <em>implementation</em>, not a dependency. A consumer
/// wanting another provider registers their own implementation; tools know
/// only this contract.
/// </para>
/// <para>
/// The contract lives HERE, not in <c>Tracon.Voice</c>: the HTTP layer
/// (<c>Tracon.AspNetCore</c>) sees these types while serving the voice
/// endpoints, but it CANNOT reference <c>Tracon.Voice</c> (the package
/// direction rule). The same pattern was applied to the MCP abstractions —.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> The unnamed default is registered as a
/// singleton with <c>TryAdd</c>; a consumer's own registration wins. More
/// synthesizers are registered by name with <c>AddSpeechSynthesizer</c>.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT implementation, tenant-gated calls.</strong>
/// An instance is configured once, with its own credentials; it never reads the
/// tenant and no member takes a tenant parameter. Tracon never calls an instance
/// directly: it resolves it through
/// <see cref="ISpeechProviderResolver"/>, which applies the tenant's egress policy to
/// <see cref="ProviderName"/> and, when the tenant has its own credential for that
/// provider, passes it in <see cref="SpeechRequest.Credential"/> — only to an
/// implementation whose <see cref="SupportsTenantCredentials"/> is
/// <see langword="true"/>.
/// </para>
/// </remarks>
public interface ISpeechSynthesizer
{
    /// <summary>The provider name. Used in cost records and health checks.</summary>
    /// <remarks>
    /// A tenant's egress policy and provider bindings name this value. Two
    /// registrations that report the same name are one provider to the tenant's
    /// checks.
    /// </remarks>
    string ProviderName { get; }

    /// <summary>
    /// Gets whether the synthesizer honours a tenant credential passed in
    /// <see cref="SpeechRequest.Credential"/> and to
    /// <see cref="ListVoicesWithCredentialAsync"/>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>. Tracon then refuses a call for a tenant
    /// that has its own binding for <see cref="ProviderName"/> rather than make it on
    /// the configured credential, which would bill the wrong account. An
    /// implementation that returns <see langword="true"/> uses the credential for the
    /// call it came with and never falls back to its configured one.
    /// </remarks>
    bool SupportsTenantCredentials => false;

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
    /// The streaming path is for low latency, and the voice layer uses
    /// it. The signature is defined <strong>now</strong>: adding a member to
    /// an interface later is a breaking change for consumers implementing that interface.
    /// </para>
    /// <para>
    /// Streamed audio is <strong>not stored as an attachment</strong>. The
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

    /// <summary>Lists the available voices with a tenant's own credential.</summary>
    /// <param name="credential">
    /// The tenant's credential, or <see langword="null"/> for the configured one.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The voices, ordered by name.</returns>
    /// <exception cref="NotSupportedException">
    /// <paramref name="credential"/> is set and the implementation does not override
    /// this method.
    /// </exception>
    /// <remarks>
    /// The default implementation calls <see cref="ListVoicesAsync(CancellationToken)"/>
    /// when no credential is given and throws when one is: an implementation that
    /// cannot use the tenant's credential must not list with its own instead. An
    /// implementation whose <see cref="SupportsTenantCredentials"/> is
    /// <see langword="true"/> overrides it.
    /// </remarks>
    ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesWithCredentialAsync(
        ModelProviderCredential? credential,
        CancellationToken cancellationToken = default)
        => credential is null
            ? ListVoicesAsync(cancellationToken)
            : throw new NotSupportedException(
                $"The speech synthesizer '{ProviderName}' cannot list voices with a tenant credential.");
}

/// <summary>A provider that transcribes speech to text.</summary>
/// <remarks>
/// <para>
/// The contract is single-shot. The <em>incremental</em> resolution the live conversation layer
/// will need should be a separate interface; adding a member here breaks consumer implementations.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> The unnamed default is registered as a
/// singleton with <c>TryAdd</c>; a consumer's own registration wins. More
/// transcribers are registered by name with <c>AddSpeechTranscriber</c>.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT implementation, tenant-gated calls.</strong>
/// The same rule as <see cref="ISpeechSynthesizer"/>: the instance never reads the
/// tenant, and Tracon resolves it
/// through the tenant's checks, and passes the tenant's own credential in
/// <see cref="SpeechTranscriptionOptions.Credential"/> only when
/// <see cref="SupportsTenantCredentials"/> is <see langword="true"/>.
/// </para>
/// </remarks>
public interface ISpeechTranscriber
{
    /// <summary>The provider name.</summary>
    /// <remarks>A tenant's egress policy and provider bindings name this value.</remarks>
    string ProviderName { get; }

    /// <summary>
    /// Gets whether the transcriber honours a tenant credential passed in
    /// <see cref="SpeechTranscriptionOptions.Credential"/>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>: Tracon refuses a call for a tenant that
    /// has its own binding for <see cref="ProviderName"/> rather than make it on the
    /// configured credential.
    /// </remarks>
    bool SupportsTenantCredentials => false;

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
/// <para>
/// The HTTP layer uses this abstraction to show voice pricing; pricing
/// resolution is inside <c>Tracon.Voice</c>, and the HTTP layer cannot
/// reference that package (the package direction rule). Tracon does not
/// fabricate a price: if there is no match, <see langword="null"/> is
/// returned — <strong>not</strong> zero.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> Pricing is
/// configured process-wide, not per tenant; no member here takes a tenant
/// parameter.
/// </para>
/// </remarks>
public interface IVoicePricingReader
{
    /// <summary>The currency label shown in reports.</summary>
    string? Currency { get; }

    /// <summary>Computes a character-based generation's cost for the default synthesizer.</summary>
    /// <param name="characters">The billed character count.</param>
    /// <returns>The amount; <see langword="null"/> if pricing is undefined.</returns>
    /// <remarks>
    /// Uses the default synthesizer's provider name and configured model. A call
    /// that knows which provider and model produced the audio uses
    /// <see cref="ForSynthesis"/> instead.
    /// </remarks>
    decimal? ForCharacters(decimal characters);

    /// <summary>Computes the cost of a synthesis by the provider and model that produced it.</summary>
    /// <param name="providerName">The <see cref="ISpeechSynthesizer.ProviderName"/> of the synthesizer.</param>
    /// <param name="modelId">
    /// The model the synthesizer reported (<see cref="SpeechAudio.ModelId"/>), or the
    /// one the request named; <see langword="null"/> when neither is known.
    /// </param>
    /// <param name="characters">The billed character count.</param>
    /// <returns>The amount; <see langword="null"/> if pricing is undefined.</returns>
    /// <remarks>The default implementation returns <see langword="null"/>.</remarks>
    decimal? ForSynthesis(string providerName, string? modelId, decimal characters) => null;

    /// <summary>Computes the cost of a transcription by the provider and model that produced it.</summary>
    /// <param name="providerName">The <see cref="ISpeechTranscriber.ProviderName"/> of the transcriber.</param>
    /// <param name="modelId">
    /// The model the transcriber reported (<see cref="SpeechTranscript.ModelId"/>), or
    /// the one the request named; <see langword="null"/> when neither is known.
    /// </param>
    /// <param name="duration">The duration of the transcribed audio.</param>
    /// <returns>The amount; <see langword="null"/> if pricing is undefined.</returns>
    /// <remarks>The default implementation returns <see langword="null"/>.</remarks>
    decimal? ForTranscription(string providerName, string? modelId, TimeSpan duration) => null;
}

/// <summary>A speech synthesis request as an operator action.</summary>
public sealed record SpeakRequest
{
    /// <summary>The text to speak.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// The session the attachment is bound to. If left empty, the
    /// attachment is considered orphaned and deleted by the retention policy.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>The identifier of the voice to use. The default voice if empty.</summary>
    public string? VoiceId { get; init; }

    /// <summary>Requests character-level timing alongside the audio. Default <see langword="false"/>.</summary>
    public bool IncludeTimestamps { get; init; }

    /// <summary>
    /// The registration name of the synthesizer to use; <see langword="null"/> for the
    /// default one.
    /// </summary>
    /// <remarks>
    /// A name registered with <c>AddSpeechSynthesizer</c>, compared in lower case.
    /// <c>GET /api/voice/providers</c> lists the registered names. The tenant's egress
    /// policy applies to the provider name the synthesizer reports, not to this value.
    /// </remarks>
    public string? Provider { get; init; }
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
    /// <c>SpeakRequest.IncludeTimestamps</c>, or when the provider does not support it.
    /// </summary>
    public IReadOnlyList<SpeechAlignment>? Alignment { get; init; }
}

/// <summary>Checks the voice provider's reachability.</summary>
/// <remarks>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered as a singleton with
/// <c>TryAdd</c>; a consumer's own registration wins.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT.</strong> The check reads the
/// default synthesizer with its configured credential: it is a platform check, not a
/// tenant's call, and sends no tenant data.
/// </para>
/// </remarks>
public interface IVoiceHealthCheck
{
    /// <summary>Checks access to the provider.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The status.</returns>
    /// <remarks>The check <strong>produces no charge</strong>: no audio is generated, only the list is read.</remarks>
    ValueTask<VoiceHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
