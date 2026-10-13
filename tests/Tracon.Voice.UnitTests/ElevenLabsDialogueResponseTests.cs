using Shouldly;
using Tracon.Voice.UnitTests.Infrastructure;

namespace Tracon.Voice.UnitTests;

/// <summary>
/// How the built-in client reads a dialogue response: the audio, each turn's time range,
/// the billed characters and the provider's request id.
/// </summary>
/// <remarks>
/// A segment that names a turn the request did not have, or the wrong voice, would put
/// one speaker's time on another's turn; the whole list is dropped instead, and the
/// audio is still returned (the character alignment rule, <c>ToAlignment</c>).
/// </remarks>
public sealed class ElevenLabsDialogueResponseTests
{
    [Fact]
    public async Task Segments_map_to_the_turns_that_spoke_them()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.TimestampedResponse(DialogueTestData.TwoSegments));
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns(includeTimestamps: true),
            TestContext.Current.CancellationToken);

        result.Audio.Data.ToArray().ShouldBe(DialogueTestData.Mp3Bytes);
        result.Audio.Alignment.ShouldNotBeNull().Count.ShouldBe(2);

        var segments = result.Segments.ShouldNotBeNull();
        segments.Count.ShouldBe(2);
        segments[0].ShouldBe(new SpeechDialogueSegment
        {
            TurnIndex = 0,
            VoiceId = "voice-a",
            Start = TimeSpan.Zero,
            End = TimeSpan.FromSeconds(0.96),
        });
        segments[1].TurnIndex.ShouldBe(1);
        segments[1].VoiceId.ShouldBe("voice-b");
        segments[1].Start.ShouldBe(TimeSpan.FromSeconds(0.96));
        segments[1].End.ShouldBe(TimeSpan.FromSeconds(1.68));
    }

    [Fact]
    public async Task Two_segments_for_one_turn_are_both_kept()
    {
        // The provider documents a segment as a part of a turn; the count is not the turns'.
        const string split =
            """
            [
              {"voice_id": "voice-a", "start_time_seconds": 0.0, "end_time_seconds": 0.4, "dialogue_input_index": 0},
              {"voice_id": "voice-a", "start_time_seconds": 0.4, "end_time_seconds": 0.96, "dialogue_input_index": 0},
              {"voice_id": "voice-b", "start_time_seconds": 0.96, "end_time_seconds": 1.68, "dialogue_input_index": 1}
            ]
            """;

        var handler = new StubHttpMessageHandler(_ => DialogueTestData.TimestampedResponse(split));
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns(includeTimestamps: true),
            TestContext.Current.CancellationToken);

        result.Segments.ShouldNotBeNull().Select(static segment => segment.TurnIndex).ShouldBe([0, 0, 1]);
    }

    [Theory]
    [InlineData("""[{"voice_id": "voice-a", "start_time_seconds": 0.0, "end_time_seconds": 0.9, "dialogue_input_index": 2}]""")]
    [InlineData("""[{"voice_id": "voice-a", "start_time_seconds": 0.0, "end_time_seconds": 0.9, "dialogue_input_index": -1}]""")]
    [InlineData("""[{"voice_id": "voice-b", "start_time_seconds": 0.0, "end_time_seconds": 0.9, "dialogue_input_index": 0}]""")]
    [InlineData("""[{"voice_id": "voice-a", "start_time_seconds": 0.9, "end_time_seconds": 0.1, "dialogue_input_index": 0}]""")]
    [InlineData("""[{"voice_id": "voice-a", "end_time_seconds": 0.9, "dialogue_input_index": 0}]""")]
    [InlineData("""[{"start_time_seconds": 0.0, "end_time_seconds": 0.9, "dialogue_input_index": 0}]""")]
    [InlineData("""[{"voice_id": "voice-a", "start_time_seconds": 0.0, "end_time_seconds": 0.9}]""")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task A_report_that_does_not_match_the_turns_gives_no_segments_and_keeps_the_audio(string voiceSegments)
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.TimestampedResponse(voiceSegments));
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns(includeTimestamps: true),
            TestContext.Current.CancellationToken);

        result.Segments.ShouldBeNull();
        result.Audio.Data.ToArray().ShouldBe(DialogueTestData.Mp3Bytes);
    }

    [Fact]
    public async Task One_bad_segment_drops_the_whole_list()
    {
        const string oneBad =
            """
            [
              {"voice_id": "voice-a", "start_time_seconds": 0.0, "end_time_seconds": 0.96, "dialogue_input_index": 0},
              {"voice_id": "voice-a", "start_time_seconds": 0.96, "end_time_seconds": 1.68, "dialogue_input_index": 1}
            ]
            """;

        var handler = new StubHttpMessageHandler(_ => DialogueTestData.TimestampedResponse(oneBad));
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns(includeTimestamps: true),
            TestContext.Current.CancellationToken);

        result.Segments.ShouldBeNull("a shifted voice would put one speaker's time on another's turn");
    }

    [Fact]
    public async Task Without_timestamps_the_raw_audio_is_returned_and_there_are_no_segments()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken);

        result.Audio.Data.ToArray().ShouldBe(DialogueTestData.Mp3Bytes);
        result.Audio.MediaType.ShouldBe("audio/mpeg");
        result.Audio.Alignment.ShouldBeNull();
        result.Segments.ShouldBeNull();
    }

    [Fact]
    public async Task The_provider_count_is_billed_and_marked_as_the_providers()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse(characterCost: "3", requestId: "req-42"));
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken);

        result.Audio.CharactersBilled.ShouldBe(3);
        result.Audio.UsageSource.ShouldBe(SpeechUsageSource.Provider);
        result.Audio.ProviderRequestId.ShouldBe("req-42");
    }

    [Fact]
    public async Task Without_a_provider_count_the_turns_characters_are_an_estimate()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(DialogueTestData.TwoTurns(), TestContext.Current.CancellationToken);

        // "Merhaba." (8) + "Selam." (6).
        result.Audio.CharactersBilled.ShouldBe(14);
        result.Audio.UsageSource.ShouldBe(SpeechUsageSource.Estimated);
        result.Audio.ProviderRequestId.ShouldBeNull();
    }

    [Fact]
    public async Task The_timestamped_path_reads_the_count_and_the_request_id_too()
    {
        var handler = new StubHttpMessageHandler(
            _ => DialogueTestData.TimestampedResponse(DialogueTestData.TwoSegments, characterCost: "14", requestId: "req-7"));
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns(includeTimestamps: true),
            TestContext.Current.CancellationToken);

        result.Audio.CharactersBilled.ShouldBe(14);
        result.Audio.UsageSource.ShouldBe(SpeechUsageSource.Provider);
        result.Audio.ProviderRequestId.ShouldBe("req-7");
    }

    [Fact]
    public async Task Raw_pcm_output_is_not_labelled_as_a_container_format()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler);

        var result = await client.SynthesizeDialogueAsync(
            DialogueTestData.TwoTurns() with { OutputFormat = "pcm_24000" },
            TestContext.Current.CancellationToken);

        result.Audio.MediaType.ShouldBe("application/octet-stream");
    }

    [Fact]
    public async Task The_single_voice_path_reports_the_request_id_too()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse(requestId: "req-single"));
        using var client = DialogueTestData.CreateClient(handler);

        var audio = await client.SynthesizeAsync(new SpeechRequest { Text = "hello" }, TestContext.Current.CancellationToken);

        audio.ProviderRequestId.ShouldBe("req-single");
    }

    [Fact]
    public void The_capabilities_report_the_provider_limits_and_the_configured_character_limit()
    {
        var handler = new StubHttpMessageHandler(_ => DialogueTestData.RawResponse());
        using var client = DialogueTestData.CreateClient(handler, static options => options.MaxDialogueCharactersPerRequest = 1900);

        client.DialogueCapabilities.ShouldBe(new SpeechDialogueCapabilities
        {
            MaxVoices = 10,
            MaxCharactersPerRequest = 1900,
            SupportsSegmentTimestamps = true,
        });
        ((ISpeechDialogueSynthesizer)client).ProviderName.ShouldBe("elevenlabs");
        ((ISpeechDialogueSynthesizer)client).SupportsTenantCredentials.ShouldBeFalse();
    }
}
