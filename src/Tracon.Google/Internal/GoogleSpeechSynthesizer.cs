using System.Runtime.CompilerServices;
using Google.GenAI;
using Google.GenAI.Types;

namespace Tracon;

/// <summary>
/// <see cref="ISpeechSynthesizer"/> and <see cref="ISpeechDialogueSynthesizer"/> over a Gemini
/// text-to-speech model.
/// </summary>
/// <remarks>
/// <para>
/// The synthesizer calls <c>Models.GenerateContentAsync</c> with the <c>AUDIO</c> response modality on
/// the Google client <c>UseGoogle</c> built, so chat, images and speech share one connection pool and
/// one egress rule (the provider name <c>google</c>). It holds no per-call state.
/// </para>
/// <para>
/// A request that carries a tenant credential is served by the client the tenant's chat requests use:
/// <see cref="GoogleModelProvider.GetTenantFactory"/> builds one per distinct credential and keeps it.
/// </para>
/// <para>
/// Gemini has no usage field in characters and the answer reports tokens, so the billed quantity is
/// the length of the text and is labelled <see cref="SpeechUsageSource.Estimated"/>. The token counts
/// stay in the answer and are not read: a price per token needs a pricing model that is not per character.
/// </para>
/// </remarks>
internal sealed class GoogleSpeechSynthesizer : ISpeechSynthesizer, ISpeechDialogueSynthesizer
{
    /// <summary>Gemini's multi-speaker request takes exactly two voices (measured: any other count is a 400).</summary>
    internal const int DialogueVoices = 2;

    private const string WavFormat = "wav";
    private const string PcmFormat = "pcm";

    // The characters a text can break a line with. A speaker label at the start of any of the lines
    // changes the voice in the text-label format.
    private static readonly char[] LineBreaks = ['\n', '\r', '\u0085', '\u2028', '\u2029'];

    private readonly string _model;
    private readonly int _maxDialogueCharacters;
    private readonly GoogleDialogueSpeakerMode _speakerMode;
    private readonly IReadOnlyList<VoiceDescriptor> _voices;
    private readonly Client _setupClient;
    private readonly Func<ModelProviderCredential, Client> _tenantClient;

    /// <summary>Builds the synthesizer.</summary>
    /// <param name="options">The settings; the values are copied.</param>
    /// <param name="setupClient">The client built with the setup credential.</param>
    /// <param name="tenantClient">Returns the client for a tenant's own credential.</param>
    public GoogleSpeechSynthesizer(
        GoogleSpeechOptions options,
        Client setupClient,
        Func<ModelProviderCredential, Client> tenantClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(setupClient);
        ArgumentNullException.ThrowIfNull(tenantClient);

        _model = options.Model!.Trim();
        MaxCharactersPerRequest = options.MaxCharactersPerRequest;
        _maxDialogueCharacters = options.MaxDialogueCharactersPerRequest;
        _speakerMode = options.DialogueSpeakerMode;
        _voices = [.. options.Voices];
        _setupClient = setupClient;
        _tenantClient = tenantClient;
    }

    /// <inheritdoc />
    public string ProviderName => GoogleProviderNames.Google;

    /// <inheritdoc />
    public bool SupportsTenantCredentials => true;

    /// <inheritdoc />
    public int MaxCharactersPerRequest { get; }

    /// <inheritdoc />
    public SpeechDialogueCapabilities DialogueCapabilities => new()
    {
        MaxVoices = DialogueVoices,
        MaxCharactersPerRequest = _maxDialogueCharacters,
        SupportsSegmentTimestamps = false,
    };

