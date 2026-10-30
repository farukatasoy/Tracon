// MEAI001: Microsoft.Extensions.AI marks its speech clients "experimental" (10.11.0).
// This file is the ONLY place in Tracon.Core that names them, and no public member does
// (YAYIN-HAZIRLIK criterion 5): when MEAI changes the surface, the change lands here.
#pragma warning disable MEAI001

using System.Buffers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tracon;

/// <summary>
/// Serves a Tracon speech synthesizer from a <c>Microsoft.Extensions.AI</c> text-to-speech client.
/// </summary>
/// <remarks>
/// <para>
/// The adapter holds no tenant data and reads no credential. <see cref="SpeechRequest.Credential"/>
/// is never copied into the options: <c>LoggingTextToSpeechClient</c> writes the options to the
/// log at Trace level, so a value placed there can leave the process. The default
/// <see cref="ISpeechSynthesizer.SupportsTenantCredentials"/> (<see langword="false"/>) makes the
/// resolver refuse a tenant that has its own key before this class is reached.
/// </para>
/// <para>
/// The adapter does not implement <see cref="ISpeechDialogueSynthesizer"/>, so a dialogue request
/// for a bridged name is refused by the resolver.
/// </para>
/// </remarks>
internal sealed class TextToSpeechClientSynthesizer : ISpeechSynthesizer
{
    private readonly ITextToSpeechClient _client;
    private readonly IReadOnlyList<VoiceDescriptor> _voices;
    private readonly string? _defaultModelId;

    private TextToSpeechClientSynthesizer(
        ITextToSpeechClient client,
        string providerName,
        int maxCharactersPerRequest,
        IReadOnlyList<VoiceDescriptor> voices,
        string? defaultModelId)
    {
        _client = client;
        ProviderName = providerName;
        MaxCharactersPerRequest = maxCharactersPerRequest;
        _voices = voices;
        _defaultModelId = defaultModelId;
    }

    /// <inheritdoc />
    public string ProviderName { get; }

    /// <inheritdoc />
    public int MaxCharactersPerRequest { get; }

    /// <summary>Takes the client from the container and checks what the binding must say.</summary>
    /// <param name="services">The container that holds the client.</param>
    /// <param name="registrationName">The name the synthesizer is registered under.</param>
    /// <param name="binding">The binding.</param>
    /// <returns>The adapter.</returns>
    /// <exception cref="TraconException">The client is not registered, or no provider name is known.</exception>
    public static TextToSpeechClientSynthesizer Create(
        IServiceProvider services,
        string registrationName,
        TextToSpeechClientBinding binding)
    {
        var client = (binding.ServiceKey is null
                ? services.GetService<ITextToSpeechClient>()
                : services.GetKeyedService<ITextToSpeechClient>(binding.ServiceKey))
            ?? throw new TraconException(
                $"The speech synthesizer '{registrationName}' is bound to a text-to-speech client that is not " +
                $"registered ({SpeechClientNames.DescribeKey(binding.ServiceKey)}). Register the client with " +
                "AddTextToSpeechClient or AddKeyedTextToSpeechClient, or with the extension of its package.");

        var metadata = client.GetService<TextToSpeechClientMetadata>();
        var providerName = SpeechClientNames.ResolveProviderName(
            "synthesizer",
            registrationName,
            binding.ProviderName,
            metadata?.ProviderName);

        // The call is a span of the run that made it. The adapter is a singleton per registered name, so
        // the ring is built once; it is not disposed with the adapter because that would dispose the consumer's
        // client. EnableSensitiveData stays the library's default (the consumer's switch): the trace store filter
        // decides what is kept.
        var ring = new TextToSpeechClientBuilder(client)
            .UseOpenTelemetry(services.GetService<ILoggerFactory>(), TraconDiagnostics.ActivitySourceName)
            .Build();

        return new TextToSpeechClientSynthesizer(
            ring,
            providerName,
            binding.MaxCharactersPerRequest,
            [.. binding.Voices],
            metadata?.DefaultModelId);
    }

    /// <inheritdoc />
    public async ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RefuseAlignment(request);

        TextToSpeechResponse response;

