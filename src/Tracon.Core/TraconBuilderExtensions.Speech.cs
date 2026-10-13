using Microsoft.Extensions.DependencyInjection;

namespace Tracon;

public static partial class TraconBuilderExtensions
{
    /// <summary>Registers a named speech synthesizer as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The registration name: 1 to 64 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>,
    /// <c>_</c> or <c>-</c>, compared in lower case.
    /// </param>
    /// <param name="factory">Creates the synthesizer once, the first time the name is resolved.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid speech provider name.</exception>
    /// <exception cref="InvalidOperationException">A synthesizer with the same name is already registered.</exception>
    /// <remarks>
    /// <para>
    /// A caller selects the synthesizer by name through
    /// <see cref="ISpeechProviderResolver.GetSynthesizerAsync"/>: a workflow function
    /// node, or <c>POST /api/voice/speak</c> with <c>provider</c>. The unnamed default
    /// (<c>UseVoice()</c>, or your own <see cref="ISpeechSynthesizer"/> registration)
    /// is not affected, and the call can come before or after <c>UseVoice()</c>.
    /// </para>
    /// <para>
    /// The tenant's egress policy applies to the synthesizer's
    /// <see cref="ISpeechSynthesizer.ProviderName"/>, not to <paramref name="name"/>:
    /// two names for one service are one provider to the policy. A factory that
    /// returns the same instance for two names shares that instance, and its
    /// concurrency limit, between them.
    /// </para>
    /// <para>
    /// A synthesizer that can call the service with a tenant's own key sets
    /// <see cref="ISpeechSynthesizer.SupportsTenantCredentials"/> and reads
    /// <see cref="SpeechRequest.Credential"/>. One that cannot is refused for a tenant
    /// that has its own binding for its provider name.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddSpeechSynthesizer("studio", provider => new StudioSpeechSynthesizer(
    ///            provider.GetRequiredService&lt;IConfiguration&gt;()["Studio:ApiKey"]));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddSpeechSynthesizer(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, ISpeechSynthesizer> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        var registration = SpeechProviderRegistration.Create(builder.Services, SpeechProviderKind.Synthesizer, name);

        builder.Services.AddKeyedSingleton<ISpeechSynthesizer>(
            registration,
            (provider, _) => factory(provider)
                ?? throw new InvalidOperationException(
                    $"The factory of the speech synthesizer '{registration.Name}' returned null."));
        builder.Services.AddSingleton(registration);

        return builder;
    }

    /// <summary>Registers a named speech transcriber as a singleton.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The registration name: 1 to 64 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>,
    /// <c>_</c> or <c>-</c>, compared in lower case. Transcriber names are separate
    /// from synthesizer names.
    /// </param>
    /// <param name="factory">Creates the transcriber once, the first time the name is resolved.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="factory"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid speech provider name.</exception>
    /// <exception cref="InvalidOperationException">A transcriber with the same name is already registered.</exception>
    /// <remarks>
    /// <para>
    /// A caller selects the transcriber by name through
    /// <see cref="ISpeechProviderResolver.GetTranscriberAsync"/>. The rules of
    /// <see cref="AddSpeechSynthesizer"/> apply: the tenant's policy reads
    /// <see cref="ISpeechTranscriber.ProviderName"/>, and a tenant credential reaches
    /// the transcriber only in <see cref="SpeechTranscriptionOptions.Credential"/>.
    /// </para>
    /// <example>
    /// <code>
    /// builder.AddTracon()
    ///        .AddSpeechTranscriber("on-prem", provider => new OnPremiseTranscriber(
    ///            new Uri("http://gpu-01:9000/")));
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddSpeechTranscriber(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, ISpeechTranscriber> factory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);

        var registration = SpeechProviderRegistration.Create(builder.Services, SpeechProviderKind.Transcriber, name);

        builder.Services.AddKeyedSingleton<ISpeechTranscriber>(
            registration,
            (provider, _) => factory(provider)
                ?? throw new InvalidOperationException(
                    $"The factory of the speech transcriber '{registration.Name}' returned null."));
        builder.Services.AddSingleton(registration);

        return builder;
    }
}
