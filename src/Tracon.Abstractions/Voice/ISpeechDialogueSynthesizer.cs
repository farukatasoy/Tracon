namespace Tracon;

/// <summary>
/// A speech provider that voices a dialogue: an ordered list of turns, each spoken by its
/// own voice, in one request and one audio result.
/// </summary>
/// <remarks>
/// <para>
/// A capability next to <see cref="ISpeechSynthesizer"/>, not a part of it: a provider
/// that speaks one voice per request implements no dialogue member. The same instance
/// implements both interfaces, and it is registered as an <see cref="ISpeechSynthesizer"/>
/// (the unnamed default, or by name with <c>AddSpeechSynthesizer</c>). It is NOT
/// registered as <see cref="ISpeechDialogueSynthesizer"/>: a caller takes the capability
/// from <see cref="ISpeechProviderResolver.GetDialogueSynthesizerAsync"/>, which applies
/// the tenant's checks first.
/// </para>
/// <para>
/// <strong>Splitting is the caller's job.</strong> A request that exceeds
/// <see cref="DialogueCapabilities"/> is refused before the service is called; the
/// implementation never splits or truncates a dialogue. The caller plans its requests from
/// <see cref="SpeechDialogueCapabilities.MaxVoices"/> and
/// <see cref="SpeechDialogueCapabilities.MaxCharactersPerRequest"/>, and joins the audio
/// itself.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> The capability lives on the synthesizer
/// instance, which is a container singleton; it holds no per-call state.
/// </para>
/// <para>
/// <strong>Tenant behavior — TENANT-INDEPENDENT implementation, tenant-gated calls.</strong>
/// The instance never reads the tenant. <see cref="ISpeechProviderResolver"/> applies the
/// tenant's egress policy to <see cref="ProviderName"/> and passes the tenant's own
/// credential in <see cref="SpeechDialogueRequest.Credential"/>, only to an
/// implementation whose <see cref="SupportsTenantCredentials"/> is <see langword="true"/>.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. One call is one request to
/// the provider; it is not retried here, and a failed call produces no audio.
/// </para>
/// <example>
/// A workflow function node that voices two turns with the default synthesizer:
/// <code>
/// var speech = provider.GetRequiredService&lt;ISpeechProviderResolver&gt;();
///
/// var dialogue = await speech.GetDialogueSynthesizerAsync(cancellationToken: cancellationToken);
/// var result = await dialogue.SynthesizeDialogueAsync(
///     new SpeechDialogueRequest
///     {
///         Turns =
///         [
///             new SpeechDialogueTurn { Text = "Welcome back.", VoiceId = "host-voice" },
///             new SpeechDialogueTurn { Text = "Glad to be here.", VoiceId = "guest-voice" },
///         ],
///         IncludeTimestamps = true,
///     },
///     cancellationToken);
///
/// foreach (var segment in result.Segments ?? [])
/// {
///     Console.WriteLine($"turn {segment.TurnIndex}: {segment.Start} - {segment.End}");
/// }
/// </code>
/// </example>
/// </remarks>
public interface ISpeechDialogueSynthesizer
{
    /// <summary>The provider name. Used in cost records and in the tenant's checks.</summary>
    /// <remarks>
    /// A tenant's egress policy and provider bindings name this value, not the name the
    /// synthesizer was registered under.
    /// </remarks>
    string ProviderName { get; }

    /// <summary>
    /// Gets whether the implementation honours a tenant credential passed in
    /// <see cref="SpeechDialogueRequest.Credential"/>.
    /// </summary>
    /// <remarks>
    /// The default is <see langword="false"/>. Tracon then refuses a dialogue for a tenant
    /// that has its own binding for <see cref="ProviderName"/> rather than make it on the
    /// configured credential, which would bill the wrong account. An implementation that
    /// returns <see langword="true"/> uses the credential for the call it came with and
    /// never falls back to its configured one.
    /// </remarks>
    bool SupportsTenantCredentials => false;

    /// <summary>Gets the limits of one dialogue request.</summary>
    SpeechDialogueCapabilities DialogueCapabilities { get; }

    /// <summary>Voices a dialogue.</summary>
    /// <param name="request">The turns and the settings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audio of the whole dialogue and, when requested, each turn's time range.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="TraconException">
    /// The request breaks a limit of <see cref="DialogueCapabilities"/>, a turn has no text
    /// or no voice, a provider setting is unknown or has the wrong type, or the provider
    /// answered with an error. The service is not called for the first three. The message
    /// names the limit or the key, never a turn's text or a setting's value.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// <see cref="SpeechDialogueRequest.Credential"/> is set and
    /// <see cref="SupportsTenantCredentials"/> is <see langword="false"/>.
    /// </exception>
    ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(
        SpeechDialogueRequest request,
        CancellationToken cancellationToken = default);
}
