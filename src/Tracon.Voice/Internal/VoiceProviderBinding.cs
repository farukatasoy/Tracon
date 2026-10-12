using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

/// <summary>
/// Records which speech contracts the consumer bound before <c>UseVoice</c>.
/// </summary>
/// <remarks>
/// <para>
/// The documented way to use another provider is "register your implementation
/// FIRST; <c>TryAdd</c> preserves it". <c>UseVoice</c> looks at the service
/// collection at that moment and keeps the answer here. The validator reads it:
/// the ElevenLabs rules (provider name, API key, address) apply only while the
/// built-in client serves at least one of the two contracts.
/// </para>
/// <para>
/// The answer cannot come from resolution. Resolving the synthesizer inside the
/// validator builds the built-in client, which reads
/// <c>IOptions&lt;VoiceOptions&gt;.Value</c>, which runs the validator again.
/// </para>
/// <para>
/// A registration added AFTER <c>UseVoice</c> is not seen. It wins resolution
/// but the built-in rules still apply, so startup fails with a message that says
/// to register first — a clear failure instead of a silent one.
/// </para>
/// </remarks>
internal sealed class VoiceProviderBinding
{
    /// <summary>Creates a binding record.</summary>
    /// <param name="customSynthesizer">The consumer bound <see cref="ISpeechSynthesizer"/>.</param>
    /// <param name="customTranscriber">The consumer bound <see cref="ISpeechTranscriber"/>.</param>
    public VoiceProviderBinding(bool customSynthesizer, bool customTranscriber)
    {
        CustomSynthesizer = customSynthesizer;
        CustomTranscriber = customTranscriber;
    }

    /// <summary>The consumer registered an <see cref="ISpeechSynthesizer"/> before <c>UseVoice</c>.</summary>
    public bool CustomSynthesizer { get; }

    /// <summary>The consumer registered an <see cref="ISpeechTranscriber"/> before <c>UseVoice</c>.</summary>
    public bool CustomTranscriber { get; }

    /// <summary>The built-in ElevenLabs client serves at least one contract.</summary>
    public bool UsesBuiltInClient => !CustomSynthesizer || !CustomTranscriber;

    /// <summary>Reads the service collection before <c>UseVoice</c> adds its defaults.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The binding record.</returns>
    public static VoiceProviderBinding Capture(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var customSynthesizer = false;
        var customTranscriber = false;

        // A keyed registration does not stop TryAdd; it is not the bound provider.
        foreach (var descriptor in services)
        {
            if (descriptor.IsKeyedService)
            {
                continue;
            }

            customSynthesizer |= descriptor.ServiceType == typeof(ISpeechSynthesizer);
            customTranscriber |= descriptor.ServiceType == typeof(ISpeechTranscriber);
        }

        return new VoiceProviderBinding(customSynthesizer, customTranscriber);
    }
}