    /// <inheritdoc />
    public async ValueTask<SpeechAudio> SynthesizeAsync(SpeechRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var format = ResolveFormat(request.OutputFormat, streaming: false);
        CheckSpeech(request);

        var model = Model(request.ModelId);
        var config = SpeechConfigFor(request);
        var client = ClientFor(request.Credential);

        GenerateContentResponse response;

        try
        {
            response = await client.Models
                .GenerateContentAsync(model, request.Text, config, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFailure(exception, cancellationToken))
        {
            throw Failure(exception, hint: request.VoiceId is { Length: > 0 } ? null : NoVoiceHint);
        }

        return ToSpeechAudio(response, format, model, request.Text.Length);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The chunks are raw 16-bit little-endian PCM. Whether the model streams in many parts depends on the
    /// model (measured 2026-10-25: <c>gemini-3.8-flash-tts</c> sent about 190 parts of 1,920 to 5,760 bytes
    /// with the first one after about a second; <c>gemini-2.5-flash-preview-tts</c> sent the whole audio as
    /// one part).
    /// </remarks>
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> SynthesizeStreamingAsync(
        SpeechRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _ = ResolveFormat(request.OutputFormat, streaming: true);
        CheckSpeech(request);

        var model = Model(request.ModelId);
        var config = SpeechConfigFor(request);
        var client = ClientFor(request.Credential);
        var hint = request.VoiceId is { Length: > 0 } ? null : NoVoiceHint;

        IAsyncEnumerator<GenerateContentResponse> updates;

        try
        {
            updates = client.Models
                .GenerateContentStreamAsync(model, request.Text, config, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception exception) when (IsFailure(exception, cancellationToken))
        {
            throw Failure(exception, hint);
        }

        var produced = false;
        GenerateContentResponse? last = null;

        await using (updates.ConfigureAwait(false))
        {
            while (true)
            {
                // A yield cannot sit inside a try block that has a catch clause: the failure is
                // classified around the step, not the whole loop.
                try
                {
                    if (!await updates.MoveNextAsync().ConfigureAwait(false))
                    {
                        break;
                    }
                }
                catch (Exception exception) when (IsFailure(exception, cancellationToken))
                {
                    throw Failure(exception, hint);
                }

                last = updates.Current;

                foreach (var blob in AudioBlobs(last))
                {
                    produced = true;

                    yield return GoogleSpeechAudio.ReadSamples(blob.MimeType, blob.Data);
                }
            }
        }

        if (!produced)
        {
            throw NoAudio(last);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The voices are the static declaration in <see cref="GoogleSpeechOptions.Voices"/>: no call is made,
    /// so a tenant credential has nothing to be used for and is ignored.
    /// </remarks>
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesAsync(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_voices);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<VoiceDescriptor>> ListVoicesWithCredentialAsync(
        ModelProviderCredential? credential,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(_voices);

    /// <inheritdoc />
    public async ValueTask<SpeechDialogueAudio> SynthesizeDialogueAsync(
        SpeechDialogueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var format = ResolveFormat(request.OutputFormat, streaming: false);
        var characters = CheckDialogue(request);
        var speakers = SpeakersFor(request.Turns);
        var content = ContentFor(request.Turns, speakers);
        var model = Model(request.ModelId);
        var config = DialogueConfigFor(request, speakers);
        var client = ClientFor(request.Credential);

        GenerateContentResponse response;

        try
        {
            response = await client.Models
                .GenerateContentAsync(model, content, config, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsFailure(exception, cancellationToken))
        {
            throw Failure(exception, hint: SpeakerModeHint);
        }

        return new SpeechDialogueAudio { Audio = ToSpeechAudio(response, format, model, characters) };
    }

    private static bool IsFailure(Exception exception, CancellationToken cancellationToken)
        => exception is not TraconException
            && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);

    /// <summary>Wraps what the SDK threw in the exception the voice endpoints answer <c>502</c> for.</summary>
    /// <remarks>
    /// The text carries the status and Google's status code name, never the body: it can echo the request, and
    /// the SDK's own message is kept in the inner exception.
    /// </remarks>
    private TraconException Failure(Exception exception, string? hint)
    {
        var detail = exception is ApiException { StatusCode: > 0 } api
            ? $"HTTP {api.StatusCode}{StatusName(api.Status)}"
            : exception.GetType().Name;

        var badRequest = exception is ApiException { StatusCode: 400 };

        return new TraconException(
            $"The speech provider '{ProviderName}' could not complete the request ({detail})." +
            (badRequest && hint is not null ? " " + hint : string.Empty),
            exception);
    }

    /// <summary>Formats Google's status code name (<c>INVALID_ARGUMENT</c>); anything else is dropped.</summary>
    private static string StatusName(string? status)
        => status is { Length: > 0 and <= 40 } && status.All(static c => c is (>= 'A' and <= 'Z') or '_')
            ? $" ({status})"
            : string.Empty;

    private const string NoVoiceHint =
        "The request named no voice, and some Gemini speech models need one: pass a voice name such as 'Kore'.";

    private string SpeakerModeHint
        => $"If the model refused the speaker format, set {nameof(GoogleSpeechOptions)}.{nameof(GoogleSpeechOptions.DialogueSpeakerMode)} " +
            $"to {(_speakerMode == GoogleDialogueSpeakerMode.PartMetadata ? nameof(GoogleDialogueSpeakerMode.TextLabels) : nameof(GoogleDialogueSpeakerMode.PartMetadata))}: " +
            $"the setting is {_speakerMode} now, and the gemini-3.8 models take PartMetadata while the gemini-2.5 and gemini-3.1 preview models take TextLabels.";

    private string Model(string? requested)
        => string.IsNullOrWhiteSpace(requested) ? _model : requested.Trim();

    private Client ClientFor(ModelProviderCredential? credential)
        => credential is null ? _setupClient : _tenantClient(credential);

    /// <summary>Checks a single-voice request, before any network call.</summary>
    private void CheckSpeech(SpeechRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new TraconException("Speech text cannot be empty.");
        }

        CheckTiming(request.IncludeTimestamps);
        CheckSettings(request.ProviderSettings);
    }

    /// <summary>Checks a dialogue request, before any network call.</summary>
    /// <returns>The characters of the turns.</returns>
    private int CheckDialogue(SpeechDialogueRequest request)
    {
        var characters = DialogueCapabilities.Check(
            request,
            ProviderName,
            $"{nameof(GoogleSpeechOptions)}.{nameof(GoogleSpeechOptions.MaxDialogueCharactersPerRequest)}");

        CheckTiming(request.IncludeTimestamps);
        CheckSettings(request.ProviderSettings);

        return characters;
    }

    private void CheckTiming(bool includeTimestamps)
    {
        if (includeTimestamps)
        {
            // K-955: the request is refused, never answered without the timing it asked for.
            throw new TraconException(
                $"{nameof(SpeechRequest.IncludeTimestamps)} is not supported by the '{ProviderName}' speech synthesizer: " +
                "Gemini returns the audio only and reports no timing.");
        }
    }

    private static void CheckSettings(IReadOnlyDictionary<string, System.Text.Json.JsonElement> settings)
        => ModelProviderSettings.Validate(settings, GoogleProviderNames.SettingsPrefix, []);

    /// <summary>Reads the output format a request names.</summary>
    /// <param name="requested">The request's <c>OutputFormat</c>.</param>
    /// <param name="streaming">Whether the audio is streamed.</param>
    /// <returns><c>wav</c> or <c>pcm</c>.</returns>
    /// <remarks>
    /// A WAV header carries the length of the samples, which a stream does not know before it ends, so a
    /// stream is raw PCM and a request for <c>wav</c> on it is refused. The message does not echo the
    /// requested value.
    /// </remarks>
    private string ResolveFormat(string? requested, bool streaming)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return streaming ? PcmFormat : WavFormat;
        }

        var format = requested.Trim();

        if (format.Equals(PcmFormat, StringComparison.OrdinalIgnoreCase))
        {
            return PcmFormat;
        }

        if (format.Equals(WavFormat, StringComparison.OrdinalIgnoreCase))
        {
            return streaming
                ? throw new TraconException(
                    $"The '{ProviderName}' speech synthesizer streams raw PCM: a WAV header needs the length of the audio, " +
                    $"which a stream does not know. Ask for '{PcmFormat}', or synthesize without streaming.")
                : WavFormat;
        }

        throw new TraconException(
            $"The '{ProviderName}' speech synthesizer takes the output formats '{WavFormat}' and '{PcmFormat}'. " +
            "Gemini answers 16-bit PCM and Tracon does not convert it to another format. When the speak tool sends " +
            "the format, set Tracon:Voice:OutputFormat to 'wav'.");
    }

    private static GenerateContentConfig NewConfig(SpeechConfig speech)
        => new()
        {
            ResponseModalities = ["AUDIO"],
            SpeechConfig = speech,
        };

    private static GenerateContentConfig SpeechConfigFor(SpeechRequest request)
    {
        var speech = new SpeechConfig
        {
            LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? null : request.LanguageCode.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(request.VoiceId))
        {
            speech.VoiceConfig = VoiceConfigFor(request.VoiceId);
        }

        return NewConfig(speech);
    }

    private static GenerateContentConfig DialogueConfigFor(SpeechDialogueRequest request, IReadOnlyList<Speaker> speakers)
        => NewConfig(new SpeechConfig
        {
            LanguageCode = string.IsNullOrWhiteSpace(request.LanguageCode) ? null : request.LanguageCode.Trim(),
            MultiSpeakerVoiceConfig = new MultiSpeakerVoiceConfig
            {
                SpeakerVoiceConfigs =
                [
                    .. speakers.Select(static speaker => new SpeakerVoiceConfig
                    {
                        Speaker = speaker.Label,
                        VoiceConfig = VoiceConfigFor(speaker.VoiceId),
                    }),
                ],
            },
        });

    private static VoiceConfig VoiceConfigFor(string voiceId)
        => new() { PrebuiltVoiceConfig = new PrebuiltVoiceConfig { VoiceName = voiceId.Trim() } };

    /// <summary>Names the dialogue's voices <c>Speaker1</c> and <c>Speaker2</c>, in the order they first speak.</summary>
    /// <exception cref="TraconException">The turns use one voice: Gemini takes exactly two.</exception>
    private static List<Speaker> SpeakersFor(IReadOnlyList<SpeechDialogueTurn> turns)
    {
        var speakers = new List<Speaker>(DialogueVoices);

        foreach (var turn in turns)
        {
            if (!speakers.Exists(speaker => string.Equals(speaker.VoiceId, turn.VoiceId, StringComparison.Ordinal)))
            {
                speakers.Add(new Speaker($"Speaker{speakers.Count + 1}", turn.VoiceId));
            }
        }

        // The limit above is enforced by Check; the minimum is Gemini's own.
        if (speakers.Count != DialogueVoices)
        {
            throw new TraconException(
                $"A Gemini dialogue takes exactly {DialogueVoices} voices; this one uses {speakers.Count}. " +
                "Voice a text with one voice through SynthesizeAsync.");
        }

        return speakers;
    }

    /// <summary>Builds the request content: one part per turn, or one text of labelled lines.</summary>
    private Content ContentFor(IReadOnlyList<SpeechDialogueTurn> turns, IReadOnlyList<Speaker> speakers)
    {
        string LabelOf(SpeechDialogueTurn turn) => speakers.First(speaker => string.Equals(speaker.VoiceId, turn.VoiceId, StringComparison.Ordinal)).Label;

        if (_speakerMode == GoogleDialogueSpeakerMode.PartMetadata)
        {
            return new Content
            {
                Parts =
                [
                    .. turns.Select(turn => new Part
                    {
                        Text = turn.Text,
                        SpeechMetadata = new SpeechMetadata { Speaker = LabelOf(turn) },
                    }),
                ],
            };
        }

        var labels = speakers.Select(static speaker => speaker.Label).ToArray();
        var lines = new List<string>(turns.Count);

        for (var index = 0; index < turns.Count; index++)
        {
            // A label at the start of a line of the turn's own text would hand the rest of the turn to the
            // other voice. The turn is refused, not rewritten: Tracon does not change the text it speaks.
            if (StartsALineWithALabel(turns[index].Text, labels))
            {
                throw new TraconException(
                    $"Dialogue turn {index} has a line that starts with a speaker label ({string.Join(" or ", labels.Select(static l => $"'{l}:'"))}). " +
                    $"With {nameof(GoogleDialogueSpeakerMode.TextLabels)} such a line changes the voice. Rephrase the line.");
            }

            lines.Add($"{LabelOf(turns[index])}: {turns[index].Text}");
        }

        return new Content { Parts = [new Part { Text = string.Join('\n', lines) }] };
    }

    private static bool StartsALineWithALabel(string text, string[] labels)
    {
        var start = 0;

        while (true)
        {
            var rest = text.AsSpan(start);
            var end = rest.IndexOfAny(LineBreaks);

            if (LineStartsWithALabel((end < 0 ? rest : rest[..end]).TrimStart(), labels))
            {
                return true;
            }

            if (end < 0)
            {
                return false;
            }

            start += end + 1;
        }
    }

    private static bool LineStartsWithALabel(ReadOnlySpan<char> line, string[] labels)
    {
        foreach (var label in labels)
        {
            if (!line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var afterLabel = line[label.Length..].TrimStart();

            // U+FF1A is the full-width colon.
            if (afterLabel.Length > 0 && afterLabel[0] is ':' or '\uFF1A')
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Blob> AudioBlobs(GenerateContentResponse response)
    {
        foreach (var part in response.Candidates?.FirstOrDefault()?.Content?.Parts ?? [])
        {
            if (part.InlineData is { Data.Length: > 0 } blob)
            {
                yield return blob;
            }
        }
    }

    private SpeechAudio ToSpeechAudio(GenerateContentResponse response, string format, string requestedModel, int characters)
    {
        var blobs = AudioBlobs(response).ToList();

        if (blobs.Count == 0)
        {
            throw NoAudio(response);
        }

        // One part is what every model measured sends. Two would need joining, and joining here would
        // hide a change in the answer; the call fails and says so.
        if (blobs.Count > 1)
        {
            throw new TraconException(
                $"The '{ProviderName}' answer holds {blobs.Count} audio parts; the synthesizer reads one.");
        }

        var audio = GoogleSpeechAudio.Read(blobs[0].MimeType, blobs[0].Data);
        var asPcm = string.Equals(format, PcmFormat, StringComparison.Ordinal);

        return new SpeechAudio
        {
            Data = asPcm ? audio.Samples : audio.ToWav(),
            MediaType = asPcm ? audio.PcmMediaType : "audio/wav",
            Duration = audio.Duration,
            CharactersBilled = characters,
            UsageSource = SpeechUsageSource.Estimated,
            ModelId = string.IsNullOrWhiteSpace(response.ModelVersion) ? requestedModel : response.ModelVersion,
            ProviderRequestId = string.IsNullOrWhiteSpace(response.ResponseId) ? null : response.ResponseId,
        };
    }

    /// <summary>Explains an answer with no audio: a blocked prompt or a model that answered text.</summary>
    private TraconException NoAudio(GenerateContentResponse? response)
    {
        var candidate = response?.Candidates?.FirstOrDefault();
        var reason = candidate?.FinishReason is { } finish
            ? $" (finish reason {finish})"
            : response?.PromptFeedback?.BlockReason is { } block ? $" (prompt blocked: {block})" : string.Empty;

        return new TraconException($"The speech provider '{ProviderName}' answered no audio{reason}.");
    }

    private sealed record Speaker(string Label, string VoiceId);
}
