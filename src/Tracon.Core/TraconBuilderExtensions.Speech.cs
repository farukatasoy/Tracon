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

        RegisterSynthesizer(builder.Services, name, factory);

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
    /// <see cref="AddSpeechSynthesizer(ITraconBuilder, string, Func{IServiceProvider, ISpeechSynthesizer})"/> apply: the tenant's policy reads
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

        RegisterTranscriber(builder.Services, name, factory);

        return builder;
    }

    /// <summary>Registers a named speech synthesizer that serves a Microsoft.Extensions.AI text-to-speech client.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The registration name: 1 to 64 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>,
    /// <c>_</c> or <c>-</c>, compared in lower case.
    /// </param>
    /// <param name="binding">Which client in the container serves the name, and what the client cannot say about itself.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not a valid speech provider name, or
    /// <see cref="TextToSpeechClientBinding.MaxCharactersPerRequest"/> is not greater than zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">A synthesizer with the same name is already registered.</exception>
    /// <remarks>
    /// <para>
    /// The result is an ordinary named synthesizer: the rules of
    /// <see cref="AddSpeechSynthesizer(ITraconBuilder, string, Func{IServiceProvider, ISpeechSynthesizer})"/>
    /// apply. Every call passes the tenant's egress policy against the provider name, and a
    /// tenant that has its own key for that provider is refused: a bridged client was built
    /// with the setup credential and cannot use another.
    /// </para>
    /// <para>
    /// The host does not start when the client is not registered or has no provider name.
    /// Billing is an estimate: the client reports no billed characters, so Tracon records the
    /// length of the text and labels it <see cref="SpeechUsageSource.Estimated"/>. A request
    /// for character timing (<see cref="SpeechRequest.IncludeTimestamps"/>) is refused, and the
    /// synthesizer cannot voice a dialogue.
    /// </para>
    /// <para>
    /// Do not put a <c>secret</c> in <see cref="SpeechRequest.ProviderSettings"/>: the values reach
    /// the client unchanged, and a client wrapped in <c>LoggingTextToSpeechClient</c> logs its
    /// options at Trace level.
    /// </para>
    /// <example>
    /// <code>
    /// #pragma warning disable MEAI001 // Microsoft.Extensions.AI marks its speech clients experimental
    /// builder.Services.AddKeyedTextToSpeechClient("studio", provider =&gt; new StudioTextToSpeechClient(
    ///     provider.GetRequiredService&lt;IConfiguration&gt;()["Studio:ApiKey"]));
    ///
    /// builder.AddTracon()
    ///        .AddSpeechSynthesizer("studio", new TextToSpeechClientBinding
    ///        {
    ///            ServiceKey = "studio",
    ///            ProviderName = "studio",
    ///            MaxCharactersPerRequest = 4000,
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddSpeechSynthesizer(
        this ITraconBuilder builder,
        string name,
        TextToSpeechClientBinding binding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(binding);

        if (binding.MaxCharactersPerRequest <= 0)
        {
            throw new ArgumentException(
                $"{nameof(TextToSpeechClientBinding)}.{nameof(TextToSpeechClientBinding.MaxCharactersPerRequest)} " +
                "must be greater than zero: the client reports no limit, and Tracon refuses text above it.",
                nameof(binding));
        }

        var registration = RegisterSynthesizer(
            builder.Services,
            name,
            provider => TextToSpeechClientSynthesizer.Create(provider, name, binding));

        SpeechClientBridgeValidation.Register(builder.Services, registration);

        return builder;
    }

    /// <summary>Registers a named speech transcriber that serves a Microsoft.Extensions.AI speech-to-text client.</summary>
    /// <param name="builder">The Tracon configuration chain.</param>
    /// <param name="name">
    /// The registration name: 1 to 64 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>,
    /// <c>_</c> or <c>-</c>, compared in lower case. Transcriber names are separate
    /// from synthesizer names.
    /// </param>
    /// <param name="binding">Which client in the container serves the name, and what the client cannot say about itself.</param>
    /// <returns>The chain, for further configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="binding"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not a valid speech provider name.</exception>
    /// <exception cref="InvalidOperationException">A transcriber with the same name is already registered.</exception>
    /// <remarks>
    /// <para>
    /// The rules of <see cref="AddSpeechSynthesizer(ITraconBuilder, string, TextToSpeechClientBinding)"/>
    /// apply: the tenant's egress policy and provider bindings read the provider name, and the
    /// host does not start when the client is missing or has no name.
    /// </para>
    /// <para>
    /// The client's contract takes the audio stream and no media type, so
    /// <c>mediaType</c> is not passed on. The transcript has no duration unless the client
    /// reports a start and an end time, and a transcription without a duration has no cost.
    /// </para>
    /// <example>
    /// <code>
    /// #pragma warning disable MEAI001 // Microsoft.Extensions.AI marks its speech clients experimental
    /// builder.Services.AddKeyedSpeechToTextClient("studio", provider =&gt; new StudioSpeechToTextClient(
    ///     provider.GetRequiredService&lt;IConfiguration&gt;()["Studio:ApiKey"]));
    ///
    /// builder.AddTracon()
    ///        .AddSpeechTranscriber("studio", new SpeechToTextClientBinding
    ///        {
    ///            ServiceKey = "studio",
    ///            ProviderName = "studio",
    ///        });
    /// </code>
    /// </example>
    /// </remarks>
    public static ITraconBuilder AddSpeechTranscriber(
        this ITraconBuilder builder,
        string name,
        SpeechToTextClientBinding binding)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(binding);

        var registration = RegisterTranscriber(
            builder.Services,
            name,
            provider => SpeechToTextClientTranscriber.Create(provider, name, binding));

        SpeechClientBridgeValidation.Register(builder.Services, registration);

        return builder;
    }

    private static SpeechProviderRegistration RegisterSynthesizer(
        IServiceCollection services,
        string name,
        Func<IServiceProvider, ISpeechSynthesizer> factory)
    {
        var registration = SpeechProviderRegistration.Create(services, SpeechProviderKind.Synthesizer, name);

        services.AddKeyedSingleton<ISpeechSynthesizer>(
            registration,
            (provider, _) => factory(provider)
                ?? throw new InvalidOperationException(
                    $"The factory of the speech synthesizer '{registration.Name}' returned null."));
        services.AddSingleton(registration);

        return registration;
    }

    private static SpeechProviderRegistration RegisterTranscriber(
        IServiceCollection services,
        string name,
        Func<IServiceProvider, ISpeechTranscriber> factory)
    {
        var registration = SpeechProviderRegistration.Create(services, SpeechProviderKind.Transcriber, name);

        services.AddKeyedSingleton<ISpeechTranscriber>(
            registration,
            (provider, _) => factory(provider)
                ?? throw new InvalidOperationException(
                    $"The factory of the speech transcriber '{registration.Name}' returned null."));
        services.AddSingleton(registration);

        return registration;
    }
}
