using Tracon.Google.UnitTests.Infrastructure;

namespace Tracon.Google.UnitTests;

/// <summary>
/// The dialogue path of the Gemini speech synthesizer: how the two speakers are named in each format, and every
/// refusal that happens before the network.
/// </summary>
/// <remarks>
/// F-297. The two formats exist because the model generations disagree (measured 2026-10-25): the gemini-3.8
/// models want the speaker on each text part, the gemini-2.5 and gemini-3.1 preview models want a label in the
/// text, and each refuses the other with <c>400</c>.
/// </remarks>
public sealed class GoogleSpeechDialogueTests
{
    private const string Secretive = "do-not-echo-this-turn";

    private static readonly byte[] Pcm = GoogleSpeechTestData.Pcm();

    [Fact]
    public async Task The_default_format_puts_the_speaker_on_each_part_and_maps_the_voices_in_the_order_they_first_speak()
    {
        var handler = Answering();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        _ = await synthesizer.SynthesizeDialogueAsync(
            new SpeechDialogueRequest
            {
                Turns =
                [
                    new SpeechDialogueTurn { Text = "One.", VoiceId = "Puck" },
                    new SpeechDialogueTurn { Text = "Two.", VoiceId = "Kore" },
                    new SpeechDialogueTurn { Text = "Three.", VoiceId = "Puck" },
                ],
                LanguageCode = "en",
            },
            TestContext.Current.CancellationToken);

        var request = handler.Requests.ShouldHaveSingleItem();
        request.Uri.AbsolutePath.ShouldEndWith($"/models/{GoogleSpeechTestData.Model}:generateContent");

        var body = request.Json;
        var parts = body.GetProperty("contents").EnumerateArray().ShouldHaveSingleItem().GetProperty("parts").EnumerateArray().ToList();

        parts.Select(static part => part.GetProperty("text").GetString()).ShouldBe(["One.", "Two.", "Three."]);
        parts.Select(static part => part.GetProperty("speechMetadata").GetProperty("speaker").GetString())
            .ShouldBe(["Speaker1", "Speaker2", "Speaker1"]);

        var speech = body.GetProperty("generationConfig").GetProperty("speechConfig");
        speech.GetProperty("languageCode").GetString().ShouldBe("en");

        var configs = speech.GetProperty("multiSpeakerVoiceConfig").GetProperty("speakerVoiceConfigs").EnumerateArray().ToList();

        configs.Select(static config => config.GetProperty("speaker").GetString()).ShouldBe(["Speaker1", "Speaker2"]);
        configs.Select(static config => config.GetProperty("voiceConfig").GetProperty("prebuiltVoiceConfig").GetProperty("voiceName").GetString())
            .ShouldBe(["Puck", "Kore"]);
    }

