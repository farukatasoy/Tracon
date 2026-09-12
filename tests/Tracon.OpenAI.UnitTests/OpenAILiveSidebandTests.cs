namespace Tracon.OpenAI.UnitTests;

/// <summary>
/// Verifies that the sideband translates the provider's wire protocol correctly.
/// </summary>
/// <remarks>
/// 🚨 Every frame below is copied from a raw dump of a <strong>real</strong>
/// <c>gpt-live-1</c> session taken on 2026-09-11. Guessing these names was the
/// largest risk in this surface: the field on an append is <c>content</c>, not
/// <c>text</c>, and there is no <c>is_final</c> anywhere — transcripts arrive only
/// as time-ranged deltas.
/// </remarks>
public sealed class OpenAILiveSidebandTests
{
    [Fact]
    public void Session_started_is_recognised()
    {
        var evt = OpenAILiveSideband.Translate(
            """{"event_id":"event_1","type":"session.started","session":{"id":"live_u2_abc","expires_at":1789090198,"model":"gpt-live-1","instructions":"","audio":{"output":{"voice":"marin"}},"delegation":{"type":"client"},"status":"active","input":[]}}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.SessionStarted);
        evt.SessionId.ShouldBe("live_u2_abc");
    }

    [Fact]
    public void An_input_transcript_delta_carries_the_providers_own_timings()
    {
        var evt = OpenAILiveSideband.Translate(
            """{"type":"session.input_transcript.delta","start_ms":600,"end_ms":800,"delta":" Order","event_id":"event_2"}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.InputTranscript);
        evt.Text.ShouldBe(" Order");
        evt.StartMilliseconds.ShouldBe(600);
        evt.EndMilliseconds.ShouldBe(800);
    }

    [Fact]
    public void An_output_transcript_delta_is_distinguished_from_an_input_one()
    {
        var evt = OpenAILiveSideband.Translate(
            """{"type":"session.output_transcript.delta","start_ms":4400,"end_ms":4600,"delta":" Okay,","event_id":"event_3"}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.OutputTranscript);
        evt.Text.ShouldBe(" Okay,");
    }

    [Fact]
    public void A_delegation_carries_its_id_and_its_offset()
    {
        // 🚨 The delegation id sits at delegation.id and is prefixed `item_`, not
        // `deleg_`. The offset is what the conversation is cut at; without it the
        // question "which part of the talk is the task" has no answer.
        var evt = OpenAILiveSideband.Translate(
            """{"type":"session.delegation.created","offset_ms":4200,"delegation":{"id":"item_EMiVQ6of","type":"delegation","target":"client"},"event_id":"event_4"}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.DelegationCreated);
        evt.DelegationId.ShouldBe("item_EMiVQ6of");
        evt.OffsetMilliseconds.ShouldBe(4200);
    }

    [Theory]
    [InlineData("session.thinking.appended")]
    [InlineData("session.commentary.appended")]
    [InlineData("session.instructions.appended")]
    public void Every_append_acknowledgement_is_recognised(string type)
    {
        var evt = OpenAILiveSideband.Translate(
            $$"""{"type":"{{type}}","start_ms":4600,"end_ms":4800,"event_id":"event_5"}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.AppendAccepted);
    }

    [Fact]
    public void Usage_carries_the_billable_duration()
    {
        // 🚨 This is the authoritative number. Tracon does not carry the media
        // and cannot time the session itself.
        var evt = OpenAILiveSideband.Translate(
            """{"type":"session.usage.updated","usage":{"seconds":28.0},"context_window":{"usage_ratio":0.0118},"event_id":"event_6"}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.UsageUpdated);
        evt.Seconds.ShouldBe(28.0m);
    }

    [Fact]
    public void A_close_carries_its_reason_and_the_final_duration()
    {
        var evt = OpenAILiveSideband.Translate(
            """{"event_id":"event_7","type":"session.closed","reason":"connection_lost","session":{"id":"live_u2_abc","model":"gpt-live-1","status":"active"},"usage":{"seconds":15.0}}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.Closed);
        evt.Reason.ShouldBe("connection_lost");
        evt.Seconds.ShouldBe(15.0m);
        evt.SessionId.ShouldBe("live_u2_abc");
    }

    [Fact]
    public void An_error_carries_its_message()
    {
        var evt = OpenAILiveSideband.Translate(
            """{"type":"error","event_id":"event_8","error":{"type":"invalid_request_error","code":"invalid_value","message":"Context append text must not exceed 500 tokens.","param":"content"}}""")
            .ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.Error);
        evt.Text.ShouldBe("Context append text must not exceed 500 tokens.");
    }

    [Theory]
    [InlineData("""{"type":"session.input_audio.append","audio":"AAAA"}""")]
    [InlineData("""{"type":"session.output_audio.delta","delta":"AAAA"}""")]
    public void Mirrored_media_frames_are_ignored(string frame)
    {
        // The provider does mirror the session's audio onto this socket. This phase
        // does not consume it, and emitting an "unknown event" for each frame would
        // drown the pump in noise a consumer cannot act on.
        OpenAILiveSideband.Translate(frame).ShouldBeNull();
    }

    [Fact]
    public void An_unparseable_frame_becomes_an_error_rather_than_an_exception()
    {
        var evt = OpenAILiveSideband.Translate("{not json").ShouldNotBeNull();

        evt.Kind.ShouldBe(LiveVoiceEventKind.Error);
    }

    [Fact]
    public void A_frame_with_no_type_is_ignored()
        => OpenAILiveSideband.Translate("""{"event_id":"event_9"}""").ShouldBeNull();

    [Fact]
    public void The_append_ceiling_is_derived_from_the_measured_token_limit()
    {
        // 🚨 The provider states its limit in TOKENS: measured 2026-09-11, an
        // oversized append answers "Context append text must not exceed 500 tokens."
        // Tracon ships no tokenizer, so the conversion happens once, here, and
        // errs low — Turkish spends far fewer characters per token than English, and
        // a ratio tuned for English would have appends refused in Turkish.
        OpenAILiveOptions.ProviderAppendTokenLimit.ShouldBe(500);
        OpenAILiveOptions.ConservativeCharactersPerToken.ShouldBe(2);

        new OpenAILiveOptions().MaxAppendCharacters.ShouldBe(1000);
    }
}
