using System.Text.Json.Serialization;

namespace Tracon;

/// <summary>
/// Returns a speech provider by name, after the calling tenant's provider checks.
/// </summary>
/// <remarks>
/// <para>
/// Every Tracon path that sends a tenant's text or audio to a speech provider
/// resolves the provider here: the <c>speak</c>, <c>transcribe</c> and
/// <c>list_voices</c> tools, <c>POST /api/voice/speak</c>, <c>GET /api/voice/voices</c>
/// and the conversation socket. A workflow function node that speaks or voices a
/// dialogue resolves it here too. The one direct call is the platform health check
/// (<c>GET /api/voice/health</c>): it lists the default synthesizer's voices with the
/// configured credential and sends no tenant data.
/// </para>
/// <para>
/// A name selects a provider registered with <c>AddSpeechSynthesizer</c> or
/// <c>AddSpeechTranscriber</c>. <see langword="null"/> selects the unnamed default,
/// the one <c>UseVoice()</c> or the consumer's own <see cref="ISpeechSynthesizer"/>
/// registration binds. Names are compared in lower case.
/// </para>
/// <para>
/// <strong>Order of the checks.</strong> The tenant's egress policy is checked
/// first, against the instance's <see cref="ISpeechSynthesizer.ProviderName"/> (not
/// the registration name, so an alias cannot pass a policy). Then the tenant's own
/// credential for that provider is looked up. When the tenant has one, the provider
/// must support tenant credentials and the binding must not carry an endpoint
/// override; otherwise the call is refused rather than sent on the setup credential.
/// The returned provider puts the tenant's credential on every request it makes.
/// </para>
/// <para>
/// The policy and the binding are read each time a provider is resolved, not
/// cached. A tool call, an HTTP request and a function node resolve for every call,
/// so a policy saved now stops their next call. The conversation socket resolves
/// once, when the connection opens: an open connection keeps the providers it was
/// given until it closes, and a policy saved meanwhile applies from the next
/// connection. A returned instance keeps the decision it was resolved under; do not
/// keep it. When a store fails, the call fails and the provider is not called.
/// </para>
/// <para>
/// <strong>Delivery:</strong> no delivery guarantee applies. Resolving sends nothing to
/// the provider; the call made with the returned instance is the caller's, with that
/// provider's own guarantee.
/// </para>
/// <para>
/// <strong>DI lifetime — singleton.</strong> Registered by <c>AddTracon()</c>.
/// Call this interface; do not implement or replace it. The tenant's egress policy and
/// credential checks above run inside it, and a replacement would send a tenant's text
/// or audio to a provider without them. To add a provider, register an
/// <see cref="ISpeechSynthesizer"/> or <see cref="ISpeechTranscriber"/> as described
/// above.
/// </para>
/// <para>
/// <strong>Tenant behavior — AMBIENT tenant.</strong> No member takes a tenant
/// parameter; the checks use the current run's tenant, or the current
/// <c>ITenantContext</c> outside a run. The returned provider can carry that
/// tenant's credential: resolve it for each call and do not keep it for another
/// tenant.
/// </para>
/// <example>
/// A workflow function node that speaks with the engine named in the node's settings:
/// <code>
/// builder.AddTracon()
///        .AddSpeechSynthesizer("studio", provider => new StudioSpeechSynthesizer())
///        .UseWorkflows()
///        .AddWorkflowFunction&lt;List&lt;ChatMessage&gt;, List&lt;ChatMessage&gt;, NarrationSettings&gt;(
///            "narrate",
///            provider =>
///            {
///                var speech = provider.GetRequiredService&lt;ISpeechProviderResolver&gt;();
///
///                return async (messages, settings, context, cancellationToken) =>
///                {
///                    var synthesizer = await speech.GetSynthesizerAsync(settings.Provider, cancellationToken);
///                    var audio = await synthesizer.SynthesizeAsync(
///                        new SpeechRequest { Text = messages[^1].Text },
///                        cancellationToken);
///
///                    return [new ChatMessage(ChatRole.Assistant, $"{audio.Data.Length} bytes")];
///                };
///            });
/// </code>
/// </example>
/// </remarks>
public interface ISpeechProviderResolver
{
    /// <summary>Gets the registered speech providers, the unnamed defaults included.</summary>
    /// <remarks>
    /// Synthesizers come first, then transcribers; in each kind the unnamed default
    /// comes first, then the named providers in ordinal order of their names. Reading
    /// the list applies no tenant check and calls no provider.
    /// </remarks>
    IReadOnlyList<SpeechProviderDescriptor> Providers { get; }