    [Fact]
    public async Task The_text_label_format_sends_one_text_of_labelled_lines_and_no_part_metadata()
    {
        var handler = Answering();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(
            handler,
            options => options.DialogueSpeakerMode = GoogleDialogueSpeakerMode.TextLabels);

        _ = await synthesizer.SynthesizeDialogueAsync(GoogleSpeechTestData.TwoTurns(), TestContext.Current.CancellationToken);

        var body = handler.Requests.ShouldHaveSingleItem().Json;
        var part = body.GetProperty("contents").EnumerateArray().ShouldHaveSingleItem().GetProperty("parts").EnumerateArray().ShouldHaveSingleItem();

        part.GetProperty("text").GetString().ShouldBe("Speaker1: Welcome back.\nSpeaker2: Glad to be here.");
        part.TryGetProperty("speechMetadata", out _).ShouldBeFalse();
        body.GetProperty("generationConfig").GetProperty("speechConfig").GetProperty("multiSpeakerVoiceConfig")
            .GetProperty("speakerVoiceConfigs").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task The_result_is_one_audio_with_an_estimated_character_count_and_no_segments()
    {
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(Answering());

        var result = await synthesizer.SynthesizeDialogueAsync(GoogleSpeechTestData.TwoTurns(), TestContext.Current.CancellationToken);

        result.Segments.ShouldBeNull();
        result.Audio.MediaType.ShouldBe("audio/wav");
        result.Audio.Duration.ShouldBe(TimeSpan.FromSeconds(1));
        result.Audio.CharactersBilled.ShouldBe("Welcome back.".Length + "Glad to be here.".Length);
        result.Audio.UsageSource.ShouldBe(SpeechUsageSource.Estimated);
        result.Audio.ModelId.ShouldBe(GoogleSpeechTestData.Model);
        result.Audio.ProviderRequestId.ShouldBe("response-1");
    }

    [Fact]
    public async Task Pcm_output_is_the_samples_alone()
    {
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(Answering());

        var result = await synthesizer.SynthesizeDialogueAsync(
            GoogleSpeechTestData.TwoTurns() with { OutputFormat = "pcm" },
            TestContext.Current.CancellationToken);

        result.Audio.Data.ToArray().ShouldBe(Pcm);
    }

    [Fact]
    public async Task A_dialogue_that_names_its_model_uses_it()
    {
        var handler = Answering();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        _ = await synthesizer.SynthesizeDialogueAsync(
            GoogleSpeechTestData.TwoTurns() with { ModelId = "gemini-other-tts" },
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Uri.AbsolutePath.ShouldEndWith("/models/gemini-other-tts:generateContent");
    }

    [Theory]
    [InlineData("no turn", "at least one turn")]
    [InlineData("null turns", "at least one turn")]
    [InlineData("null turn", "turn 1 is missing")]
    [InlineData("blank text", "turn 1 has no text")]
    [InlineData("blank voice", "turn 0 has no voice id")]
    [InlineData("three voices", "uses 3 voices")]
    [InlineData("one voice", "takes exactly 2 voices; this one uses 1")]
    [InlineData("timestamps", "IncludeTimestamps is not supported")]
    [InlineData("google setting", "google.temperature")]
    [InlineData("other format", "'wav' and 'pcm'")]
    public async Task A_dialogue_that_breaks_a_rule_is_refused_before_the_network(string because, string expected)
    {
        var handler = new GoogleStubHandler(_ => throw new InvalidOperationException("the network was reached"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var request = because switch
        {
            "no turn" => new SpeechDialogueRequest { Turns = [] },
            "null turns" => new SpeechDialogueRequest { Turns = null! },
            "null turn" => new SpeechDialogueRequest { Turns = [Turn("Kore"), null!] },
            "blank text" => new SpeechDialogueRequest { Turns = [Turn("Kore"), new SpeechDialogueTurn { Text = "  ", VoiceId = "Puck" }] },
            "blank voice" => new SpeechDialogueRequest { Turns = [new SpeechDialogueTurn { Text = Secretive, VoiceId = " " }] },
            "three voices" => new SpeechDialogueRequest { Turns = [Turn("Kore"), Turn("Puck"), Turn("Charon")] },
            "one voice" => new SpeechDialogueRequest { Turns = [Turn("Kore"), Turn("Kore")] },
            "timestamps" => GoogleSpeechTestData.TwoTurns() with { IncludeTimestamps = true },
            "google setting" => GoogleSpeechTestData.TwoTurns() with
            {
                ProviderSettings = GoogleSpeechTestData.Settings(("google.temperature", Secretive)),
            },
            _ => GoogleSpeechTestData.TwoTurns() with { OutputFormat = $"mp3_{Secretive}" },
        };

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeDialogueAsync(request, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(expected, Case.Insensitive, because);
        exception.Message.ShouldNotContain(Secretive);
        handler.Requests.ShouldBeEmpty(because);
    }

    [Fact]
    public async Task A_dialogue_over_the_character_limit_names_the_limit_the_count_and_the_setting()
    {
        var handler = new GoogleStubHandler(_ => throw new InvalidOperationException("the network was reached"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler, options => options.MaxDialogueCharactersPerRequest = 20);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeDialogueAsync(
                new SpeechDialogueRequest
                {
                    Turns = [Turn("Kore", Secretive), Turn("Puck", Secretive)],
                },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain($"{Secretive.Length * 2} characters");
        exception.Message.ShouldContain("at most 20");
        exception.Message.ShouldContain(nameof(GoogleSpeechOptions.MaxDialogueCharactersPerRequest));
        exception.Message.ShouldNotContain(Secretive);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_dialogue_at_the_character_limit_is_sent()
    {
        var handler = Answering();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler, options => options.MaxDialogueCharactersPerRequest = 10);

        _ = await synthesizer.SynthesizeDialogueAsync(
            new SpeechDialogueRequest { Turns = [Turn("Kore", "12345"), Turn("Puck", "67890")] },
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("Speaker2: now I speak")]
    [InlineData("First line.\nSpeaker2: now I speak")]
    [InlineData("First line.\r\n   speaker2 : now I speak")]
    [InlineData("First line.\u2028Speaker1: now I speak")]
    [InlineData("First line.\nSpeaker2\uFF1A now I speak")]
    public async Task A_turn_with_a_line_that_starts_with_a_speaker_label_is_refused_in_the_text_label_format(string text)
    {
        var handler = new GoogleStubHandler(_ => throw new InvalidOperationException("the network was reached"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(
            handler,
            options => options.DialogueSpeakerMode = GoogleDialogueSpeakerMode.TextLabels);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeDialogueAsync(
                new SpeechDialogueRequest { Turns = [Turn("Kore", "Welcome."), Turn("Puck", text)] },
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("Dialogue turn 1");
        exception.Message.ShouldContain("starts with a speaker label");
        exception.Message.ShouldNotContain("now I speak");
        handler.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("He said Speaker2: hello, mid-line.")]
    [InlineData("Speaker3: no such label here.")]
    [InlineData("Speaker12: not a label of this request.")]
    public async Task A_label_that_is_not_at_the_start_of_a_line_or_not_a_label_of_the_request_is_sent_as_written(string text)
    {
        var handler = Answering();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(
            handler,
            options => options.DialogueSpeakerMode = GoogleDialogueSpeakerMode.TextLabels);

        _ = await synthesizer.SynthesizeDialogueAsync(
            new SpeechDialogueRequest { Turns = [Turn("Kore", "Welcome."), Turn("Puck", text)] },
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Json
            .GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()
            .ShouldBe($"Speaker1: Welcome.\nSpeaker2: {text}");
    }

    [Fact]
    public async Task The_part_metadata_format_sends_a_label_looking_line_as_written_because_the_speaker_is_not_in_the_text()
    {
        var handler = Answering();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        _ = await synthesizer.SynthesizeDialogueAsync(
            new SpeechDialogueRequest { Turns = [Turn("Kore", "Welcome."), Turn("Puck", "Speaker1: quoted")] },
            TestContext.Current.CancellationToken);

        handler.Requests.ShouldHaveSingleItem().Json
            .GetProperty("contents")[0].GetProperty("parts")[1].GetProperty("text").GetString()
            .ShouldBe("Speaker1: quoted");
    }

    [Theory]
    [InlineData(GoogleDialogueSpeakerMode.PartMetadata, "TextLabels")]
    [InlineData(GoogleDialogueSpeakerMode.TextLabels, "PartMetadata")]
    public async Task A_refusal_from_the_model_names_the_other_speaker_format(GoogleDialogueSpeakerMode mode, string other)
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Error(400, "INVALID_ARGUMENT", "Speech metadata is not supported for this model."));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler, options => options.DialogueSpeakerMode = mode);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeDialogueAsync(GoogleSpeechTestData.TwoTurns(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("HTTP 400 (INVALID_ARGUMENT)");
        exception.Message.ShouldContain($"to {other}");
        exception.Message.ShouldNotContain("Speech metadata is not supported");
    }

    [Fact]
    public async Task A_provider_failure_other_than_a_bad_request_has_no_speaker_format_hint()
    {
        var handler = new GoogleStubHandler(_ => GoogleSpeechTestData.Error(503, "UNAVAILABLE", "later"));
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        var exception = await Should.ThrowAsync<TraconException>(
            async () => await synthesizer.SynthesizeDialogueAsync(GoogleSpeechTestData.TwoTurns(), TestContext.Current.CancellationToken));

        exception.Message.ShouldContain("HTTP 503 (UNAVAILABLE)");
        exception.Message.ShouldNotContain(nameof(GoogleSpeechOptions.DialogueSpeakerMode));
    }

    [Fact]
    public async Task A_null_request_is_an_argument_error()
    {
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(new GoogleStubHandler(_ => throw new InvalidOperationException()));

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await synthesizer.SynthesizeDialogueAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_dialogue_with_a_tenant_credential_goes_out_on_that_key_and_the_next_one_on_the_setup_key()
    {
        var handler = Answering();
        var credentials = new List<ModelProviderCredential>();
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler, tenantCredentials: credentials);

        _ = await synthesizer.SynthesizeDialogueAsync(
            GoogleSpeechTestData.TwoTurns() with { Credential = new ModelProviderCredential { ApiKey = GoogleSpeechTestData.TenantKey } },
            TestContext.Current.CancellationToken);
        _ = await synthesizer.SynthesizeDialogueAsync(GoogleSpeechTestData.TwoTurns(), TestContext.Current.CancellationToken);

        handler.Requests.Select(static request => request.ApiKey)
            .ShouldBe([GoogleSpeechTestData.TenantKey, GoogleSpeechTestData.SetupKey]);
        credentials.ShouldHaveSingleItem().ApiKey.ShouldBe(GoogleSpeechTestData.TenantKey);
    }

    [Fact]
    public async Task A_cancelled_dialogue_is_a_cancellation_and_not_a_provider_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new GoogleStubHandler(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });
        var synthesizer = GoogleSpeechTestData.CreateSynthesizer(handler);

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await synthesizer.SynthesizeDialogueAsync(GoogleSpeechTestData.TwoTurns(), cancellation.Token));
    }

    private static GoogleStubHandler Answering()
        => new(_ => GoogleSpeechTestData.Answer(GoogleSpeechTestData.WavWithProvenance(Pcm), "audio/wav"));

    private static SpeechDialogueTurn Turn(string voiceId, string text = "line")
        => new() { Text = text, VoiceId = voiceId };
}
