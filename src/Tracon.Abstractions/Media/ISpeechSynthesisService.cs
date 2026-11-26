namespace Tracon;

/// <summary>
/// Voices text and dialogue from the host's own code under the tenant's provider rules, with the spend measured.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISpeechProviderResolver"/> returns a synthesizer that obeys the tenant's rules, and calling it records nothing.
/// This service resolves the synthesizer the same way, calls it and measures what it billed.
/// </para>
/// <para>
/// <strong>The measurement.</strong> Inside a run (a step run opened with <see cref="ITraconStepRunner"/>, a workflow function node,
/// or a tool of an agent run) the call's spend is recorded as a non-token item of that run, which is part of the run's
/// <c>nonTokenCost</c>, its budget and the tenant's quota; the result's <c>UsageRecorded</c> is then <see langword="true"/>. Outside
/// a run nothing is recorded, and the result still carries the measurement. The amount comes from
/// <c>Tracon:Pricing:Voice</c> (or a registered <see cref="IVoicePricingReader"/>) per billed character; with no price it is
/// <see langword="null"/>, never zero.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered by <c>AddTracon()</c> with <c>TryAdd</c>. Call this interface; do not
/// implement or replace it: the tenant checks and the measurement run inside it. To add a provider, register an
/// <see cref="ISpeechSynthesizer"/>.
/// </para>
/// <para>
/// <strong>Tenant behavior — AMBIENT tenant.</strong> The current run's tenant, or the current <c>ITenantContext</c> outside a run.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. One call sends one request; a failed call is not repeated by this
/// service.
/// </para>
/// </remarks>
public interface ISpeechSynthesisService
{
    /// <summary>Voices a text with one voice.</summary>
    /// <param name="request">The text, and optionally the voice, model, format and provider settings.</param>
    /// <param name="provider">The name of a registered synthesizer, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audio, and what the call spent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The text is blank.</exception>
    /// <exception cref="TraconException">
    /// No synthesizer is registered under the name, the text is longer than the synthesizer takes, the provider is refused for the
    /// tenant, or the provider failed (a <c>ProviderInvocationException</c> for the built-in client; a synthesizer of your own, or
    /// the OpenAI and Gemini speech clients, throw what they throw).
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapPost("/welcome", async (ISpeechSynthesisService speech, CancellationToken cancellationToken) =>
    /// {
    ///     var spoken = await speech.SynthesizeAsync(
    ///         new SpeechRequest { Text = "Welcome to the show.", VoiceId = "narrator" },
    ///         provider: "batch",
    ///         cancellationToken);
    ///
    ///     return Results.Bytes(spoken.Audio.Data.ToArray(), spoken.Audio.MediaType);
    /// });
    /// </code>
    /// </example>
    ValueTask<SpeechSynthesisResult> SynthesizeAsync(
        SpeechRequest request,
        string? provider = null,
        CancellationToken cancellationToken = default);

    /// <summary>Voices a dialogue with several speakers in one request.</summary>
    /// <param name="request">The turns, and optionally the model, format and provider settings.</param>
    /// <param name="provider">The name of a registered synthesizer, or <see langword="null"/> for the default.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The audio with its segments, and what the call spent.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The request has no turns.</exception>
    /// <exception cref="TraconException">
    /// No synthesizer is registered under the name or it cannot voice a dialogue, the dialogue is over a limit, the provider is
    /// refused for the tenant, or the provider failed (a <c>ProviderInvocationException</c> for the built-in client).
    /// </exception>
    ValueTask<SpeechDialogueSynthesisResult> SynthesizeDialogueAsync(
        SpeechDialogueRequest request,
        string? provider = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What one synthesis call returned and spent.</summary>
public sealed record SpeechSynthesisResult
{
    /// <summary>Gets the audio.</summary>
    public required SpeechAudio Audio { get; init; }

    /// <summary>Gets the measurement: the provider and model, the billed characters, and the cost when a price is configured.</summary>
    public required ToolCallUsage Usage { get; init; }

    /// <summary>
    /// Gets whether the measurement was reported to a run. <see langword="false"/> outside a run, and when the run already holds
    /// the most items it can.
    /// </summary>
    /// <remarks>
    /// A store that cannot write the item does not change it: the failure is logged and the amount still counts toward the run's
    /// quota entry and webhook. A call that ends after its run has closed is still reported, and its amount stays in the run's
    /// items only: the run's total, its budget and its quota entry were written when it closed.
    /// </remarks>
    public required bool UsageRecorded { get; init; }
}

/// <summary>What one dialogue call returned and spent.</summary>
public sealed record SpeechDialogueSynthesisResult
{
    /// <summary>Gets the dialogue: the audio and, when asked for, its segments.</summary>
    public required SpeechDialogueAudio Dialogue { get; init; }

    /// <summary>Gets the measurement: the provider and model, the billed characters, and the cost when a price is configured.</summary>
    public required ToolCallUsage Usage { get; init; }

    /// <summary>
    /// Gets whether the measurement was reported to a run. <see langword="false"/> outside a run, and when the run already holds
    /// the most items it can.
    /// </summary>
    /// <remarks>
    /// A store that cannot write the item does not change it: the failure is logged and the amount still counts toward the run's
    /// quota entry and webhook. A call that ends after its run has closed is still reported, and its amount stays in the run's
    /// items only: the run's total, its budget and its quota entry were written when it closed.
    /// </remarks>
    public required bool UsageRecorded { get; init; }
}