    /// <summary>Returns a speech synthesizer for the current tenant.</summary>
    /// <param name="name">
    /// The registration name, or <see langword="null"/> for the unnamed default.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The synthesizer, carrying the tenant's credential when it has one.</returns>
    /// <exception cref="TraconException">
    /// No synthesizer is registered under <paramref name="name"/>, or no default is
    /// registered; or the tenant's checks refuse the provider. The message names the
    /// tenant and the provider, never a key value.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ISpeechSynthesizer> GetSynthesizerAsync(string? name = null, CancellationToken cancellationToken = default);

    /// <summary>Returns a speech transcriber for the current tenant.</summary>
    /// <param name="name">
    /// The registration name, or <see langword="null"/> for the unnamed default.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The transcriber, carrying the tenant's credential when it has one.</returns>
    /// <exception cref="TraconException">
    /// No transcriber is registered under <paramref name="name"/>, or no default is
    /// registered; or the tenant's checks refuse the provider.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<ISpeechTranscriber> GetTranscriberAsync(string? name = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the dialogue capability of a speech synthesizer for the current tenant.</summary>
    /// <param name="name">
    /// The registration name of the synthesizer, or <see langword="null"/> for the unnamed
    /// default. Dialogue has no registration of its own: the synthesizer registered under
    /// this name must implement <see cref="ISpeechDialogueSynthesizer"/>.
    /// </param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// The dialogue synthesizer, carrying the tenant's credential when it has one.
    /// </returns>
    /// <exception cref="TraconException">
    /// No synthesizer is registered under <paramref name="name"/>, or no default is
    /// registered; the synthesizer cannot voice a dialogue; or the tenant's checks refuse
    /// the provider. The message names the provider, never a key value.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// The capability is read from the synthesizer instance itself, never from a wrapper,
    /// so a tenant with its own credential and a tenant without one see the same answer.
    /// The tenant's checks then apply to <see cref="ISpeechDialogueSynthesizer.ProviderName"/>
    /// in the order <see cref="GetSynthesizerAsync"/> uses.
    /// </para>
    /// <para>
    /// A host's own synthesizer that voices no dialogue is reported, not replaced: the call
    /// fails naming that provider, and the built-in client is not called in its place.
    /// </para>
    /// </remarks>
    ValueTask<ISpeechDialogueSynthesizer> GetDialogueSynthesizerAsync(
        string? name = null,
        CancellationToken cancellationToken = default);
}

/// <summary>A registered speech provider, as the speech provider list reports it.</summary>
/// <remarks>Returned by <see cref="ISpeechProviderResolver.Providers"/> and <c>GET /api/voice/providers</c>.</remarks>
public sealed record SpeechProviderDescriptor
{
    /// <summary>
    /// Gets the registration name, or <see langword="null"/> for the unnamed default.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>Gets whether the provider synthesizes or transcribes speech.</summary>
    public required SpeechProviderKind Kind { get; init; }

    /// <summary>
    /// Gets the provider name the instance reports. A tenant's egress policy and
    /// provider bindings name this value, not the registration name.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>Gets whether the provider can call the service with a tenant's own credential.</summary>
    public required bool SupportsTenantCredentials { get; init; }
}

/// <summary>The kind of a registered speech provider.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SpeechProviderKind>))]
public enum SpeechProviderKind
{
    /// <summary>Text to speech: an <see cref="ISpeechSynthesizer"/>.</summary>
    Synthesizer = 0,

    /// <summary>Speech to text: an <see cref="ISpeechTranscriber"/>.</summary>
    Transcriber = 1,
}