        try
        {
            response = await _client
                .GetAudioAsync(request.Text, ToOptions(request), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (SpeechClientFailure.IsProviderFailure(exception, cancellationToken))
        {
            throw SpeechClientFailure.Wrap(ProviderName, exception);
        }

        var (data, mediaType) = Join(response.Contents);

        return new SpeechAudio
        {
            Data = data,
            MediaType = mediaType,
            // MEAI reports no billed quantity. The text length is the quantity a
            // per-character price is quoted in, and it is labelled as an estimate.
            CharactersBilled = request.Text.Length,
            UsageSource = SpeechUsageSource.Estimated,
            ModelId = FirstText(response.ModelId, request.ModelId, _defaultModelId),
            ProviderRequestId = FirstText(response.ResponseId),
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RefuseAlignment(request);

        IAsyncEnumerator<TextToSpeechResponseUpdate> updates;

        try
        {
            updates = _client
                .GetStreamingAudioAsync(request.Text, ToOptions(request), cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception exception) when (SpeechClientFailure.IsProviderFailure(exception, cancellationToken))
        {
            // A client that is not an async iterator can throw before the first step.
            throw SpeechClientFailure.Wrap(ProviderName, exception);
        }

        await using (updates.ConfigureAwait(false))
        {
            while (true)
            {
                // A yield cannot sit inside a try block that has a catch clause: the
                // provider's failure is classified around the step, not the whole loop.
                try
                {
                    if (!await updates.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield break;
                    }
                }
                catch (Exception exception) when (SpeechClientFailure.IsProviderFailure(exception, cancellationToken))
                {
                    throw SpeechClientFailure.Wrap(ProviderName, exception);
                }

                foreach (var content in updates.Current.Contents)
                {
                    if (content is DataContent { Data.IsEmpty: false } audio)
                    {
                        yield return audio.Data;
                    }
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_voices);

    private static void RefuseAlignment(SpeechRequest request)
    {
        // A timing request is refused, never answered without timing. MEAI has
        // no channel for alignment on either path.
        if (request.IncludeTimestamps)
        {
            throw new TraconException(
                $"{nameof(SpeechRequest.IncludeTimestamps)} is not supported by a speech synthesizer bound to a " +
                "Microsoft.Extensions.AI client: the client returns audio only and has no channel for alignment data.");
        }
    }

    private static TextToSpeechOptions ToOptions(SpeechRequest request)
    {
        var options = new TextToSpeechOptions
        {
            VoiceId = FirstText(request.VoiceId),
            ModelId = FirstText(request.ModelId),
            AudioFormat = FirstText(request.OutputFormat),
            Language = FirstText(request.LanguageCode),
        };

        if (request.ProviderSettings.Count > 0)
        {
            // The settings reach the client unchanged. Tracon cannot tell which keys the
            // client reads, so it cannot refuse an unknown one the way a provider that
            // owns its settings does.
            options.AdditionalProperties = [];

            foreach (var (key, value) in request.ProviderSettings)
            {
                options.AdditionalProperties[key] = value;
            }
        }

        return options;
    }

    private (ReadOnlyMemory<byte> Data, string MediaType) Join(IList<AIContent> contents)
    {
        ReadOnlyMemory<byte> first = default;
        ArrayBufferWriter<byte>? joined = null;
        string? mediaType = null;

        foreach (var content in contents)
        {
            if (content is not DataContent { Data.IsEmpty: false } audio)
            {
                continue;
            }

            if (mediaType is null)
            {
                mediaType = audio.MediaType;
                first = audio.Data;

                continue;
            }

            if (joined is null)
            {
                joined = new ArrayBufferWriter<byte>(first.Length + audio.Data.Length);
                joined.Write(first.Span);
            }

            joined.Write(audio.Data.Span);
        }

        return mediaType is null
            ? throw new TraconException($"The speech provider '{ProviderName}' returned no audio.")
            : (joined is null ? first : joined.WrittenMemory, mediaType);
    }

    private static string? FirstText(params ReadOnlySpan<string?> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>
/// Serves a Tracon speech transcriber from a <c>Microsoft.Extensions.AI</c> speech-to-text client.
/// </summary>
/// <remarks>
/// The client's contract takes a stream and no media type, so <c>mediaType</c> is not passed on.
/// What the client does with the stream is its own rule.
/// </remarks>
internal sealed class SpeechToTextClientTranscriber : ISpeechTranscriber
{
    private readonly ISpeechToTextClient _client;
    private readonly string? _defaultModelId;

    private SpeechToTextClientTranscriber(ISpeechToTextClient client, string providerName, string? defaultModelId)
    {
        _client = client;
        ProviderName = providerName;
        _defaultModelId = defaultModelId;
    }

    /// <inheritdoc />
    public string ProviderName { get; }

    /// <summary>Takes the client from the container and checks what the binding must say.</summary>
    /// <param name="services">The container that holds the client.</param>
    /// <param name="registrationName">The name the transcriber is registered under.</param>
    /// <param name="binding">The binding.</param>
    /// <returns>The adapter.</returns>
    /// <exception cref="TraconException">The client is not registered, or no provider name is known.</exception>
    public static SpeechToTextClientTranscriber Create(
        IServiceProvider services,
        string registrationName,
        SpeechToTextClientBinding binding)
    {
        var client = (binding.ServiceKey is null
                ? services.GetService<ISpeechToTextClient>()
                : services.GetKeyedService<ISpeechToTextClient>(binding.ServiceKey))
            ?? throw new TraconException(
                $"The speech transcriber '{registrationName}' is bound to a speech-to-text client that is not " +
                $"registered ({SpeechClientNames.DescribeKey(binding.ServiceKey)}). Register the client with " +
                "AddSpeechToTextClient or AddKeyedSpeechToTextClient, or with the extension of its package.");

        var metadata = client.GetService<SpeechToTextClientMetadata>();
        var providerName = SpeechClientNames.ResolveProviderName(
            "transcriber",
            registrationName,
            binding.ProviderName,
            metadata?.ProviderName);

        // See TextToSpeechClientSynthesizer.Create.
        var ring = new SpeechToTextClientBuilder(client)
            .UseOpenTelemetry(services.GetService<ILoggerFactory>(), TraconDiagnostics.ActivitySourceName)
            .Build();

        return new SpeechToTextClientTranscriber(ring, providerName, metadata?.DefaultModelId);
    }

    /// <inheritdoc />
    public async ValueTask<SpeechTranscript> TranscribeAsync(
        Stream audio,
        string mediaType,
        SpeechTranscriptionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var clientOptions = new SpeechToTextOptions
        {
            ModelId = NullIfBlank(options?.ModelId),
            SpeechLanguage = NullIfBlank(options?.LanguageCode),
        };

        SpeechToTextResponse response;

        try
        {
            response = await _client.GetTextAsync(audio, clientOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (SpeechClientFailure.IsProviderFailure(exception, cancellationToken))
        {
            throw SpeechClientFailure.Wrap(ProviderName, exception);
        }

        return new SpeechTranscript
        {
            Text = response.Text,

            // The cost of a transcription comes from this. A client that reports one end
            // of the span only, or an end before the start, has not measured a duration.
            AudioDuration = response is { StartTime: { } start, EndTime: { } end } && end > start ? end - start : null,
            ModelId = NullIfBlank(response.ModelId) ?? clientOptions.ModelId ?? NullIfBlank(_defaultModelId),
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>The rules for the provider name of a bridged client.</summary>
internal static class SpeechClientNames
{
    /// <summary>Picks the provider name: the binding's, else the client metadata's.</summary>
    /// <param name="noun">The <c>synthesizer</c> or <c>transcriber</c>, for the message.</param>
    /// <param name="registrationName">The registration name, for the message.</param>
    /// <param name="declared">The name on the binding.</param>
    /// <param name="fromMetadata">The name the client reports.</param>
    /// <returns>The provider name.</returns>
    /// <exception cref="TraconException">Neither source has a name.</exception>
    public static string ResolveProviderName(string noun, string registrationName, string? declared, string? fromMetadata)
    {
        if (!string.IsNullOrWhiteSpace(declared))
        {
            return declared.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fromMetadata))
        {
            return fromMetadata.Trim();
        }

        // The tenant's egress policy is applied to this name. A provider
        // without one would pass every policy, so the host does not start.
        throw new TraconException(
            $"The speech {noun} '{registrationName}' has no provider name. The client reports none, so set " +
            "ProviderName on the binding. A tenant's egress policy and provider bindings are checked against " +
            "the provider name, and a provider with no name cannot be checked.");
    }

    /// <summary>Describes a service key for a message.</summary>
    /// <param name="serviceKey">The key, or <see langword="null"/> for the unkeyed registration.</param>
    /// <returns>The text.</returns>
    public static string DescribeKey(object? serviceKey)
        => serviceKey is null ? "no service key" : $"service key '{serviceKey}'";
}

/// <summary>Classifies what a bridged client throws.</summary>
internal static class SpeechClientFailure
{
    /// <summary>Says whether an exception is a failure of the provider rather than a stop the caller asked for.</summary>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns><see langword="true"/> when the call failed.</returns>
    public static bool IsProviderFailure(Exception exception, CancellationToken cancellationToken)
        => exception is not TraconException && OperationCancellation.IsFailure(exception, cancellationToken);

    /// <summary>Wraps a failure in the exception type the voice endpoints answer <c>502</c> for.</summary>
    /// <param name="providerName">The provider name.</param>
    /// <param name="exception">What the client threw.</param>
    /// <returns>The exception to throw.</returns>
    /// <remarks>
    /// The text carries the type and, for HTTP, the status code. The client's own message can
    /// echo the request or a fragment of the response, so it stays in the inner exception and
    /// never reaches an endpoint response.
    /// </remarks>
    public static TraconException Wrap(string providerName, Exception exception)
    {
        var status = exception is HttpRequestException { StatusCode: { } code } ? $", HTTP {(int)code}" : string.Empty;

        return new TraconException(
            $"The speech provider '{providerName}' could not complete the request ({exception.GetType().Name}{status}).",
            exception);
    }
}
